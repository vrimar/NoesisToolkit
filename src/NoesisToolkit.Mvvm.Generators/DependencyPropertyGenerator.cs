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
            InheritsMemberNamed(owner, prop.Name + "Property")
        );
    }

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
            w.Line($"get => ({m.TypeFqnNullable})GetValue({m.PropertyName}Property);");
            w.Line($"set => SetValue({m.PropertyName}Property, value);");
        }
        w.Line();
    }

    private static void WriteRegistration(CodeWriter w, Model m, bool attached)
    {
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
            w.Line($"({m.TypeFqnNullable})element.GetValue({m.PropertyName}Property);");

        w.Line();
        w.Line(
            $"public static void Set{m.PropertyName}(DependencyObject element, {m.TypeFqnNullable} value) =>"
        );
        using (w.Indented())
            w.Line($"element.SetValue({m.PropertyName}Property, value);");
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

        return $"new FrameworkPropertyMetadata({def}, {opts}, {changed})";
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
        bool HidesInheritedField
    );
}
