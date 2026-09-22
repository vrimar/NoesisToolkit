using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

// Anything that does not resolve whole stays a Noesis.Binding: a compiled binding that guessed
// would be worse than a reflective one.
sealed partial class XamlEmitter
{
    const string CompiledBindingFqn = "global::NoesisToolkit.Mvvm.CodeGen.CompiledBinding";

    const string BindingHopFqn = "global::NoesisToolkit.Mvvm.CodeGen.BindingHop";

    const string SetupFqn = "global::NoesisToolkit.Mvvm.CodeGen.CompiledBindingSetup";

    const string SpecFqn = "global::NoesisToolkit.Mvvm.CodeGen.CompiledBindingSpec";

    const string CompiledMultiBindingFqn =
        "global::NoesisToolkit.Mvvm.CodeGen.CompiledMultiBinding";

    const string PartFqn = "global::NoesisToolkit.Mvvm.CodeGen.CompiledBindingPart";

    const string MultiSpecFqn = "global::NoesisToolkit.Mvvm.CodeGen.CompiledMultiBindingSpec";

    int _setups;

    /// <summary>The dependency property a binding writes, named the same way whether the document
    /// wrote it as a plain attribute or as an attached one.</summary>
    readonly struct BindingSlot(
        string reference,
        ITypeSymbol type,
        string name,
        bool twoWayByDefault,
        string? assign = null,
        ITypeSymbol? registered = null
    )
    {
        public string Reference { get; } = reference;
        public ITypeSymbol Type { get; } = type;
        public string Name { get; } = name;
        public bool TwoWayByDefault { get; } = twoWayByDefault;

        /// <summary>Set where the property has to be written through its own accessor rather than
        /// SetValue.</summary>
        public string? Assign { get; } = assign;

        /// <summary>Set where the native side registered the property under another type than the
        /// CLR one it shows.</summary>
        public ITypeSymbol? Registered { get; } = registered;
    }

