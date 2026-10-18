using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Mvvm.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class DependencyPropertyGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "NoesisToolkit.Mvvm.DependencyPropertyAttribute";

    private const string WatcherFqn = "global::NoesisToolkit.Mvvm.CodeGen.DependencyWatcher";

    private static readonly DiagnosticDescriptor NotPartial = new(
        id: "NTK3001",
        title: "Containing type must be partial",
        messageFormat: "Type '{0}' must be declared partial to generate dependency properties",
        category: "NoesisToolkit",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor MisroutedSetter = new(
        id: "NTK3002",
        title: "Noesis cannot write this property type from managed code",
        messageFormat: "'{0}' is a {1}, which Noesis writes from managed code as a pointer; declare it {{ get; }} and set it from markup, a style or a binding",
        category: "NoesisToolkit",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal const string PropertiesStep = "NoesisMvvmDependencyProperties";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => BuildModel((IPropertySymbol)ctx.TargetSymbol, ctx.SemanticModel)
            )
            .WithTrackingName(PropertiesStep)
            .Collect();

        context.RegisterSourceOutput(
            models,
            static (spc, all) =>
            {
                foreach (var group in all.GroupBy(m => m.Owner.Fqn, StringComparer.Ordinal))
                {
                    var owner = group.First();

                    if (!owner.Owner.IsPartial)
                    {
                        spc.ReportDiagnostic(
                            Diagnostic.Create(
                                NotPartial,
                                owner.Owner.Location?.ToLocation(),
                                owner.Owner.NotPartial ?? owner.Owner.Display
                            )
                        );
                        continue;
                    }

                    foreach (var m in group)
                    {
                        if (m.Misrouted && !m.IsAttached && m.SetterKeyword is not null)
                            spc.ReportDiagnostic(
                                Diagnostic.Create(
                                    MisroutedSetter,
                                    m.Location?.ToLocation(),
                                    m.PropertyName,
                                    m.TypeDisplay
                                )
                            );
                    }

                    Output.Add(
                        spc,
                        $"{owner.Owner.Key}.DependencyProperties.g.cs",
                        GeneratePartial(owner, group)
                    );
                }
            }
        );
    }

    private static Model BuildModel(IPropertySymbol prop, SemanticModel model)
    {
        var attr = prop.GetAttributes()
            .First(a => a.AttributeClass?.ToDisplayString() == AttributeMetadataName);

        var typeFqn = prop.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).Fq();

        string? defaultValue = null;
        string? propertyChanged = null;
        string? metadataOptions = null;

        // Bound values, not source text: the generated file has none of the declaring file's usings.
        if (attr.AttributeConstructor is { } constructor)
        {
            var count = Math.Min(constructor.Parameters.Length, attr.ConstructorArguments.Length);
            for (var i = 0; i < count; i++)
            {
                var argument = attr.ConstructorArguments[i];
                switch (constructor.Parameters[i].Name)
                {
                    case "defaultValue":
                        if (!argument.IsNull && Literal(argument) is { } literal)
                            defaultValue = $"({typeFqn})({literal})";
                        break;
                    case "propertyChanged":
                        if (argument.Value is string { Length: > 0 } callback)
                            propertyChanged = CallbackReference(
                                attr,
                                i,
                                callback,
                                prop.ContainingType,
                                model
                            );
                        break;
                    case "metadataOptions":
                        if (
                            argument.Type is INamedTypeSymbol options
                            && argument.Value is { } flags
                            && Bits(flags) != 0
                        )
                            metadataOptions = EnumLiteral(options, flags, options.Name);
                        break;
                }
            }
        }

        var owner = prop.ContainingType;
        var underlying = prop.Type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
            ? nullable.TypeArguments[0]
            : prop.Type;

        return new Model(
            OwnerInfo.From(owner),
            LocationInfo.From(prop.Locations.FirstOrDefault()),
            prop.Name,
            prop.Type.ToDisplayString(SymbolText.FqnNullable),
            typeFqn,
            prop.Type.ToDisplayString(),
            NoesisSlots.IsMisrouted(underlying.ToDisplayString()),
            propertyChanged,
            defaultValue,
            metadataOptions,
            prop.IsStatic,
            OwnerNaming.IsPartial(prop),
            InheritsMemberNamed(owner, prop.Name),
            InheritsMemberNamed(owner, prop.Name + "Property"),
            TypedRead(prop.Type),
            WritesTyped(prop.Type),
            SyntaxFacts.GetText(prop.DeclaredAccessibility),
            prop.GetMethod is null ? null : AccessorModifier(prop, prop.GetMethod),
            prop.SetMethod is null ? null
                : prop.SetMethod.IsInitOnly ? "init"
                : "set",
            prop.SetMethod is null ? "" : AccessorModifier(prop, prop.SetMethod)
        );
    }

    // nameof binds to the bare name, so a callback another type declares is found through the syntax.
    private static string CallbackReference(
        AttributeData attr,
        int parameter,
        string name,
        INamedTypeSymbol owner,
        SemanticModel model
    )
    {
        if (
            ArgumentSyntax(attr, parameter, "propertyChanged")
                is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: { Count: 1 } arguments,
                }
            && model.GetSymbolInfo(arguments[0].Expression) is var info
            && (info.Symbol ?? info.CandidateSymbols.FirstOrDefault())
                is IMethodSymbol { ContainingType: { } declaring }
            && !InheritsFrom(owner, declaring)
        )
            return $"{declaring.Fq()}.{name}";

        return name;
    }

    private static ExpressionSyntax? ArgumentSyntax(
        AttributeData attr,
        int parameter,
        string parameterName
    )
    {
        if (
            attr.ApplicationSyntaxReference?.GetSyntax()
            is not AttributeSyntax { ArgumentList: { } list }
        )
            return null;

        for (var i = 0; i < list.Arguments.Count; i++)
        {
            var argument = list.Arguments[i];
            if (argument.NameEquals is not null)
                continue;

            if (
                argument.NameColon is { } named
                    ? named.Name.Identifier.Text == parameterName
                    : i == parameter
            )
                return argument.Expression;
        }

        return null;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol ancestor)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    t.OriginalDefinition,
                    ancestor.OriginalDefinition
                )
            )
                return true;
        }

        return false;
    }

    private static string AccessorModifier(IPropertySymbol prop, IMethodSymbol accessor) =>
        accessor.DeclaredAccessibility == prop.DeclaredAccessibility
            ? ""
            : SyntaxFacts.GetText(accessor.DeclaredAccessibility) + " ";

    private static string? Literal(TypedConstant constant) =>
        constant.Kind switch
        {
            _ when constant.IsNull => "null",
            TypedConstantKind.Enum when constant.Type is INamedTypeSymbol type => EnumLiteral(
                type,
                constant.Value!,
                type.Fq()
            ),
            TypedConstantKind.Type when constant.Value is ITypeSymbol type =>
                $"typeof({type.Fq()})",
            TypedConstantKind.Primitive => PrimitiveLiteral(constant.Value!),
            TypedConstantKind.Array when constant.Type is { } type => ArrayLiteral(
                type,
                constant.Values
            ),
            _ => null,
        };

    private static string? ArrayLiteral(ITypeSymbol type, IEnumerable<TypedConstant> values)
    {
        var elements = values.Select(Literal).ToList();
        return elements.Any(e => e is null)
            ? null
            : $"new {type.Fq()} {{ {string.Join(", ", elements)} }}";
    }

    private static string PrimitiveLiteral(object value) =>
        value switch
        {
            double d when double.IsNaN(d) => "double.NaN",
            double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
            double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
            double d => d.ToString("R", CultureInfo.InvariantCulture) + "D",
            float f when float.IsNaN(f) => "float.NaN",
            float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
            float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
            float f => f.ToString("R", CultureInfo.InvariantCulture) + "F",
            _ => SymbolDisplay.FormatPrimitive(
                value,
                quoteStrings: true,
                useHexadecimalNumbers: false
            ),
        };

    private static string EnumLiteral(INamedTypeSymbol type, object value, string typeName)
    {
        var bits = Bits(value);
        var members = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue && f.ConstantValue is not null)
            .Select(f => (f.Name, Bits: Bits(f.ConstantValue!)))
            .ToList();

        if (members.FirstOrDefault(m => m.Bits == bits) is { Name: { } exact })
            return $"{typeName}.{exact}";

        var parts = members.Where(m => m.Bits != 0 && (bits & m.Bits) == m.Bits).ToList();
        return parts.Aggregate(0L, (all, m) => all | m.Bits) == bits && parts.Count > 0
            ? string.Join(" | ", parts.Select(m => $"{typeName}.{m.Name}"))
            : $"({typeName})({bits.ToString(CultureInfo.InvariantCulture)})";
    }

    private static long Bits(object value) =>
        value is ulong wide ? unchecked((long)wide) : Convert.ToInt64(value);

    private static string? TypedRead(ITypeSymbol type)
    {
        // Noesis reads a null string as empty, so an annotated one reads the same way.
        if (type.SpecialType == SpecialType.System_String)
            return "String";

        // An object slot holding text unboxes a fresh string per read; the toolkit's shares it.
        if (type.SpecialType == SpecialType.System_Object)
            return "Value";

        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return null;

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => "Bool",
            SpecialType.System_Int32 => "Int",
            SpecialType.System_Int64 => "Long",
            SpecialType.System_Single => "Float",
            SpecialType.System_Double => "Double",
            _ => type.TypeKind == TypeKind.Enum ? $"Enum<{type.Fq()}>" : null,
        };
    }

    // The value types Noesis has a typed setter for; a nullable still goes through SetValue.
    private static bool WritesTyped(ITypeSymbol type) =>
        type.NullableAnnotation != NullableAnnotation.Annotated
        && (
            type.SpecialType
                is SpecialType.System_Boolean
                    or SpecialType.System_Int32
                    or SpecialType.System_Int64
                    or SpecialType.System_Single
                    or SpecialType.System_Double
            || type.TypeKind == TypeKind.Enum
            || type.ToDisplayString()
                is "Noesis.Thickness"
                    or "Noesis.Color"
                    or "Noesis.Point"
                    or "Noesis.Size"
                    or "Noesis.CornerRadius"
        );

    private static bool InheritsMemberNamed(INamedTypeSymbol owner, string name)
    {
        for (var t = owner.BaseType; t is not null; t = t.BaseType)
        {
            foreach (var member in t.GetMembers(name))
            {
                if (member.DeclaredAccessibility != Accessibility.Private)
                    return true;
            }
        }

        return false;
    }

    private static string GeneratePartial(Model owner, IEnumerable<Model> props)
    {
        var w = new CodeWriter();
        Output.Header(w);
        w.Line("using Noesis;");

        if (owner.Owner.Namespace is not null)
        {
            w.Line($"namespace {owner.Owner.Namespace};");
            w.Line();
        }

        var scopes = PartialTypeEmitter.Open(w, owner.Owner.Declarations);

        foreach (var m in props.GroupBy(p => p.PropertyName).Select(g => g.First()))
        {
            if (m.IsAttached)
                WriteAttachedProperty(w, m);
            else
                WriteDependencyProperty(w, m);
        }

        PartialTypeEmitter.Close(scopes);
        return w.ToString();
    }

    private static void WriteDependencyProperty(CodeWriter w, Model m)
    {
        string fieldNew = m.HidesInheritedField ? "new " : "";
        string propertyNew = m.HidesInheritedProperty ? "new " : "";

        w.Line($"public {fieldNew}static readonly DependencyProperty {m.PropertyName}Property =");
        WriteRegistration(w, m, attached: false);

        w.Line();
        using (
            w.Block($"{m.Accessibility} {propertyNew}partial {m.TypeFqnNullable} {m.PropertyName}")
        )
        {
            if (m.GetterModifier is { } getter)
                w.Line($"{getter}get => {Read(m, "this")};");
            if (m.SetterKeyword is { } setter)
                w.Line(
                    m.Misrouted
                        ? $"{m.SetterModifier}{setter} => throw new global::System.NotSupportedException();"
                        : $"{m.SetterModifier}{setter} => {Write(m, "this")};"
                );
        }
        w.Line();
    }

    // A bool, int, long, float, double or enum reads typed through the toolkit; Noesis' GetValue boxes
    // every value type anew.
    private static string Read(Model m, string target) =>
        m.TypedRead is { } typed
            ? $"{ReadFqn}.{typed}({target}, {m.PropertyName}Property){(typed == "Value" ? "!" : "")}"
        : m.Misrouted ? $"({m.TypeFqnNullable}){ReadFqn}.Value({target}, {m.PropertyName}Property)!"
        : $"({m.TypeFqnNullable}){target}.GetValue({m.PropertyName}Property)";

    private const string ReadFqn = "global::NoesisToolkit.Mvvm.CodeGen.DependencyRead";

    private const string HandleFqn = "global::NoesisToolkit.Mvvm.ElementHandle";

    // What a change callback that takes its object by handle reads its sibling properties with.
    private static string ReadByHandle(Model m, string target) =>
        m.TypedRead is { } typed
            ? $"{ReadFqn}.{typed}({target}, {m.PropertyName}Property){(typed == "Value" ? "!" : "")}"
            : $"({m.TypeFqnNullable}){ReadFqn}.Value({target}, {m.PropertyName}Property)!";

    private static string Write(Model m, string target) =>
        m.WritesTyped
            ? $"{WriteFqn}.Value({target}, {m.PropertyName}Property, value)"
            : $"{target}.SetValue({m.PropertyName}Property, value)";

    private const string WriteFqn = "global::NoesisToolkit.Mvvm.CodeGen.DependencyWrite";

    private static void WriteRegistration(CodeWriter w, Model m, bool attached)
    {
        using (w.Indented())
        {
            w.Line($"{WatcherFqn}.SelfNotifying(");
            using (w.Indented())
            {
                w.Line($"DependencyProperty.Register{(attached ? "Attached" : "")}(");
                using (w.Indented())
                {
                    w.Line(attached ? $"\"{m.PropertyName}\"," : $"nameof({m.PropertyName}),");
                    w.Line($"typeof({m.TypeFqn}),");
                    w.Line($"typeof({m.Owner.Fqn}),");
                    w.Line(BuildMetadata(m));
                }
                w.Line(")");
            }
            w.Line(");");
        }
    }

    private static void WriteAttachedProperty(CodeWriter w, Model m)
    {
        var fieldNew = m.HidesInheritedField ? "new " : "";

        w.Line($"public {fieldNew}static readonly DependencyProperty {m.PropertyName}Property =");
        WriteRegistration(w, m, attached: true);

        w.Line();
        w.Line(
            $"public static {m.TypeFqnNullable} Get{m.PropertyName}(DependencyObject element) =>"
        );
        using (w.Indented())
            w.Line($"{Read(m, "element")};");

        w.Line();
        w.Line($"public static {m.TypeFqnNullable} Get{m.PropertyName}({HandleFqn} element) =>");
        using (w.Indented())
            w.Line($"{ReadByHandle(m, "element")};");

        w.Line();
        if (!m.Misrouted)
        {
            w.Line(
                $"public static void Set{m.PropertyName}(DependencyObject element, {m.TypeFqnNullable} value) =>"
            );
            using (w.Indented())
                w.Line($"{Write(m, "element")};");
            w.Line();
        }

        if (m.IsPropertyPartial)
        {
            w.Line($"private static {m.TypeFqnNullable} __backing_{m.PropertyName} = default!;");
            using (
                w.Block($"{m.Accessibility} static partial {m.TypeFqnNullable} {m.PropertyName}")
            )
            {
                if (m.GetterModifier is { } getter)
                    w.Line($"{getter}get => __backing_{m.PropertyName};");
                if (m.SetterKeyword is { } setter)
                    w.Line($"{m.SetterModifier}{setter} => __backing_{m.PropertyName} = value;");
            }
            w.Line();
        }
    }

    private static string BuildMetadata(Model m)
    {
        string def = m.DefaultValue ?? $"default({m.TypeFqn})";
        string opts = m.MetadataOptions ?? "FrameworkPropertyMetadataOptions.None";

        // A null callback would not pick between the object and the handle overloads.
        var metadata = m.PropertyChanged is { } changed
            ? $"{WatcherFqn}.Metadata({def}, {opts}, {changed})"
            : $"{WatcherFqn}.Metadata({def}, {opts})";

        return m.Misrouted ? $"{ReadFqn}.Capturing({metadata})" : metadata;
    }

    private readonly record struct Model(
        OwnerInfo Owner,
        LocationInfo? Location,
        string PropertyName,
        string TypeFqnNullable,
        string TypeFqn,
        string TypeDisplay,
        bool Misrouted,
        string? PropertyChanged,
        string? DefaultValue,
        string? MetadataOptions,
        bool IsAttached,
        bool IsPropertyPartial,
        bool HidesInheritedProperty,
        bool HidesInheritedField,
        string? TypedRead,
        bool WritesTyped,
        string Accessibility,
        string? GetterModifier,
        string? SetterKeyword,
        string SetterModifier
    );
}
