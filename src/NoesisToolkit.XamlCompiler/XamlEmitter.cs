using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace NoesisToolkit.Xaml;

/// <summary>Compiles one XAML document into the C# statements that rebuild its object
/// graph. One instance per file: the statement list and the scope stacks are its whole state.</summary>
sealed partial class XamlEmitter(
    XamlTypeResolver resolver,
    string packPrefix = "",
    string projectDir = "",
    string filePath = ""
)
{
    /// <summary>False where the runtime the emitted graph names is not referenced, which makes a
    /// compiled binding impossible rather than merely unresolvable.</summary>
    public bool CompiledBindingsAvailable { get; init; } = true;

    public System.Threading.CancellationToken Cancellation { get; init; }

    ErrorList? _errors;

    public ErrorList Errors => _errors ??= new ErrorList(CurrentSite);

    XElement? _element;

    (int Line, int Column) CurrentSite()
    {
        IXmlLineInfo? at =
            _attribute is { } attribute && ((IXmlLineInfo)attribute).HasLineInfo()
                ? attribute
                : _element;
        return at is { } info && info.HasLineInfo() ? (info.LineNumber, info.LinePosition) : (0, 0);
    }

    public readonly List<string> DeadMarkup = new List<string>();

    public readonly BindingTally Tally = new BindingTally();

    readonly List<string> _lines = new List<string>();

    readonly List<string> _deferredGlobal = new List<string>();

    readonly List<string> _dictionaries = new List<string>();

    bool _usesResourceLookup;

    readonly List<INamedTypeSymbol?> _styleTargets = new List<INamedTypeSymbol?>();

    readonly List<string> _templates = new List<string>();

    INamedTypeSymbol? _rootClass;

    readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.Ordinal);

    public void HideRootMembers(IEnumerable<string> names) => _hidden.UnionWith(names);

    // The accessor is get-only and typed as the element, so the root's own member is reached past it.
    string Member(string target, string name) =>
        target == "this" && _hidden.Contains(name) && _rootClass?.BaseType is { } inherited
            ? $"(({XamlTypeResolver.Fqn(inherited)})this).{name}"
            : $"{target}.{name}";

    readonly List<Dictionary<string, XElement>> _nameScopes =
        new List<Dictionary<string, XElement>>();

    int _next;

    INamedTypeSymbol? StyleTarget =>
        _styleTargets.Count == 0 ? null : _styleTargets[_styleTargets.Count - 1];

    string NextName(string hint)
    {
        var clean = new string(hint.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        // Card1 then 0 would spell Card then 10.
        var separator = clean.Length > 0 && char.IsDigit(clean[clean.Length - 1]) ? "_" : "";
        return $"__{clean}{separator}{_next++}";
    }

    /// <summary>Records every name in one template so a Setter TargetName resolves even when
    /// the trigger precedes the element. Scoped per template: one file reuses names freely.</summary>
    void PushNameScope(XElement root)
    {
        var scope = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var element in NameScopeOf(root))
        {
            if (ScopedName(element) is not { } name)
                continue;

            if (resolver.SymbolOf(element) is not null)
                scope[name] = element;
        }

        _nameScopes.Add(scope);
    }

    // The parser registers a plain Name as it does x:Name, but no field backs it.
    string? ScopedName(XElement element) =>
        element.Attribute(XName.Get("Name", XamlTypeResolver.DirectiveNs))?.Value
        ?? (
            element.Attribute("Name") is { } plain
            && resolver.SymbolOf(element) is { } symbol
            && XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkElement")
                ? plain.Value.Trim()
                : null
        );

    IEnumerable<XElement> NameScopeOf(XElement root)
    {
        var pending = new Stack<XElement>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var element = pending.Pop();
            yield return element;

            foreach (var child in element.Elements().Reverse())
            {
                if (resolver.SymbolOf(child) is not { } symbol || !PushesNameScope(symbol))
                    pending.Push(child);
            }
        }
    }

    // A Style opens none: the engine registers a name inside one into the scope around it.
    internal static bool PushesNameScope(INamedTypeSymbol symbol) =>
        XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate");

    XElement? LookupNamed(string name)
    {
        for (var i = _nameScopes.Count - 1; i >= 0; i--)
        {
            if (_nameScopes[i].TryGetValue(name, out var element))
                return element;
        }

        return null;
    }

    INamedTypeSymbol? LookupName(string name) =>
        LookupNamed(name) is { } element ? resolver.SymbolOf(element) : null;

    /// <summary>Builds into an existing instance: an x:Class root IS the generated partial class.</summary>
    public List<string> EmitRoot(XElement root, INamedTypeSymbol rootType)
    {
        _rootClass = rootType;
        PushNameScope(root);

        // A dictionary is no DependencyObject, so it cannot hold a name scope.
        var dictionary = XamlTypeResolver.DerivesFrom(
            rootType,
            "global::Noesis.ResourceDictionary"
        );

        // Cleared first: a hot reload runs this again over a root whose names point at the old elements.
        if (!dictionary)
            _lines.Add("global::Noesis.NameScope.SetNameScope(this, null);");
        _lines.Add($"var {RootScope} = new global::Noesis.NameScope(this);");
        if (!dictionary)
            _lines.Add($"global::Noesis.NameScope.SetNameScope(this, {RootScope});");
        PlanTemplateBindings(root, null);

        foreach (var attribute in root.Attributes())
            ApplyAttribute(root, "this", rootType, attribute);

        if (dictionary)
            EmitScopedDictionaryBody(root, "this");
        else
            ApplyChildren(root, "this", rootType);

        Finish();
        return _lines;
    }

    public List<string> EmitDictionary(XElement root)
    {
        PushNameScope(root);
        var name = NextName("dict");
        _dictionaries.Add(name);

        var type = DictionaryType(root);
        var source = root.Name.LocalName == "ResourceDictionary" ? root.Attribute("Source") : null;
        _lines.Add(
            source is null
                ? $"var {name} = new {XamlTypeResolver.Fqn(type)}();"
                : $"var {name} = {SourcedDictionary(source.Value)};"
        );
        PlanTemplateBindings(root, null);

        foreach (var attribute in root.Attributes().Where(a => a != source))
            ApplyAttribute(root, name, type, attribute);

        EmitDictionaryBody(root, name);

        Finish();
        _lines.Add($"return {name};");
        return _lines;
    }

    INamedTypeSymbol DictionaryType(XElement root)
    {
        var plain = resolver.Resolve(XamlTypeResolver.PresentationNs, "ResourceDictionary")!;
        if (
            resolver.SymbolOf(root) is not { } type
            || SymbolEqualityComparer.Default.Equals(type, plain)
        )
            return plain;

        if (type.IsAbstract || !resolver.HasConstructor(type) || !resolver.IsAccessible(type, null))
        {
            Fail(
                $"<{root.Name.LocalName}> roots a dictionary that generated code cannot construct"
            );
            return plain;
        }

        return type;
    }

    void Finish()
    {
        if (_usesResourceLookup)
            _lines.Insert(1, ResourcesHelper);

        EmitLookupHelpers();

        if (_usesTemplated)
            _lines.Add(TemplatedHelper);

        if (_deferredGlobal.Count > 0)
        {
            _lines.Add("__XamlResources.Defer(__root =>");
            _lines.Add("{");
            _lines.AddRange(_deferredGlobal);
            _lines.Add("});");
        }

        Settled();
    }

    // An element that collected wiring and never emitted it would drop those bindings in silence.
    void Settled()
    {
        if (HasUnflushedWires)
            Errors.Add("an element collected compiled bindings its wiring never emitted");
    }

    bool PushStyleTarget(XElement element, INamedTypeSymbol symbol)
    {
        var styles =
            XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.Style")
            || XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate");

        if (!styles)
            return false;

        var attribute = element.Attribute("TargetType");
        INamedTypeSymbol? target = null;

        if (attribute is not null)
            target = ResolveTypeSymbol(element, attribute.Value);
        else if (StyleTarget is not null)
            target = StyleTarget;

        _styleTargets.Add(target);
        return true;
    }

    const string RootScope = "__scope";

    string? Fail(string message)
    {
        Errors.Add(message);
        return null;
    }

    /// <summary>Deciding whether a construct CAN compile is speculative: one that refuses stays
    /// native and is not a fault. Speculation is a property of the call, so the channels a refusal
    /// would otherwise mark -- the diagnostics, the reason, the dead markup, the tally and the
    /// trimming roots -- are restored when the scope closes.</summary>
    Speculation Speculate() => new Speculation(this);

    readonly struct Speculation : IDisposable
    {
        readonly XamlEmitter _emitter;
        readonly int _errors;
        readonly int _dead;
        readonly int _native;
        readonly int _cells;
        readonly int _walked;
        readonly int _heldBack;
        readonly string? _refusal;
        readonly BindingTally _tally;
        readonly RootsMark _roots;

        internal Speculation(XamlEmitter emitter)
        {
            _emitter = emitter;
            _errors = emitter.Errors.Count;
            _dead = emitter.DeadMarkup.Count;
            _native = emitter.LeftNative.Count;
            _cells = emitter.NativeCells.Count;
            _walked = emitter._walked.Count;
            _heldBack = emitter._heldBack.Count;
            _refusal = emitter._refusal;
            _tally = emitter.Tally.Snapshot();
            _roots = emitter.MarkRoots();
        }

        public void Dispose()
        {
            _emitter.Errors.RemoveRange(_errors, _emitter.Errors.Count - _errors);
            _emitter.DeadMarkup.RemoveRange(_dead, _emitter.DeadMarkup.Count - _dead);
            _emitter.LeftNative.RemoveRange(_native, _emitter.LeftNative.Count - _native);
            _emitter.NativeCells.RemoveRange(_cells, _emitter.NativeCells.Count - _cells);
            _emitter._walked.Truncate(_walked);
            _emitter._heldBack.Truncate(_heldBack);
            _emitter._refusal = _refusal;
            _emitter.Tally.Restore(_tally);
            _emitter.RestoreRoots(_roots);
        }
    }
}

sealed class ErrorList(Func<(int Line, int Column)> site) : IEnumerable<string>
{
    readonly List<string> _messages = new List<string>();

    readonly List<(int Line, int Column)> _sites = new List<(int Line, int Column)>();

    public int Count => _messages.Count;

    public IReadOnlyList<string> Messages => _messages;

    public IReadOnlyList<(int Line, int Column)> Sites => _sites;

    public void Add(string message) => Add(message, site());

    public void Add(string message, (int Line, int Column) at)
    {
        _messages.Add(message);
        _sites.Add(at);
    }

    public void AddRange(IEnumerable<string> messages)
    {
        foreach (var message in messages)
            Add(message);
    }

    public void RemoveRange(int index, int count)
    {
        _messages.RemoveRange(index, count);
        _sites.RemoveRange(index, count);
    }

    public IEnumerator<string> GetEnumerator() => _messages.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