    // SetValue dispatches on the slot's declared type: an enum rides a 64-bit slot the native side
    // rejects silently, and a GridLength has no typed route at all and crashes the process, so both
    // are written through their own accessor instead.
    static bool NeedsAccessor(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Enum || XamlTypeResolver.Fqn(type) == "global::Noesis.GridLength";

    // The CLR surface lies about the registered uint type, so a typed SetValue dies silently.
    static readonly (string Owner, string Name)[] MismatchedNativeSlots =
    {
        ("global::Noesis.UniformGrid", "Rows"),
        ("global::Noesis.UniformGrid", "Columns"),
        ("global::Noesis.UniformGrid", "FirstColumn"),
        ("global::Noesis.PasswordBox", "ShowLastCharacterDuration"),
        ("global::Noesis.PasswordBox", "PasswordChar"),
    };

    // PropertyType throws for these, so the managed side can neither read nor write such a slot.
    static readonly string[] UnregisteredNativeEnums =
    {
        "global::Noesis.GridResizeDirection",
        "global::Noesis.GridResizeBehavior",
        "global::Noesis.SweepDirection",
        "global::Noesis.BlendingMode",
        "global::Noesis.InputScope",
        "global::Noesis.Interactivity.ForwardChaining",
    };

    bool Unregistered(ITypeSymbol type)
    {
        if (Array.IndexOf(UnregisteredNativeEnums, XamlTypeResolver.Fqn(Wrapped(type) ?? type)) < 0)
            return false;

        Note("native-enum-unregistered");
        return true;
    }

    static bool Listed(IPropertySymbol property, (string Owner, string Name)[] table)
    {
        foreach (var (owner, name) in table)
        {
            if (
                property.Name == name
                && XamlTypeResolver.DerivesFrom(property.ContainingType, owner)
            )
                return true;
        }

        return false;
    }

    static bool MismatchedSlot(IPropertySymbol property) => Listed(property, MismatchedNativeSlots);

    BindingSlot? PlainSlot(IPropertySymbol property)
    {
        if (
            resolver.FindDependencyPropertyOwner(property.ContainingType, property.Name)
            is not { } owner
        )
        {
            Note("target-is-not-a-dependency-property");
            return null;
        }

        if (Unregistered(property.Type))
            return null;

        string? assign = null;
        if (NeedsAccessor(property.Type) || MismatchedSlot(property))
        {
            if (property.SetMethod is null)
            {
                Note("accessor-slot-has-no-setter");
                return null;
            }

            // The slot's own default arrives boxed as the registered uint.
            var narrowed = MismatchedSlot(property)
                ? $"__v is {XamlTypeResolver.Fqn(property.Type)} __a ? __a : __v is uint __u"
                    + $" ? ({XamlTypeResolver.Fqn(property.Type)})__u"
                    + $" : default({XamlTypeResolver.Fqn(property.Type)})"
                : NarrowTo(property.Type, "__v", "__a");

            assign =
                $"(__t, __v) => (({XamlTypeResolver.Fqn(property.ContainingType)})__t)."
                + $"{property.Name} = {narrowed}";
        }

        return new BindingSlot(
            SlotReference(owner, property.Name),
            property.Type,
            property.Name,
            BindsTwoWayByDefault(property),
            assign,
            MismatchedSlot(property) ? resolver.UInt32Type : null
        );
    }

    // The slot's own type is what decides whether the source fits, so a property with no type to
    // read is nothing to check against.
    BindingSlot? AttachedSlot(INamedTypeSymbol owner, string name)
    {
        if (resolver.FindAttachedValueType(owner, name) is not { } value)
        {
            Note("attached-target-has-no-value-type");
            return null;
        }

        if (resolver.FindDependencyPropertyOwner(owner, name) is null)
            Note("attached-target-is-not-a-dependency-property");

        if (Unregistered(value))
            return null;

        return resolver.FindDependencyPropertyOwner(owner, name) is not null
            ? new BindingSlot(
                SlotReference(owner, name),
                value,
                name,
                TwoWayByDefault.Any(t =>
                    t.Property == name && XamlTypeResolver.DerivesFrom(owner, t.Owner)
                ),
                NeedsAccessor(value)
                    ? $"(__t, __v) => {XamlTypeResolver.Fqn(owner)}.Set{name}"
                        + $"(__t, {NarrowTo(value, "__v", "__a")})"
                    : null
            )
            : null;
    }

    readonly Dictionary<XElement, string> _elementVars = new Dictionary<XElement, string>();

    bool _nativeByDesign;

    string? _refusal;

    /// <summary>Why the binding being emitted could not compile. The first reason recorded wins,
    /// which is the innermost one, because that is the one that names the actual obstacle.</summary>
    void Note(string reason)
    {
        if (!_quiet)
            _refusal ??= reason;
    }

    // Context resolution walks other elements' bindings; what stops one of those is not what stopped
    // the binding being emitted, and recording it would mislabel this one.
    bool _quiet;

    bool No(string reason)
    {
        Note(reason);
        return false;
    }

    void BeginAttempt() => _refusal = null;

    /// <summary>Tallies the fallback the caller just emitted, under the reason that explains it.</summary>
    void ClassifyFallback()
    {
        if (_nativeByDesign)
        {
            _nativeByDesign = false;
            Tally.NativeByDesign++;
            return;
        }

        var reason = _refusal ?? "unclassified";

        // Read without being consumed: one refusal has to explain every remaining child.
        if (_inMultiBinding > 0)
        {
            Tally.Fell(
                reason.StartsWith(MultiBindingReason, StringComparison.Ordinal)
                    ? reason
                    : MultiBindingReason + reason
            );
            return;
        }

        _refusal = null;
        Tally.Fell(reason);
    }

    const string MultiBindingReason = "multi-binding-";

    readonly Dictionary<XElement, List<string>> _pendingWires =
        new Dictionary<XElement, List<string>>();

    /// <summary>Holds a wiring statement until the element it targets has taken its last child.</summary>
    void AddPendingWire(XElement element, string statement)
    {
        if (!_pendingWires.TryGetValue(element, out var pending))
            _pendingWires[element] = pending = new List<string>();

        pending.Add(statement);
    }

    /// <summary>The emitted reference to the DependencyProperty field backing a named property.</summary>
    static string SlotReference(ITypeSymbol owner, string name) =>
        $"{XamlTypeResolver.Fqn(owner)}.{name}Property";

    static object? NamedValue(MarkupCall call, string key) =>
        call.Named.FirstOrDefault(p => p.Key == key).Value;

    static bool IsNativeSlot(string? reference) =>
        reference is not null && reference.StartsWith("global::Noesis.", StringComparison.Ordinal);

    bool TryEmitCompiledBinding(
        XElement element,
        string target,
        INamedTypeSymbol type,
        BindingSlot slot,
        MarkupCall call
    )
    {
        BeginAttempt();

        if (!OptedIntoCompiledBindings(element))
            return No("opted-out");

        var anchor = target;
        var anchorType = type;
        string? receiver = null;
        if (!XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement"))
        {
            if (DetachedReceiver(element) is not { } detached)
                return No("target-not-an-element");

            // The accessor route writes through the element type, which a detached receiver is not.
            if (slot.Assign is not null)
                return No("detached-receiver-needs-accessor");

            anchor = detached.Anchor;
            anchorType = detached.AnchorType;
            receiver = detached.Receiver;
        }

        if (NamedByTemplateTrigger(element, slot.Name))
            return No("binding-outranked-by-template-trigger");

        if (PresenterContentInTemplate(type, slot.Name))
            return No("presenter-content-in-a-template");

        if (PlainPath(call) is not { } path)
            return No("path-not-plain");

        // Self is the attached object, not its host.
        if (receiver is not null && SelfRelative(call))
            return No("self-is-the-attached-object");

        if (ResolveSource(element, call) is not { } source)
            return No("source-unresolved");

        // Writing the slot the path is read through would feed the next read its own result.
        if (source.Resolver is null && slot.Name == "DataContext")
            return No("target-is-the-data-context");

        if (
            source.Resolver is null
            && XamlTypeResolver.DerivesFrom(anchorType, "global::Noesis.ContentPresenter")
        )
            return No("target-adopts-its-content-as-data-context");

        var walked = ResolvePath(source, path);

        // An identity binding has no hops to type, and an object slot holds whatever arrives, so an
        // undeclared context costs it nothing.
        if (
            walked is null
            && path.Length == 0
            && source.Resolver is null
            && slot.Type.SpecialType == SpecialType.System_Object
        )
            walked = new ResolvedPath(null, new List<Hop>(), resolver.ObjectType);

        if (walked is not { } resolved)
            return No("path-unresolved");

        var converter = ConverterExpression(element, call);

        // Named but unresolved: compiling without it would silently drop the conversion.
        if (converter is null && call.Named.Any(p => p.Key == "Converter"))
            return No("converter-unresolved");

        if (NamedValue(call, "ConverterParameter") is MarkupCall)
            return No("converter-parameter-is-markup");

        var formatted = call.Named.Any(p => p.Key == "StringFormat");

        // A converter or a format is what turns the source value into the slot's type.
        var shaped = converter is not null || formatted;
        string? coercion = null;
        if (!shaped && !Assignable(resolved.Type, slot.Type))
        {
            coercion = Conversion(resolved.Type, slot.Type);
            if (coercion is null)
                return No("value-does-not-fit-the-slot");
        }

        // A deliberate refusal, not a gap: nothing managed is in this path to compile.
        if (
            receiver is null
            && source.Structural
            && !shaped
            && coercion is null
            && resolved.Hops.Count == 0
            && IsNativeSlot(resolved.Slot)
            && IsNativeSlot(slot.Reference)
        )
        {
            _nativeByDesign = true;
            return false;
        }

        if (
            Shape(call, slot.Type, resolved.Type, converter is not null, coercion)
            is not { } convert
        )
            return No("shape-unsupported");

        // A format is one-way arithmetic, and so is most coercion; the numeric/enum widening is the
        // exception, because the cast back is exact enough that the native engine runs it two-way.
        // A detached receiver is one-way by construction.
        var slotNumber = Wrapped(slot.Type) ?? slot.Type;
        var reversible =
            coercion is not null
            && Wrapped(resolved.Type) is null
            && IsNumeric(slotNumber)
            && (
                IsNumericOrEnum(resolved.Type)
                || (OperatorNumber(resolved.Type) is { } via && CastsBack(resolved.Type, via))
            );
        var write =
            receiver is not null || formatted || (coercion is not null && !reversible)
                ? null
                : WriteExpression(resolved, reversible ? slot.Type : null);
        var unwritable =
            receiver is not null ? "write-back-through-a-detached-receiver"
            : formatted ? "write-back-through-a-format"
            : coercion is not null ? "write-back-through-a-conversion"
            : resolved.Hops.Count == 0 ? "write-back-through-a-source-property"
            : "write-back-has-no-setter";
        if (Direction(call, slot, write is not null, unwritable) is not { } mode)
            return false;

        if (Trigger(call) is not { } trigger)
            return No("update-trigger-unsupported");

        var fields = SourceFields(source, resolved);
        var lane = converter is null
            ? TextLane(resolved, slot, call, formatted, source.Resolver)
                ?? (
                    formatted
                        ? null
                        : Lane(
                            resolved,
                            slot,
                            coercion is not null,
                            write is not null,
                            source.Resolver
                        )
                )
            : null;

        if (lane is not null)
        {
            fields.Add($"Lane = {lane}");
        }
        else
        {
            fields.Add($"Convert = {convert}");

            if (slot.Assign is not null)
                fields.Add($"Assign = {slot.Assign}");

            if (converter is not null)
            {
                fields.Add($"Converter = {converter}");
                fields.Add($"ConverterParameter = {ConverterParameter(call)}");
                fields.Add($"TargetType = typeof({ConverterTargetType(slot)})");
            }

            if (write is not null)
                fields.Add($"Write = {write}");
        }

        if (mode.Length > 0)
            fields.Add($"Mode = global::Noesis.BindingMode.{mode}");

        if (trigger.Length > 0)
            fields.Add($"Trigger = global::Noesis.UpdateSourceTrigger.{trigger}");

        var spec = Shared("spec", $"new {SpecFqn} {{ {string.Join(", ", fields.ToArray())} }}");
        Tally.Compiled++;

        if (receiver is not null && _markedReceivers.Add(element))
            _lines.Add($"{CompiledBindingFqn}.MarkReceiver({target}, {ReceiverKey(element)});");

        EmitCompiledBind(
            anchor,
            r =>
                receiver is null
                    ? $"{CompiledBindingFqn}.Bind({r}, {slot.Reference}, {spec})"
                    : $"{CompiledBindingFqn}.Bind({r}, {receiver}, {slot.Reference}, {spec})"
        );
        return true;
    }

    // An enum slot keeps the boxed route: it is written through its own accessor.
    string? Lane(
        ResolvedPath resolved,
        BindingSlot slot,
        bool converted,
        bool writes,
        string? resolver
    )
    {
        if (resolved.Hops.Count == 0 || slot.Assign is not null || slot.Registered is not null)
            return null;

        if (
            slot.Type.SpecialType
            is not (
                SpecialType.System_Boolean
                or SpecialType.System_Int32
                or SpecialType.System_Int64
                or SpecialType.System_Single
                or SpecialType.System_Double
            )
        )
            return null;

        var last = resolved.Hops[resolved.Hops.Count - 1];
        var from = last.Type;
        var exact = SymbolEqualityComparer.Default.Equals(from, slot.Type);
        if (!exact && !(converted && (IsNumericOrEnum(from) || Operates(from, slot.Type))))
            return null;

        var owner = XamlTypeResolver.Fqn(last.Owner);
        var fromFqn = XamlTypeResolver.Fqn(from);
        var slotFqn = XamlTypeResolver.Fqn(slot.Type);
        var convert = exact ? "__t" : CastTo(slot.Type, from, "__t");
        var writeBack = writes
            ? $"static (__o, __w) => __o.{last.Name} = {(exact ? "__w" : CastTo(from, from, "__w"))}"
            : "null";
        var guarded = resolver is not null && resolved.Hops.Count == 1 ? "true" : "false";

        return $"{BindingLaneFqn}.Of<{owner}, {fromFqn}, {slotFqn}>("
            + $"static __o => __o.{last.Name}, static __t => {convert}, {writeBack}, {guarded})";
    }

    const string BindingLaneFqn = "global::NoesisToolkit.Mvvm.CodeGen.BindingLane";

    const string SlotTextFqn = "global::NoesisToolkit.Mvvm.CodeGen.SlotText";

    // A number shown as text is formatted into a buffer and handed to Noesis without a string; a
    // format with an aligned hole keeps the string route, whose padding the writer does not do.
    string? TextLane(
        ResolvedPath resolved,
        BindingSlot slot,
        MarkupCall call,
        bool formatted,
        string? resolverName
    )
    {
        if (
            slot.Type.SpecialType != SpecialType.System_String
            || resolved.Hops.Count == 0
            || slot.Assign is not null
            || slot.Registered is not null
        )
            return null;

        var last = resolved.Hops[resolved.Hops.Count - 1];
        var from = last.Type;
        var real = from.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
        if (!real && !IsInteger(from))
            return null;

        var pieces = new List<string>();
        if (!formatted)
        {
            pieces.Add(TextPiece(from, "")!);
        }
        else
        {
            if (
                NamedValue(call, "StringFormat") is not string raw
                || FormatHoles(XamlMarkupParser.Unescape(raw), 1) is not { } parsed
            )
                return null;

            var format = parsed.Format;
            var literal = new System.Text.StringBuilder();
            var hole = 0;
            for (var i = 0; i < format.Length; i++)
            {
                var c = format[i];
                if ((c is '{' or '}') && i + 1 < format.Length && format[i + 1] == c)
                {
                    literal.Append(c);
                    i++;
                    continue;
                }

                if (c != '{')
                {
                    literal.Append(c);
                    continue;
                }

                var close = format.IndexOf('}', i);
                if (format.Substring(i + 1, close - i - 1).IndexOf(',') >= 0)
                    return null;

                if (TextPiece(from, parsed.Holes[hole++].Spec) is not { } piece)
                    return null;

                if (literal.Length > 0)
                {
                    pieces.Add($"__s.Literal({Quote(literal.ToString())});");
                    literal.Clear();
                }

                pieces.Add(piece);
                i = close;
            }

            if (literal.Length > 0)
                pieces.Add($"__s.Literal({Quote(literal.ToString())});");
        }

        var owner = XamlTypeResolver.Fqn(last.Owner);
        var fromFqn = XamlTypeResolver.Fqn(from);
        var guarded = resolverName is not null && resolved.Hops.Count == 1 ? "true" : "false";

        return $"{BindingLaneFqn}.Text<{owner}, {fromFqn}>("
            + $"static __o => __o.{last.Name}, "
            + $"static ({fromFqn} __t, global::System.Span<char> __d, out int __w) => "
            + $"{{ var __s = new {SlotTextFqn}(__d); {string.Join(" ", pieces.ToArray())} "
            + "return __s.Done(out __w); }, "
            + $"{guarded})";
    }

    // The same text FormatArgument and TextOf produce, written instead of returned.
    static string? TextPiece(ITypeSymbol type, string spec)
    {
        if (type.SpecialType is SpecialType.System_Single or SpecialType.System_Double)
        {
            if (spec.Length == 0)
                return "__s.Number(__t);";

            return NumberFormat(spec, "NFP") ? $"__s.Fixed(__t, {Quote(spec)});" : null;
        }

        if (spec.Length == 0)
            return "__s.Integer(__t);";

        return
            NumberFormat(spec, "DNFPX")
            && !(type.SpecialType == SpecialType.System_SByte && spec[0] is 'X' or 'x')
            ? $"__s.Integer(__t, {Quote(spec)});"
            : null;
    }

    // A struct that casts to a number and back through its own operators rides the lane that number
    // would, cast through it both ways: C# will not chain the operator to an unrelated number itself.
    static bool Operates(ITypeSymbol from, ITypeSymbol slot) =>
        IsNumeric(slot) && OperatorNumber(from) is not null;

    // The number a struct casts to through an operator it declares, as a typed id casts to its backing.
    static ITypeSymbol? OperatorNumber(ITypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Struct || Wrapped(type) is not null || IsNumericOrEnum(type))
            return null;

        foreach (var op in Operators(type))
        {
            if (
                SymbolEqualityComparer.Default.Equals(op.Parameters[0].Type, type)
                && IsNumeric(op.ReturnType)
            )
                return op.ReturnType;
        }

        return null;
    }

    static bool CastsBack(ITypeSymbol type, ITypeSymbol number) =>
        Operators(type)
            .Any(op =>
                SymbolEqualityComparer.Default.Equals(op.Parameters[0].Type, number)
                && SymbolEqualityComparer.Default.Equals(op.ReturnType, type)
            );

    static IEnumerable<IMethodSymbol> Operators(ITypeSymbol type) =>
        type.GetMembers("op_Implicit")
            .Concat(type.GetMembers("op_Explicit"))
            .OfType<IMethodSymbol>()
            .Where(op => op.Parameters.Length == 1);

    // A cast into or out of an operator-carrying struct goes through its operator's number.
    static string CastTo(ITypeSymbol to, ITypeSymbol structType, string value) =>
        OperatorNumber(structType) is { } via
            ? $"({XamlTypeResolver.Fqn(to)})({XamlTypeResolver.Fqn(via)}){value}"
            : $"({XamlTypeResolver.Fqn(to)}){value}";

    // Found by a mark rather than a position: the host's own code can add objects ahead of these.
    (string Anchor, INamedTypeSymbol AnchorType, string Receiver)? DetachedReceiver(
        XElement element
    )
    {
        if (
            AttachedTo(element) is not { } attached
            || AnchorVar(attached.Host) is not { } anchor
            || resolver.SymbolOf(attached.Host) is not { } anchorType
        )
            return null;

        return (
            anchor,
            anchorType,
            $"__a => {CompiledBindingFqn}.{attached.Lookup}(__a, {ReceiverKey(element)})"
        );
    }

    static (XElement Host, string Lookup)? AttachedTo(XElement element)
    {
        if (element.Parent is not { } parent)
            return null;

        if (parent.Name.LocalName == "Interaction.Behaviors" && parent.Parent is { } behaved)
            return (behaved, "MarkedBehavior");

        if (
            parent.Name.LocalName.EndsWith(".InputBindings", StringComparison.Ordinal)
            && parent.Parent is { } bound
        )
            return (bound, "MarkedInputBinding");

        if (
            parent.Parent is { Name.LocalName: "Interaction.Triggers" } wrapper
            && wrapper.Parent is { } triggered
        )
            return (triggered, "MarkedAction");

        return null;
    }

    readonly Dictionary<XElement, string> _receiverKeys = new Dictionary<XElement, string>();

    readonly HashSet<XElement> _markedReceivers = new HashSet<XElement>();

    string ReceiverKey(XElement element)
    {
        if (!_receiverKeys.TryGetValue(element, out var key))
            _receiverKeys[element] = key = Quote(
                $"{XamlPaths.LogicalName(filePath, packPrefix, projectDir)}#r{_receiverKeys.Count}"
            );

        return key;
    }

    string? AnchorVar(XElement? host)
    {
        if (host is null)
            return null;

        string? name = null;
        if (_elementVars.TryGetValue(host, out var found))
            name = found;
        else if (host.Parent is null && _rootClass is not null)
            name = "this";

        if (name is null)
            return null;

        var type = resolver.SymbolOf(host);
        return
            type is not null
            && XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement")
            ? name
            : null;
    }

    // Inside a template the wire runs once per clone, so what it only reads is built once, outside.
    string Shared(string hint, string expression)
    {
        if (_templates.Count == 0)
            return expression;

        var name = NextName(hint);
        _lines.Add($"var {name} = {expression};");
        return name;
    }

    void EmitCompiledBind(string target, Func<string, string> bind)
    {
        if (_templates.Count == 0)
        {
            _lines.Add($"{bind(target)};");
            return;
        }

        // Inside a template the generated code holds a prototype; only a copied local value reaches
        // the clones, and an element carries one such value however many bindings it has.
        if (!_wires.TryGetValue(target, out var wires))
            _wires[target] = wires = new List<string>();

        wires.Add($"{bind("__e")};");
    }

    readonly Dictionary<string, List<string>> _wires = new Dictionary<string, List<string>>(
        StringComparer.Ordinal
    );

    /// <summary>Emits the one wiring slot an element's compiled bindings share, once the element has
    /// taken every attribute and child that could add one.</summary>
    internal void FlushWires(string target)
    {
        if (!_wires.TryGetValue(target, out var wires))
            return;

        _wires.Remove(target);
        var key = Quote($"{XamlPaths.LogicalName(filePath, packPrefix, projectDir)}#{_setups++}");
        _lines.Add(
            $"{SetupFqn}.SetIndex({target}, {SetupFqn}.Register({key}, "
                + $"__e => {{ {string.Join(" ", wires.ToArray())} }}));"
        );
    }

    /// <summary>Whether any element's wiring was collected and never emitted, which would drop those
    /// bindings without a word.</summary>
    internal bool HasUnflushedWires => _wires.Count > 0 || _pendingWires.Count > 0;

    // Slot is the dependency property the path reads through; null reads the source's DataContext.
    /// <summary>The three fields every part opens with, already in declaration order.</summary>
    List<string> SourceFields(BindingSource source, ResolvedPath resolved)
    {
        var chain = string.Join(
            ", ",
            resolved
                .Hops.Select((h, i) => HopExpression(h, source.Resolver is not null && i == 0))
                .ToArray()
        );

        var fields = new List<string>();

        if (source.Resolver is not null)
            fields.Add($"Source = {source.Resolver}");

        if (resolved.Slot is not null)
            fields.Add($"SourceProperty = {resolved.Slot}");

        fields.Add($"Hops = new {BindingHopFqn}[] {{ {chain} }}");
        return fields;
    }

    string PartExpression(BindingSource source, ResolvedPath resolved) =>
        $"new {PartFqn} {{ {string.Join(", ", SourceFields(source, resolved).ToArray())} }}";

    readonly struct ResolvedPath(string? slot, List<Hop> hops, ITypeSymbol type)
    {
        public string? Slot { get; } = slot;
        public List<Hop> Hops { get; } = hops;
        public ITypeSymbol Type { get; } = type;
    }

    ResolvedPath? ResolvePath(BindingSource source, string path)
    {
        if (source.Resolver is null)
        {
            if (DeclaredContext(source.Scope) is not { } own)
            {
                Note("data-context-undeclared");
                return null;
            }

            if (path.Length == 0)
                return new ResolvedPath(null, new List<Hop>(), own);

            return ResolveHops(own, path) is { } hops
                ? new ResolvedPath(null, hops, hops[hops.Count - 1].Type)
                : null;
        }

        if (TrySplitDataContextHop(path, out var tail))
        {
            // One container type serves a different view model per use site, so only the
            // annotation can say; inferring it from the document's ancestry would be a guess.
            var context =
                source.Detached ? AncestorContext(source.Scope)
                : source.Templated ? TemplatedContext(source.Scope)
                : DeclaredContext(source.Scope);

            if (context is null)
            {
                Note(
                    source.Detached ? "ancestor-data-context-undeclared" : "data-context-undeclared"
                );
                return null;
            }

            return ResolveHops(context, tail) is { } hops
                ? new ResolvedPath(null, hops, hops[hops.Count - 1].Type)
                : null;
        }

        return ElementPath(source, path);
    }

    // Only a dependency property of an element can be watched, so the first segment has to be one.
    // The identity binding names no member at all, so off an element there is nothing to watch.
    /// <summary>The <c>(Owner.Name)</c> head of an attached-property path, split into the type
    /// reference, the property name and the tail after it; null when the path has no such
    /// head.</summary>
    static (string Owner, string Name, string Tail)? AttachedSegment(string path)
    {
        if (path.Length == 0 || path[0] != '(')
            return null;

        var close = path.IndexOf(')');
        if (close < 0)
            return null;

        var inside = path.Substring(1, close - 1);
        var dot = inside.LastIndexOf('.');
        if (dot <= 0 || dot == inside.Length - 1)
            return null;

        var tail = path.Substring(close + 1).TrimStart('.');
        return (inside.Substring(0, dot), inside.Substring(dot + 1), tail);
    }

    ResolvedPath? ElementPath(BindingSource source, string path)
    {
        if (path.Length == 0)
        {
            Note("identity-binding-off-an-element");
            return null;
        }

        var scope = source.Scope;
        var type = source.Type ?? resolver.SymbolOf(scope);
        if (type is null)
            return null;

        if (AttachedSegment(path) is { } attached)
            return AttachedElementPath(scope, attached);

        var segments = path.Split('.');
        var first = resolver.FindProperty(type, segments[0]);
        if (first is null || first.GetMethod is null)
        {
            Note("element-has-no-such-member");
            return null;
        }

        if (resolver.FindDependencyPropertyOwner(type, segments[0]) is not { } owner)
        {
            Note("element-member-is-not-a-dependency-property");
            return null;
        }

        if (Unregistered(first.Type))
            return null;

        var slot = $"{XamlTypeResolver.Fqn(owner)}.{segments[0]}Property";
        if (segments.Length == 1)
            return new ResolvedPath(
                slot,
                new List<Hop>(),
                MismatchedSlot(first) ? resolver.UInt32Type : first.Type
            );

        if (first.Type is not INamedTypeSymbol root)
            return null;

        var rest = string.Join(".", segments, 1, segments.Length - 1);
        return ResolveHops(root, rest) is { } hops
            ? new ResolvedPath(slot, hops, hops[hops.Count - 1].Type)
            : null;
    }

    ResolvedPath? AttachedElementPath(XElement scope, (string Owner, string Name, string Tail) part)
    {
        if (ResolveTypeSymbol(scope, part.Owner) is not { } owner)
        {
            Note("attached-path-owner-unresolved");
            return null;
        }

        if (resolver.FindAttachedValueType(owner, part.Name) is not { } value)
        {
            Note("attached-path-has-no-value-type");
            return null;
        }

        if (resolver.FindDependencyPropertyOwner(owner, part.Name) is null)
        {
            Note("attached-path-is-not-a-dependency-property");
            return null;
        }

        var slot = SlotReference(owner, part.Name);
        if (part.Tail.Length == 0)
            return new ResolvedPath(slot, new List<Hop>(), value);

        if (value is not INamedTypeSymbol root)
        {
            Note("attached-path-tail-has-no-named-type");
            return null;
        }

        return ResolveHops(root, part.Tail) is { } hops
            ? new ResolvedPath(slot, hops, hops[hops.Count - 1].Type)
            : null;
    }

    string? WriteExpression(ResolvedPath resolved, ITypeSymbol? incoming = null)
    {
        if (resolved.Hops.Count == 0)
            return null;

        var last = resolved.Hops[resolved.Hops.Count - 1];

        // The hop arrives boxed, so a struct would take the write on a copy the caller never sees.
        if (last.Owner.IsValueType)
            return null;

        if (incoming is not null)
        {
            var writable = resolver.FindProperty(last.Owner, last.Name);
            if (
                writable?.SetMethod is null
                || writable.SetMethod.IsInitOnly
                || writable.SetMethod.DeclaredAccessibility != Accessibility.Public
            )
                return null;

            var inFqn = XamlTypeResolver.Fqn(Wrapped(incoming) ?? incoming);
            var outFqn = XamlTypeResolver.Fqn(last.Type);
            return $"(__o, __v) => (({XamlTypeResolver.Fqn(last.Owner)})__o).{last.Name} = "
                + $"__v is {inFqn} __w ? {CastTo(last.Type, last.Type, "__w")} : default({outFqn})";
        }

        var property = resolver.FindProperty(last.Owner, last.Name);

        // An init accessor is a setter the language only lets an initializer reach.
        if (
            property?.SetMethod is null
            || property.SetMethod.IsInitOnly
            || property.SetMethod.DeclaredAccessibility != Accessibility.Public
        )
            return null;

        return $"(__o, __v) => (({XamlTypeResolver.Fqn(last.Owner)})__o).{last.Name} = "
            + NarrowTo(last.Type, "__v", "__w");
    }

    // The value arrives boxed as object, and `x is T?` does not parse, so a nullable slot has to be
    // matched against the type it wraps.
    static string NarrowTo(ITypeSymbol type, string value, string temp)
    {
        var fqn = XamlTypeResolver.Fqn(type);
        if (!type.IsValueType)
            return $"{value} as {fqn}";

        if (Wrapped(type) is { } inner)
            return $"{value} is {XamlTypeResolver.Fqn(inner)} {temp} ? ({fqn}){temp} : default({fqn})";

        return $"{value} is {fqn} {temp} ? {temp} : default({fqn})";
    }

    static ITypeSymbol? Wrapped(ITypeSymbol type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
            ? nullable.TypeArguments[0]
            : null;

    // Direction settles at run time off the target property's metadata; this only refuses what it
    // could not honour either way.
    string? Direction(MarkupCall call, BindingSlot slot, bool writable, string unwritable)
    {
        if (NamedValue(call, "Mode") is not string raw)
            return Default();

        switch (raw.Trim())
        {
            case "Default":
                return Default();
            case "OneWay":
                return "OneWay";
            case "TwoWay":
                if (writable)
                    return "TwoWay";

                Note(unwritable);
                return null;
            default:
                Note("direction-not-implemented");
                return null;
        }

        string? Default()
        {
            if (writable || !slot.TwoWayByDefault)
                return "";

            Note(unwritable);
            return null;
        }
    }

    string? Trigger(MarkupCall call)
    {
        if (NamedValue(call, "UpdateSourceTrigger") is not string raw)
            return "";

        return raw.Trim() switch
        {
            "Default" => "",
            "PropertyChanged" => "PropertyChanged",
            "LostFocus" => "LostFocus",
            _ => null,
        };
    }

    // A null resolver is the binding's own target, which needs no lookup and no guarded hop.
    readonly struct BindingSource(
        string? resolver,
        XElement scope,
        INamedTypeSymbol? type = null,
        bool detached = false,
        bool structural = false,
        bool templated = false
    )
    {
        public string? Resolver { get; } = resolver;
        public XElement Scope { get; } = scope;

        /// <summary>Set where the template's own shape says which element the source is, so the
        /// binding names no identifier the app authored and none can be renamed out from under it.</summary>
        public bool Structural { get; } = structural;

        /// <summary>Set where the source is not an element the document wrote, so its type comes
        /// from what the template declares rather than from a tag name.</summary>
        public INamedTypeSymbol? Type { get; } = type;

        /// <summary>Set where the scope element only locates the binding, not the source, so the
        /// source's DataContext cannot be typed from it.</summary>
        public bool Detached { get; } = detached;

        public bool Templated { get; } = templated;
    }

    BindingSource? ResolveSource(XElement element, MarkupCall call)
    {
        var named = NamedValue(call, "ElementName") as string;
        var relative = NamedValue(call, "RelativeSource");

        if (named is null && relative is null)
            return new BindingSource(null, element);

        if (named is not null && relative is not null)
            return null;

        return named is not null
            ? NamedSource(element, named.Trim())
            : AncestorSource(element, relative as MarkupCall);
    }

    BindingSource? NamedSource(XElement element, string name)
    {
        if (LookupNamed(name) is not { } scope)
            return null;

        var type = resolver.SymbolOf(scope);
        if (type is null || !XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement"))
            return null;

        var owner = TemplateOwner(element);
        var declared = TemplateOwner(scope);

        if (!ReferenceEquals(owner, declared))
        {
            // A clone's own scope answers for the template alone, so an outer name is only reached
            // by walking past it; a name declared inward is not reachable at all.
            return declared is null || element.Ancestors().Any(a => ReferenceEquals(a, declared))
                ? new BindingSource(
                    $"__s => {CompiledBindingFqn}.FindNamed(__s, {Quote(name)})",
                    scope
                )
                : null;
        }

        if (owner is not null)
            return new BindingSource(
                $"__s => __s.FindName({Quote(name)}) as global::Noesis.FrameworkElement",
                scope
            );

        // A compiled root assigns its names to fields and registers none, so nothing else answers.
        return _rootClass is null ? null : new BindingSource($"__s => this._{name}", scope);
    }

    // The templated parent is not an element the document wrote, so what it is comes from the type
    // the template states it applies to.
    BindingSource? TemplatedSource(XElement element) =>
        TemplatedTarget(element) is { } target
            ? new BindingSource(
                $"__s => {CompiledBindingFqn}.TemplatedParent(__s)",
                element,
                target,
                structural: true,
                templated: true
            )
            : null;

    BindingSource? AncestorSource(XElement element, MarkupCall? relative)
    {
        if (relative is not { Name: "RelativeSource" })
            return null;

        var (mode, reference, level) = RelativeSourceParts(relative);

        if (mode == "TemplatedParent")
            return TemplatedSource(element);

        if (mode == "Self")
            return new BindingSource("__s => __s", element, structural: true);

        // A deeper ancestor has no compiled walk, so it stays native.
        if (level != "1")
            return null;

        if (reference is null || ResolveTypeSymbol(element, reference) is not { } type)
            return null;

        // A walk from an attached object counts the element it hangs off.
        var walk = AttachedTo(element) is null ? "FindAncestor" : "FindAncestorOrSelf";
        var lookup =
            $"__s => {CompiledBindingFqn}.{walk}(__s, typeof({XamlTypeResolver.Fqn(type)}))";
        if (AncestorScope(element, type) is { } scope)
            return new BindingSource(lookup, scope);

        // Past a style the XML ancestry says nothing, so the type comes from the attribute alone
        // and the run-time walk retries off LayoutUpdated until the tree connects. What that
        // ancestor's DataContext holds stays unknowable, so only its own properties may be read.
        return new BindingSource(lookup, element, type, detached: true);
    }

    /// <summary>The three knobs a RelativeSource states, however the document spelled them.</summary>
    static (string Mode, string? AncestorType, string Level) RelativeSourceParts(MarkupCall call)
    {
        var written = NamedValue(call, "AncestorType");

        return (
            (call.Positional.FirstOrDefault() ?? NamedValue(call, "Mode") as string ?? "").Trim(),
            written as string ?? (written as MarkupCall)?.Positional.FirstOrDefault(),
            (NamedValue(call, "AncestorLevel") as string)?.Trim() ?? "1"
        );
    }

    // A style detaches content from where it was written, so past one the XML ancestry says nothing.
    XElement? AncestorScope(XElement element, INamedTypeSymbol type)
    {
        var wanted = XamlTypeResolver.Fqn(type);

        foreach (var ancestor in element.Ancestors())
        {
            var symbol = resolver.SymbolOf(ancestor);
            if (symbol is null)
                continue;

            if (XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.Style"))
                return null;

            if (XamlTypeResolver.DerivesFrom(symbol, wanted))
                return ancestor;
        }

        return null;
    }

    XElement? TemplateOwner(XElement element)
    {
        foreach (var ancestor in element.Ancestors())
        {
            var symbol = resolver.SymbolOf(ancestor);
            if (
                symbol is not null
                && XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate")
            )
                return ancestor;
        }

        return null;
    }

    static bool TrySplitDataContextHop(string path, out string tail)
    {
        const string prefix = "DataContext.";
        tail = path.StartsWith(prefix, StringComparison.Ordinal)
            ? path.Substring(prefix.Length)
            : "";

        return tail.Length > 0;
    }

    string HopExpression(Hop hop, bool guarded)
    {
        var owner = XamlTypeResolver.Fqn(hop.Owner);
        var name = Quote(hop.Name);

        if (hop.Type.SpecialType == SpecialType.System_Boolean)
            return guarded
                ? $"{BindingHopFqn}.GuardedBool<{owner}>({name}, __c => __c.{hop.Name})"
                : $"{BindingHopFqn}.Bool({name}, __o => (({owner})__o).{hop.Name})";

        if (BoxesOnRead(hop.Type))
        {
            var value = XamlTypeResolver.Fqn(hop.Type);
            return guarded
                ? $"{BindingHopFqn}.GuardedValue<{owner}, {value}>({name}, __c => __c.{hop.Name})"
                : $"{BindingHopFqn}.Value<{value}>({name}, __o => (({owner})__o).{hop.Name})";
        }

        return guarded
            ? $"{BindingHopFqn}.Guarded<{owner}>({name}, __c => __c.{hop.Name})"
            : $"new {BindingHopFqn}({name}, __o => (({owner})__o).{hop.Name})";
    }

    // A nullable already boxes to its value or null, and a ref struct cannot be a type argument.
    static bool BoxesOnRead(ITypeSymbol type) =>
        type.IsValueType
        && !type.IsRefLikeType
        && type.TypeKind != TypeKind.TypeParameter
        && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

    // A presenter the template gives no Content of its own takes the templated parent's content and template.
    bool PresenterContentInTemplate(INamedTypeSymbol type, string slot) =>
        _templates.Count > 0
        && slot == "Content"
        && XamlTypeResolver.DerivesFrom(type, "global::Noesis.ContentPresenter");

    bool OptedIntoCompiledBindings(XElement element)
    {
        if (
            !CompiledBindingsAvailable
            || element
                .AncestorsAndSelf()
                .Select(e => e.Attribute(XName.Get("CompileBindings", XamlTypeResolver.ToolkitNs)))
                .FirstOrDefault(a => a is not null)
                is not { } marker
        )
            return false;

        if (XamlScopeRules.CompileBindings(marker.Value) is { } on)
            return on;

        Note("compile-bindings-is-not-a-boolean");
        return false;
    }

    // A template hands its content a different DataContext, so an un-annotated one has to stop the
    // walk: continuing would silently bind against the type of an enclosing scope.

    readonly HashSet<XElement> _resolving = new HashSet<XElement>();

    /// <summary>The DataContext an <c>ntk:AncestorDataType</c> in scope states a
    /// <c>RelativeSource FindAncestor</c> binding's ancestor will hold, or null where none does.
    /// Scoped like <c>ntk:DataType</c>: the nearest one out from the binding wins, so a template
    /// states it once for every binding inside it.</summary>
    INamedTypeSymbol? AncestorContext(XElement element)
    {
        foreach (var scope in element.AncestorsAndSelf())
        {
            if (
                scope.Attribute(XName.Get("AncestorDataType", XamlTypeResolver.ToolkitNs)) is
                { } annotated
            )
                return ResolveTypeSymbol(scope, annotated.Value);
        }

        return null;
    }

    INamedTypeSymbol? DeclaredContext(XElement element)
    {
        // A template's source can be read off an element inside it, and the lookup would loop.
        if (!_resolving.Add(element))
            return null;

        try
        {
            return DeclaredContextCore(element);
        }
        finally
        {
            _resolving.Remove(element);
        }
    }

    INamedTypeSymbol? DeclaredContextCore(XElement element)
    {
        // An element that rebinds its own DataContext hands a different one to everything below it,
        // itself included. Collected innermost first, so they apply to the base in reverse.
        var redirects = new List<string>();

        foreach (var scope in element.AncestorsAndSelf())
        {
            if (scope.Attribute(XName.Get("DataType", XamlTypeResolver.ToolkitNs)) is { } annotated)
                return Redirected(ResolveTypeSymbol(scope, annotated.Value), redirects);

            if (!XamlScopeRules.ScopeBreakers.Contains(scope.Name.LocalName))
            {
                if (Redirection(scope) is not { } path)
                    return null;

                if (path.Length > 0)
                    redirects.Add(path);

                continue;
            }

            if (
                scope.Name.LocalName == "DataTemplate"
                && scope.Attribute("DataType") is { } declared
            )
                return Redirected(ResolveTypeSymbol(scope, declared.Value), redirects);

            // Any document or code can apply a key, so the uses seen here prove nothing.
            if (scope.Attribute(XName.Get("Key", XamlTypeResolver.DirectiveNs)) is not null)
                return null;

            return Redirected(HostedContext(scope), redirects);
        }

        return null;
    }

    // Empty keeps the inherited DataContext; null replaces it with one the compiler cannot type.
    string? Redirection(XElement element)
    {
        if (
            element
                .Elements()
                .Any(e => e.Name.LocalName.EndsWith(".DataContext", StringComparison.Ordinal))
        )
            return null;

        if (element.Attribute("DataContext") is not { } written)
            return "";

        if (XamlMarkupParser.Parse(written.Value) is not { Name: "Binding" } call)
            return null;

        if (
            call.Named.Any(p =>
                p.Key is not ("Path" or "Mode" or "UpdateSourceTrigger")
                || p is { Key: "Mode", Value: string mode } && mode.Trim() == "OneWayToSource"
            )
        )
            return null;

        var was = _quiet;
        _quiet = true;
        try
        {
            return PlainPath(call);
        }
        finally
        {
            _quiet = was;
        }
    }

    INamedTypeSymbol? Redirected(INamedTypeSymbol? context, List<string> redirects)
    {
        for (var i = redirects.Count - 1; i >= 0 && context is not null; i--)
        {
            context = ResolveHops(context, redirects[i]) is { } hops
                ? hops[hops.Count - 1].Type as INamedTypeSymbol
                : null;
        }

        return context;
    }

    INamedTypeSymbol? HostedContext(XElement template)
    {
        if (template.Parent is not { } slot)
            return null;

        var dot = slot.Name.LocalName.LastIndexOf('.');
        if (dot < 0)
            return null;

        var property = slot.Name.LocalName.Substring(dot + 1);
        if (XamlScopeRules.TransparentHosts.Contains(property))
            return slot.Parent is { } styled ? DeclaredContext(styled) : null;

        if (!XamlScopeRules.TemplateHosts.TryGetValue(property, out var host))
            return null;

        return SourcedContext(slot, host);
    }

    // A cell template sits under the column rather than the list, so the source is the nearest one
    // named above -- but never past a breaker, which renders something else entirely.
    INamedTypeSymbol? SourcedContext(XElement slot, XamlScopeRules.TemplateHost host)
    {
        foreach (var element in slot.AncestorsAndSelf())
        {
            if (element.Attribute(host.SourceProperty) is not { } written)
            {
                if (XamlScopeRules.ScopeBreakers.Contains(element.Name.LocalName))
                    return null;

                continue;
            }

            if (SourceType(element, written.Value) is not { } type)
                return null;

            return host.IsCollection ? ElementType(type) : type as INamedTypeSymbol;
        }

        return null;
    }

    // What the host renders can be named the same three ways any other value can.
    ITypeSymbol? SourceType(XElement element, string written)
    {
        if (XamlMarkupParser.Parse(written) is not { } call)
            return null;

        if (call.Name == "TemplateBinding")
            return TemplatedType(element, call.Positional.FirstOrDefault() as string);

        // What a converter or a format hands the host is not the type its path reads.
        if (call.Named.Any(p => p.Key is "Converter" or "StringFormat"))
            return null;

        // A pathless {Binding} hands on the DataContext itself, so the host renders what is here.
        if (call is { Name: "Binding", Positional.Count: 0, Named.Count: 0 })
            return DeclaredContext(element);

        if (PlainPath(call) is not { } path)
            return null;

        if (ResolveSource(element, call) is not { } source)
            return null;

        return ResolvePath(source, path)?.Type;
    }

    // The type a template states it applies to, which is what a TemplatedParent binding reads off.
    // A ControlTemplate in a Style's Template setter states none and takes the Style's.
    INamedTypeSymbol? TemplatedTarget(XElement element)
    {
        foreach (var scope in element.AncestorsAndSelf())
        {
            if (resolver.SymbolOf(scope) is not { } symbol)
                continue;

            if (XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.Style"))
                return null;

            if (!XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate"))
                continue;

            // Content of any other template is templated by whatever presents it.
            if (!XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.ControlTemplate"))
                return null;

            var stating = scope.Attribute("TargetType") is null ? SetterStyle(scope) : scope;
            return stating?.Attribute("TargetType") is { } target
                ? ResolveTypeSymbol(stating, target.Value)
                : null;
        }

        return null;
    }

    static XElement? SetterStyle(XElement template)
    {
        if (
            template.Parent is not { Name.LocalName: "Setter.Value" } value
            || value.Parent is not { Name.LocalName: "Setter" } setter
            || setter.Attribute("Property")?.Value.Trim() is not { } property
            || property.Substring(property.LastIndexOf('.') + 1) != "Template"
        )
            return null;

        var owner = setter.Parent is { Name.LocalName: "Style.Setters" } setters
            ? setters.Parent
            : setter.Parent;
        return owner?.Name.LocalName == "Style" ? owner : null;
    }

    // The templated parent's DataContext is what the content inherits, before any redirect in it.
    INamedTypeSymbol? TemplatedContext(XElement element)
    {
        XElement? stated = null;

        foreach (var scope in element.AncestorsAndSelf())
        {
            if (XamlScopeRules.ScopeBreakers.Contains(scope.Name.LocalName))
                return stated is null ? DeclaredContext(scope) : DeclaredContext(stated);

            if (Redirection(scope) is not "")
                stated = null;
            else if (
                stated is null
                && scope.Attribute(XName.Get("DataType", XamlTypeResolver.ToolkitNs)) is not null
            )
                stated = scope;
        }

        return null;
    }

    ITypeSymbol? TemplatedType(XElement element, string? property) =>
        property is not null
        && XamlMarkup.IsIdentifier(property.Trim())
        && TemplatedTarget(element) is { } owner
            ? resolver.FindProperty(owner, property.Trim())?.Type
            : null;

    static INamedTypeSymbol? ElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType as INamedTypeSymbol;

        var candidates = type is INamedTypeSymbol { TypeKind: TypeKind.Interface } self
            ? new[] { self }.Concat(type.AllInterfaces)
            : type.AllInterfaces.AsEnumerable();

        foreach (var candidate in candidates)
        {
            if (
                candidate.OriginalDefinition.SpecialType
                == SpecialType.System_Collections_Generic_IEnumerable_T
            )
                return candidate.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    // Only a bare path compiles; every other knob is the binding engine's behaviour, not a lookup.
    // Mode and UpdateSourceTrigger are read out of it separately, and may still refuse.
    string? PlainPath(MarkupCall call)
    {
        if (call.Name != "Binding" || call.PositionalCalls.Count > 0)
        {
            Note("binding-argument-is-markup");
            return null;
        }

        var named = call
            .Named.Where(p =>
                p.Key
                    is not (
                        "Path"
                        or "Converter"
                        or "ConverterParameter"
                        or "StringFormat"
                        or "Mode"
                        or "UpdateSourceTrigger"
                        or "ElementName"
                        or "RelativeSource"
                    )
            )
            .ToArray();
        if (named.Length > 0)
        {
            Note("binding-knob-" + named[0].Key.ToLowerInvariant());
            return null;
        }

        var path = call.Positional.FirstOrDefault() ?? NamedValue(call, "Path") as string;

        // No path at all is the identity binding, which reads the source itself rather than a member.
        if (string.IsNullOrWhiteSpace(path))
            return "";

        path = path!.Trim();

        // "." names the source object itself, which is what an absent path already means.
        if (path == ".")
            return "";

        if (AttachedSegment(path) is not null || path.Split('.').All(XamlMarkup.IsIdentifier))
            return path;

        Note(
            path.Contains('(') ? "path-is-an-attached-property"
            : path.Contains('[') ? "path-has-an-indexer"
            : "path-is-not-identifiers"
        );
        return null;
    }

    readonly struct Hop(string name, INamedTypeSymbol owner, ITypeSymbol propertyType)
    {
        public string Name { get; } = name;
        public INamedTypeSymbol Owner { get; } = owner;
        public ITypeSymbol Type { get; } = propertyType;
    }

    // A cast has to name the type, and an unbound one only names its own parameters. Constructing it
    // over object would compile and then fail the cast, since a generic is not covariant.
    static bool IsOpenGeneric(INamedTypeSymbol type) =>
        type.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter);

    ITypeSymbol? MemberType(INamedTypeSymbol owner, string name) =>
        resolver.FindProperty(owner, name) is { GetMethod: not null } property
            ? property.Type
            : resolver.FindGeneratedCommand(owner, name);

    List<Hop>? ResolveHops(INamedTypeSymbol context, string path)
    {
        var hops = new List<Hop>();
        var current = context;
        var remaining = path.Split('.').Length;

        foreach (var segment in path.Split('.'))
        {
            if (IsOpenGeneric(current))
            {
                Note("hop-through-an-open-generic");
                return null;
            }

            if (MemberType(current, segment) is not { } member)
            {
                Note("hop-missing");
                return null;
            }

            hops.Add(new Hop(segment, current, member));
            remaining--;

            // Only a hop something else reads off has to be a type the next one can resolve against.
            if (remaining == 0)
                break;

            if (member is not INamedTypeSymbol next)
            {
                Note("hop-has-no-named-type");
                return null;
            }

            current = next;
        }

        return hops;
    }

    // Noesis dispatches SetValue on the property's declared type, so a near-enough value is a crash
    // rather than a coercion; without a converter an inexact source falls back instead.
    // A boxed T and a boxed T? are the same object, so a nullable slot takes what it wraps.
    static bool Assignable(ITypeSymbol source, ITypeSymbol slot) =>
        slot.SpecialType == SpecialType.System_Object
        || SymbolEqualityComparer.Default.Equals(source, slot)
        || XamlTypeResolver.DerivesFrom(source, XamlTypeResolver.Fqn(slot))
        || Implements(source, slot)
        || (Wrapped(slot) is { } inner && SymbolEqualityComparer.Default.Equals(source, inner));

    static bool Implements(ITypeSymbol source, ITypeSymbol slot)
    {
        if (slot.TypeKind != TypeKind.Interface)
            return false;

        foreach (var implemented in source.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, slot))
                return true;
        }

        return false;
    }

    // A compiled binding is one-way and BindsTwoWayByDefault is not readable off a native
    // DependencyProperty. Over-listing costs a fallback; under-listing drops a write-back silently.
    static readonly (string Owner, string Property)[] TwoWayByDefault =
    {
        ("global::Noesis.ToggleButton", "IsChecked"),
        ("global::Noesis.Selector", "IsSelected"),
        ("global::Noesis.Selector", "SelectedIndex"),
        ("global::Noesis.Selector", "SelectedItem"),
        ("global::Noesis.Selector", "SelectedValue"),
        ("global::Noesis.RangeBase", "Value"),
        ("global::Noesis.Track", "Value"),
        ("global::Noesis.TextBox", "Text"),
        ("global::Noesis.ComboBox", "Text"),
        ("global::Noesis.ComboBox", "IsDropDownOpen"),
        ("global::Noesis.Expander", "IsExpanded"),
        ("global::Noesis.Expander", "ExpandDirection"),
        ("global::Noesis.Popup", "IsOpen"),
        ("global::Noesis.ContextMenu", "IsOpen"),
        ("global::Noesis.ToolTip", "IsOpen"),
        ("global::Noesis.MenuItem", "IsChecked"),
        ("global::Noesis.MenuItem", "IsSubmenuOpen"),
        ("global::Noesis.ListBoxItem", "IsSelected"),
        ("global::Noesis.TabItem", "IsSelected"),
        ("global::Noesis.TreeViewItem", "IsSelected"),
        ("global::Noesis.TreeViewItem", "IsSelectionActive"),
        ("global::Noesis.ToolBar", "IsOverflowOpen"),
        ("global::Noesis.Slider", "SelectionStart"),
        ("global::Noesis.Slider", "SelectionEnd"),
        ("global::Noesis.TickBar", "SelectionStart"),
        ("global::Noesis.TickBar", "SelectionEnd"),
    };

    static bool BindsTwoWayByDefault(IPropertySymbol property) => Listed(property, TwoWayByDefault);

    const string UnsetValueFqn = "global::Noesis.DependencyProperty.UnsetValue";

    const string SlotConversionFqn = "global::NoesisToolkit.Mvvm.CodeGen.SlotConversion";

    const string InvariantFqn = "global::System.Globalization.CultureInfo.InvariantCulture";

    // StringFormat runs after the converter, and only where the slot actually holds a string.
    string? Shape(
        MarkupCall call,
        ITypeSymbol slot,
        ITypeSymbol source,
        bool converted,
        string? coercion
    )
    {
        if (NamedValue(call, "StringFormat") is not { } raw)
            return coercion ?? (converted ? ConverterFit(slot) : Coerce(slot));

        if (raw is not string format || slot.SpecialType != SpecialType.System_String)
            return null;

        if (converted)
        {
            Note("text-conversion-unlike-native");
            return null;
        }

        if (FormatHoles(XamlMarkupParser.Unescape(format), 1) is not { } parsed)
            return null;

        var from = Wrapped(source) ?? source;
        var value = IsNumeric(from) ? "__t" : "__v";
        var args = new List<string>();
        foreach (var (_, spec) in parsed.Holes)
        {
            if (FormatArgument(source, spec, value) is not { } arg)
                return null;

            args.Add(arg);
        }

        var formatted = FormatCall(parsed.Format, args);
        if (IsNumeric(from))
            return Cached(from, formatted, UnsetValueFqn);

        // The engine fails the whole format on a null it does not hold as a string.
        return source.SpecialType == SpecialType.System_String
            ? $"__v => {formatted}"
            : $"__v => __v is null ? {UnsetValueFqn} : {formatted}";
    }

    static string FormatCall(string format, List<string> args) =>
        $"string.Format({InvariantFqn}, {Quote(format)}"
        + string.Concat(args.Select(a => ", " + a).ToArray())
        + ")";

    // Only the plain shape reads alike in .NET and the engine: numbered holes, brace-free formats.
    (string Format, List<(int Index, string Spec)> Holes)? FormatHoles(string format, int count)
    {
        if (format.IndexOf('{') < 0)
            format = "{0:" + format + "}";

        var rewritten = "";
        var holes = new List<(int Index, string Spec)>();
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if ((c is '{' or '}') && i + 1 < format.Length && format[i + 1] == c)
            {
                rewritten += new string(c, 2);
                i++;
                continue;
            }

            if (c == '}')
                return FormatUnlikeNative();

            if (c != '{')
            {
                rewritten += c;
                continue;
            }

            var close = format.IndexOf('}', i);
            if (close < 0)
                return FormatUnlikeNative();

            var body = format.Substring(i + 1, close - i - 1);
            var colon = body.IndexOf(':');
            var head = colon < 0 ? body : body.Substring(0, colon);
            var comma = head.IndexOf(',');
            var index = FormatIndex(comma < 0 ? head : head.Substring(0, comma));
            var alignment = comma < 0 ? "" : head.Substring(comma + 1);

            // A doubled brace after a format would be read as part of that format.
            if (
                body.IndexOf('{') >= 0
                || index is not { } at
                || at >= count
                || (comma >= 0 && FormatIndex(alignment.TrimStart('-')) is null)
                || (colon >= 0 && close + 1 < format.Length && format[close + 1] == '}')
            )
                return FormatUnlikeNative();

            rewritten +=
                "{" + holes.Count.ToString(Invariant) + (comma < 0 ? "" : "," + alignment) + "}";
            holes.Add((at, colon < 0 ? "" : body.Substring(colon + 1)));
            i = close;
        }

        return (rewritten, holes);
    }

    static int? FormatIndex(string digits) =>
        int.TryParse(
            digits,
            global::System.Globalization.NumberStyles.None,
            Invariant,
            out var number
        )
            ? number
            : null;

    (string, List<(int, string)>)? FormatUnlikeNative()
    {
        Note("string-format-unlike-native");
        return null;
    }

    // The engine applies a format only to a number it boxes itself; any other value it shows whole.
    string? FormatArgument(ITypeSymbol source, string spec, string value)
    {
        var type = Wrapped(source) ?? source;
        var fqn = XamlTypeResolver.Fqn(type);

        if (spec.Length > 0 && IsInteger(type))
        {
            // The engine boxes an sbyte as a short, which shows in hex.
            return
                NumberFormat(spec, "DNFPX")
                && !(type.SpecialType == SpecialType.System_SByte && spec[0] is 'X' or 'x')
                ? $"(({fqn}){value}).ToString({Quote(spec)}, {InvariantFqn})"
                : SpecUnlikeNative();
        }

        if (
            spec.Length > 0
            && type.SpecialType is SpecialType.System_Single or SpecialType.System_Double
        )
        {
            return NumberFormat(spec, "NFP")
                ? $"{SlotConversionFqn}.Text(({fqn}){value}, {Quote(spec)})"
                : SpecUnlikeNative();
        }

        return TextOf(source, value, "\"\"");
    }

    static bool NumberFormat(string spec, string kinds) =>
        kinds.IndexOf(char.ToUpperInvariant(spec[0])) >= 0
        && spec.Length <= 3
        && spec.Skip(1).All(d => d is >= '0' and <= '9');

    string? SpecUnlikeNative()
    {
        Note("string-format-unlike-native");
        return null;
    }

    // Numbers show in the engine's invariant form; any other value only through a ToString override.
    string? TextOf(ITypeSymbol source, string value, string undefined)
    {
        var type = Wrapped(source) ?? source;
        var fqn = XamlTypeResolver.Fqn(type);

        if (type.SpecialType == SpecialType.System_String)
            return value;

        if (type.SpecialType == SpecialType.System_Boolean)
            return $"((bool){value} ? \"True\" : \"False\")";

        if (type.SpecialType == SpecialType.System_Int32)
            return $"{SlotConversionFqn}.Text((int){value})";

        if (IsInteger(type))
            return $"(({fqn}){value}).ToString({InvariantFqn})";

        if (type.SpecialType is SpecialType.System_Single or SpecialType.System_Double)
            return $"{SlotConversionFqn}.Text(({fqn}){value})";

        if (type.TypeKind == TypeKind.Enum && OrdinaryEnum(type))
            return $"(global::System.Enum.IsDefined(typeof({fqn}), {value})"
                + $" ? (object){value}.ToString() : {undefined})";

        if (
            type.TypeKind is TypeKind.Struct or TypeKind.Class
            && type.SpecialType is not (SpecialType.System_Char or SpecialType.System_Decimal)
            && !fqn.StartsWith("global::Noesis.", StringComparison.Ordinal)
            && !XamlTypeResolver.DerivesFrom(type, "global::Noesis.BaseComponent")
            && !XamlTypeResolver.DerivesFrom(type, "global::System.Type")
            && (type.IsValueType || OverridesToString(type))
        )
            return $"{value}.ToString()";

        Note("text-conversion-unlike-native");
        return null;
    }

    static bool IsInteger(ITypeSymbol type) =>
        type.SpecialType
            is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64;

    // The engine names a value by the first member holding it, and fails to box a negative one.
    static bool OrdinaryEnum(ITypeSymbol type)
    {
        var seen = new HashSet<object>();
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.ConstantValue is not { } constant)
                continue;

            if (constant is sbyte and < 0 or short and < 0 or int and < 0 or long and < 0)
                return false;

            if (!seen.Add(constant))
                return false;
        }

        return true;
    }

    static bool OverridesToString(ITypeSymbol type)
    {
        for (
            var current = type;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            if (
                current
                    .GetMembers("ToString")
                    .OfType<IMethodSymbol>()
                    .Any(m => m.IsOverride && m.Parameters.Length == 0)
            )
                return true;
        }

        return false;
    }

    // A result of another type is the engine's to convert; a null it cannot hold means the default.
    static string ConverterFit(ITypeSymbol slot)
    {
        if (slot.SpecialType == SpecialType.System_Object)
            return "__v => __v";

        return $"__v => __v is {XamlTypeResolver.Fqn(Wrapped(slot) ?? slot)} ? __v"
            + $" : __v is null ? {NullInto(slot)} : {SlotConversionFqn}.Unconverted";
    }

    // A multi-binding's result reaches the slot only where the registered type accepts it as is.
    static string MultiFit(BindingSlot slot)
    {
        var target = slot.Registered ?? slot.Type;
        if (
            target.SpecialType == SpecialType.System_Object
            || target.TypeKind == TypeKind.Interface
        )
            return "__v => __v";

        var fits = $"__v => __v is {XamlTypeResolver.Fqn(Wrapped(target) ?? target)} ? __v";
        return NullInto(target) == UnsetValueFqn
            ? $"{fits} : {UnsetValueFqn}"
            : $"{fits} : __v is null ? null : {UnsetValueFqn}";
    }

    static string NullInto(ITypeSymbol slot) =>
        (slot.IsValueType && Wrapped(slot) is null) || slot.SpecialType == SpecialType.System_String
            ? UnsetValueFqn
            : "null";

    // The engine passes the registered type, which is BaseComponent for anything held as an object.
    static string ConverterTargetType(BindingSlot slot)
    {
        var target = slot.Registered ?? slot.Type;
        return
            target.SpecialType == SpecialType.System_Object || target.TypeKind == TypeKind.Interface
            ? "global::Noesis.BaseComponent"
            : XamlTypeResolver.Fqn(target);
    }

    // The value arrives boxed and unboxes to nothing but its own type, so the cast runs off that.
    string? Conversion(ITypeSymbol source, ITypeSymbol slot)
    {
        var from = Wrapped(source) ?? source;
        if (slot.SpecialType == SpecialType.System_String)
        {
            if (IsNumeric(from))
                return TextOf(source, "__t", UnsetValueFqn) is { } number
                    ? Cached(from, $"(object){number}", "null")
                    : null;

            return TextOf(source, "__v", UnsetValueFqn) is { } text
                ? $"__v => __v is null ? null : (object){text}"
                : null;
        }

        if (Constructed(from, Wrapped(slot) ?? slot) is { } constructed)
            return constructed;

        if (Operates(from, Wrapped(slot) ?? slot))
            return Cached(from, $"(object){CastTo(slot, from, "__t")}", NullInto(slot));

        if (!IsNumericOrEnum(from) || !IsNumeric(Wrapped(slot) ?? slot))
            return null;

        return Cached(from, $"(object)({XamlTypeResolver.Fqn(slot)})__t", NullInto(slot));
    }

    // The source value arrives boxed as its own type, and the conversion runs off __t.
    static string Cached(ITypeSymbol from, string convert, string fallback) =>
        $"{SlotConversionFqn}.Cached<{XamlTypeResolver.Fqn(from)}>(static __t => {convert}, {fallback})";

    // The slots the native binding fills through a type converter; the constructor is that
    // converter's whole job, so the compiled form calls it directly.
    static string? Constructed(ITypeSymbol from, ITypeSymbol slot)
    {
        var toFqn = XamlTypeResolver.Fqn(slot);
        if (toFqn == "global::Noesis.ImageSource")
            return from.SpecialType == SpecialType.System_String
                ? "__v => __v is string __t"
                    + " ? global::NoesisToolkit.Mvvm.CodeGen.ImageSources.From(__t) : null"
                : null;

        if (!IsNumeric(from))
            return null;

        var fromFqn = XamlTypeResolver.Fqn(from);
        return toFqn is "global::Noesis.CornerRadius" or "global::Noesis.GridLength"
            ? $"__v => __v is {fromFqn} __t ? (object)new {toFqn}((float)__t) : {UnsetValueFqn}"
            : null;
    }

    static bool IsNumericOrEnum(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Enum || IsNumeric(type);

    static bool IsNumeric(ITypeSymbol type) =>
        type.SpecialType
            is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Single
                or SpecialType.System_Double
                or SpecialType.System_Decimal
                or SpecialType.System_Char;

    static string Coerce(ITypeSymbol slot)
    {
        if (slot.SpecialType == SpecialType.System_Object)
            return "__v => __v";

        if (!slot.IsValueType)
            return $"__v => {NarrowTo(slot, "__v", "__t")}";

        // The hop interned the box; pass it through.
        return Wrapped(slot) is null
            ? $"__v => __v is {XamlTypeResolver.Fqn(slot)} ? __v : {UnsetValueFqn}"
            : $"__v => (object)({NarrowTo(slot, "__v", "__t")})";
    }

    string? ConverterExpression(XElement element, MarkupCall call) =>
        NamedValue(call, "Converter") is MarkupCall resource
            ? ConverterLookup(element, resource, "global::Noesis.IValueConverter")
            : null;

    string? ConverterLookup(XElement element, MarkupCall resource, string interfaceFqn)
    {
        if (resource.Name != "StaticResource")
            return null;

        if (ResourceKeyExpression(element, resource) is not { } key)
            return null;

        return $"({interfaceFqn}){ResourceValueExpression(key)}";
    }

    string ConverterParameter(MarkupCall call) =>
        NamedValue(call, "ConverterParameter") is string text
            ? Quote(XamlMarkupParser.Unescape(text))
            : "null";

    // Only the shape one constructor call can state: attributes or children would need the whole
    // object pipeline, whose emitted lines could not be taken back on a later refusal.
    string? InlineMultiConverter(XElement child)
    {
        if (child.Elements().Skip(1).Any())
            return null;

        var value = child.Elements().FirstOrDefault();
        if (
            value is null
            || value.Attributes().Any(a => !a.IsNamespaceDeclaration)
            || value.Elements().Any()
        )
            return null;

        var type = ResolveIn(value, value.Name.NamespaceName, value.Name.LocalName);
        if (type is null)
            return null;

        if (
            !type.AllInterfaces.Any(i =>
                XamlTypeResolver.Fqn(i) == "global::Noesis.IMultiValueConverter"
            )
        )
            return null;

        return resolver.HasConstructor(type) ? $"new {XamlTypeResolver.Fqn(type)}()" : null;
    }

    bool TryEmitCompiledBindingElement(
        XElement element,
        string target,
        INamedTypeSymbol type,
        IPropertySymbol property,
        XElement binding
    ) =>
        BindingElementCall(binding) is { } call
        && PlainSlot(property) is { } slot
        && TryEmitCompiledBinding(element, target, type, slot, call);

    // A compiled MultiBinding is one-way only: a write back would need ConvertBack, so a slot that
    // binds two-way by default keeps the native binding unless the document says OneWay itself.
    bool TryEmitCompiledMultiBinding(
        XElement element,
        string target,
        INamedTypeSymbol type,
        BindingSlot slot,
        XElement multi
    )
    {
        BeginAttempt();

        if (!OptedIntoCompiledBindings(element))
            return No("opted-out");

        if (!XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement"))
            return No("target-not-an-element");

        if (NamedByTemplateTrigger(element, slot.Name))
            return No("multi-binding-outranked-by-template-trigger");

        if (PresenterContentInTemplate(type, slot.Name))
            return No("multi-binding-presenter-content-in-a-template");

        string? converter = null;
        string? parameter = null;
        string? format = null;
        var oneWay = false;

        foreach (var attribute in multi.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            switch (attribute.Name.LocalName)
            {
                case "Converter":
                    if (XamlMarkupParser.Parse(attribute.Value) is not { } resource)
                        return No("converter-unresolved");

                    converter = ConverterLookup(
                        element,
                        resource,
                        "global::Noesis.IMultiValueConverter"
                    );
                    if (converter is null)
                        return No("converter-unresolved");

                    continue;
                case "ConverterParameter":
                    if (XamlMarkupParser.IsMarkup(attribute.Value))
                        return No("converter-parameter-is-markup");

                    parameter = Quote(XamlMarkupParser.Unescape(attribute.Value));
                    continue;
                case "StringFormat":
                    format = XamlMarkupParser.Unescape(attribute.Value);
                    continue;
                case "Mode":
                    if (attribute.Value.Trim() != "OneWay")
                        return No("multi-binding-is-one-way-only");

                    oneWay = true;
                    continue;
                default:
                    return No("multi-binding-knob-unsupported");
            }
        }

        if (slot.TwoWayByDefault && !oneWay)
            return No("direction-unsupported");

        if (format is not null && slot.Type.SpecialType != SpecialType.System_String)
            return No("string-format-into-a-non-string-slot");

        var parts = new List<string>();
        var partTypes = new List<ITypeSymbol>();
        foreach (var child in multi.Elements())
        {
            if (child.Name.LocalName == "MultiBinding.Converter")
            {
                if (converter is not null)
                    return No("converter-stated-twice");

                converter = InlineMultiConverter(child);
                if (converter is null)
                    return No("inline-converter-unresolved");

                continue;
            }

            if (child.Name.LocalName != "Binding")
                return No("multi-binding-child-is-not-a-binding");

            if (BindingElementCall(child) is not { } call)
                return No("child-attribute-unparsed");

            if (call.Named.Any(p => p.Key is "Converter" or "ConverterParameter" or "StringFormat"))
                return No("child-shapes-its-own-value");

            if (
                NamedValue(call, "Mode") is string mode
                && mode.Trim() is not ("OneWay" or "Default")
            )
                return No("child-direction-unsupported");

            if (PlainPath(call) is not { } path)
                return No("child-path-unsupported");

            if (ResolveSource(element, call) is not { } source)
                return No("child-source-unresolved");

            if (source.Resolver is null && slot.Name == "DataContext")
                return No("target-is-the-data-context");

            if (
                source.Resolver is null
                && XamlTypeResolver.DerivesFrom(type, "global::Noesis.ContentPresenter")
            )
                return No("target-adopts-its-content-as-data-context");

            if (ResolvePath(source, path) is not { } resolved)
                return No("child-path-unresolved");

            parts.Add(PartExpression(source, resolved));
            partTypes.Add(resolved.Type);
        }

        if (parts.Count == 0)
            return No("multi-binding-has-no-children");

        if (converter is null == (format is null))
            return No("multi-binding-needs-exactly-one-of-converter-or-format");

        var specFields = new List<string>
        {
            $"Parts = new {PartFqn}[] {{ {string.Join(", ", parts.ToArray())} }}",
        };

        if (converter is not null)
        {
            specFields.Add($"Converter = {converter}");
            specFields.Add($"ConverterParameter = {parameter ?? "null"}");
            specFields.Add($"TargetType = typeof({ConverterTargetType(slot)})");
        }
        else
        {
            if (FormatHoles(format!, parts.Count) is not { } parsed)
                return No("string-format-unlike-native");

            var args = new List<string>();
            foreach (var (index, held) in parsed.Holes)
            {
                if (FormatArgument(partTypes[index], held, $"__vs[{index}]") is not { } arg)
                    return No("string-format-unlike-native");

                // A null part shows as nothing rather than failing the whole format.
                args.Add($"__vs[{index}] is null ? (object)\"\" : {arg}");
            }

            specFields.Add($"Format = __vs => {FormatCall(parsed.Format, args)}");
        }

        specFields.Add($"Convert = {(converter is not null ? MultiFit(slot) : Coerce(slot.Type))}");

        if (slot.Assign is not null)
            specFields.Add($"Assign = {slot.Assign}");

        var spec = Shared(
            "multispec",
            $"new {MultiSpecFqn} {{ {string.Join(", ", specFields.ToArray())} }}"
        );
        Tally.Compiled++;
        EmitCompiledBind(
            target,
            receiver => $"{CompiledMultiBindingFqn}.Bind({receiver}, {slot.Reference}, {spec})"
        );
        return true;
    }
}
