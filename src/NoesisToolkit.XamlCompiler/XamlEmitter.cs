using System;
using System.Collections.Generic;
using System.Linq;
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

    public readonly List<string> Errors = new List<string>();

    public readonly List<string> DeadMarkup = new List<string>();

    public readonly BindingTally Tally = new BindingTally();

    readonly List<string> _lines = new List<string>();

    readonly List<string> _deferredGlobal = new List<string>();

    readonly List<string> _dictionaries = new List<string>();

    bool _usesResourceLookup;

    string? _dictionary => _dictionaries.Count == 0 ? null : _dictionaries[_dictionaries.Count - 1];

    readonly List<INamedTypeSymbol?> _styleTargets = new List<INamedTypeSymbol?>();

    readonly List<string> _templates = new List<string>();

    INamedTypeSymbol? _rootClass;

    readonly List<Dictionary<string, XElement>> _nameScopes =
        new List<Dictionary<string, XElement>>();

    int _next;

    INamedTypeSymbol? StyleTarget =>
        _styleTargets.Count == 0 ? null : _styleTargets[_styleTargets.Count - 1];

    string NextName(string hint)
    {
        var clean = new string(hint.Where(char.IsLetterOrDigit).ToArray());
        return $"__{clean.ToLowerInvariant()}{_next++}";
    }

    /// <summary>Records every x:Name in one template so a Setter TargetName resolves even when
    /// the trigger precedes the element. Scoped per template: one file reuses names freely.</summary>
    void PushNameScope(XElement root)
    {
        var scope = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var element in NameScopeOf(root))
        {
            var name = element.Attribute(XName.Get("Name", XamlTypeResolver.DirectiveNs));
            if (name is null)
                continue;

            if (resolver.SymbolOf(element) is not null)
                scope[name.Value] = element;
        }

        _nameScopes.Add(scope);
    }

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

    static bool PushesNameScope(INamedTypeSymbol symbol) =>
        XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.Style")
        || XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate");

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

        // Cleared first: a hot reload runs this again over a root whose names point at the old elements.
        _lines.Add("global::Noesis.NameScope.SetNameScope(this, null);");
        _lines.Add($"var {RootScope} = new global::Noesis.NameScope();");
        _lines.Add($"global::Noesis.NameScope.SetNameScope(this, {RootScope});");
        PlanTemplateBindings(root, null);

        foreach (var attribute in root.Attributes())
            ApplyAttribute(root, "this", rootType, attribute);

        ApplyChildren(root, "this", rootType);

        Finish();
        return _lines;
    }

    public List<string> EmitDictionary(XElement root)
    {
        PushNameScope(root);
        var name = NextName("dict");
        _dictionaries.Add(name);
        _lines.Add($"var {name} = new global::Noesis.ResourceDictionary();");
        PlanTemplateBindings(root, null);
        EmitDictionaryBody(root, name);

        Finish();
        _lines.Add($"return {name};");
        return _lines;
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
    /// would otherwise mark -- the diagnostics, the reason, the dead markup and the tally -- are
    /// restored when the scope closes.</summary>
    Speculation Speculate() => new Speculation(this);

    readonly struct Speculation : IDisposable
    {
        readonly XamlEmitter _emitter;
        readonly int _errors;
        readonly int _dead;
        readonly string? _refusal;
        readonly BindingTally _tally;

        internal Speculation(XamlEmitter emitter)
        {
            _emitter = emitter;
            _errors = emitter.Errors.Count;
            _dead = emitter.DeadMarkup.Count;
            _refusal = emitter._refusal;
            _tally = emitter.Tally.Snapshot();
        }

        public void Dispose()
        {
            _emitter.Errors.RemoveRange(_errors, _emitter.Errors.Count - _errors);
            _emitter.DeadMarkup.RemoveRange(_dead, _emitter.DeadMarkup.Count - _dead);
            _emitter._refusal = _refusal;
            _emitter.Tally.Restore(_tally);
        }
    }
}
