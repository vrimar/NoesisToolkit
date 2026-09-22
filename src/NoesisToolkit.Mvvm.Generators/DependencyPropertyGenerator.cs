using System;
using System.Collections.Generic;
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

    internal const string PropertiesStep = "NoesisMvvmDependencyProperties";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => BuildModel((IPropertySymbol)ctx.TargetSymbol)
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

                    Output.Add(
                        spc,
                        $"{owner.Owner.Key}.DependencyProperties.g.cs",
                        GeneratePartial(owner, group)
                    );
                }
            }
        );
    }

    private static string Parameter(int index) =>
        index switch
        {
            0 => "defaultValue",
            1 => "propertyChanged",
            2 => "metadataOptions",
            _ => "",
        };

    private static Model BuildModel(IPropertySymbol prop)
    {
        var attr = prop.GetAttributes()
            .First(a => a.AttributeClass?.ToDisplayString() == AttributeMetadataName);

        string? defaultValue = null;
        string? propertyChanged = null;
        string? metadataOptions = null;

        // Syntax, not AttributeData: the default value is emitted verbatim and a callback is a method group.
        if (
            attr.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax
            && syntax.ArgumentList is { Arguments.Count: > 0 } list
        )
        {
            var positional = 0;
            foreach (var argument in list.Arguments)
            {
                var name =
                    argument.NameColon?.Name.Identifier.Text
                    ?? argument.NameEquals?.Name.Identifier.Text
                    ?? Parameter(positional++);
                var text = argument.Expression.ToString();

                switch (name)
                {
                    case "defaultValue":
                        if (!text.Equals("null", StringComparison.OrdinalIgnoreCase))
                            defaultValue = text;
                        break;
                    case "propertyChanged":
                        propertyChanged = CleanCallbackString(text);
                        break;
                    case "metadataOptions":
                        metadataOptions = text;
                        break;
                }
            }
        }

        var owner = prop.ContainingType;

        return new Model(
            OwnerInfo.From(owner),
            prop.Name,
            prop.Type.ToDisplayString(SymbolText.FqnNullable),
            prop.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).Fq(),
            propertyChanged,
            defaultValue,
            metadataOptions,
            prop.IsStatic,
            OwnerNaming.IsPartial(prop),
            InheritsMemberNamed(owner, prop.Name),
            InheritsMemberNamed(owner, prop.Name + "Property"),
            TypedRead(prop.Type),
            WritesTyped(prop.Type)
        );
    }

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
        using (w.Block($"public {propertyNew}partial {m.TypeFqnNullable} {m.PropertyName}"))
        {
            w.Line($"get => {Read(m, "this")};");
            w.Line($"set => {Write(m, "this")};");
        }
        w.Line();
    }

    // A bool, int, long, float, double or enum reads typed through the toolkit; Noesis' GetValue boxes
    // every value type anew.
    private static string Read(Model m, string target) =>
        m.TypedRead is { } typed
            ? $"{ReadFqn}.{typed}({target}, {m.PropertyName}Property){(typed == "Value" ? "!" : "")}"
            : $"({m.TypeFqnNullable}){target}.GetValue({m.PropertyName}Property)";

    private const string ReadFqn = "global::NoesisToolkit.Mvvm.CodeGen.DependencyRead";

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
        w.Line(
            $"public static void Set{m.PropertyName}(DependencyObject element, {m.TypeFqnNullable} value) =>"
        );
        using (w.Indented())
            w.Line($"{Write(m, "element")};");
        w.Line();

        if (m.IsPropertyPartial)
        {
            w.Line($"private static {m.TypeFqnNullable} __backing_{m.PropertyName} = default!;");
            using (w.Block($"public static partial {m.TypeFqnNullable} {m.PropertyName}"))
            {
                w.Line($"get => __backing_{m.PropertyName};");
                w.Line($"set => __backing_{m.PropertyName} = value;");
            }
            w.Line();
        }
    }

    private static string BuildMetadata(Model m)
    {
        string def = m.DefaultValue ?? $"default({m.TypeFqn})";
        string changed = m.PropertyChanged ?? "null";
        string opts = m.MetadataOptions ?? "FrameworkPropertyMetadataOptions.None";

        return $"{WatcherFqn}.Metadata({def}, {opts}, {changed})";
    }

    private static string? CleanCallbackString(string raw)
    {
        string s = raw.Trim();

        if (s.StartsWith("nameof", StringComparison.Ordinal))
        {
            int open = s.IndexOf('(');
            int close = s.IndexOf(')');
            if (open >= 0 && close > open)
                s = s.Substring(open + 1, close - open - 1).Trim();
        }

        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            s = s.Substring(1, s.Length - 2);

        // A blank name is no callback at all; interpolated, it leaves the argument missing.
        return s.Length == 0 ? null : s;
    }

    private readonly record struct Model(
        OwnerInfo Owner,
        string PropertyName,
        string TypeFqnNullable,
        string TypeFqn,
        string? PropertyChanged,
        string? DefaultValue,
        string? MetadataOptions,
        bool IsAttached,
        bool IsPropertyPartial,
        bool HidesInheritedProperty,
        bool HidesInheritedField,
        string? TypedRead,
        bool WritesTyped
    );
}
