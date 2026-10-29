using System.Reflection;
using NoesisToolkit.CodeGen;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class RuntimeNamesTests
{
    const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public async Task Every_name_the_generators_emit_resolves_in_NoesisToolkit_Mvvm()
    {
        var mvvm = typeof(CompiledBinding).Assembly;
        var unresolved = new List<string>();

        foreach (var names in typeof(RuntimeNames).GetNestedTypes(Declared))
        {
            var typeName = (string)
                names.GetField("Type", Declared | BindingFlags.Static)!.GetRawConstantValue()!;
            if (mvvm.GetType(typeName) is not { } type)
            {
                unresolved.Add(typeName);
                continue;
            }

            foreach (var (name, value) in Constants(names))
            {
                var resolves = name switch
                {
                    "Type" => true,
                    "Fqn" => value == "global::" + typeName,
                    "Generic" => mvvm.GetType(value) is not null,
                    _ when value.StartsWith("global::", StringComparison.Ordinal) => StaticMember(
                        mvvm,
                        value.Substring("global::".Length)
                    ),
                    _ => type.GetMember(
                        value,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                    ).Length > 0,
                };

                if (!resolves)
                    unresolved.Add($"{names.Name}.{name} = {value}");
            }

            foreach (var parameters in names.GetNestedTypes(Declared))
            foreach (var (name, value) in Constants(parameters))
            {
                if (!type.GetConstructors().Any(c => c.GetParameters().Any(p => p.Name == value)))
                    unresolved.Add($"{names.Name}.{parameters.Name}.{name} = {value}");
            }
        }

        await Assert.That(unresolved).IsEmpty();
    }

    static IEnumerable<(string Name, string Value)> Constants(Type names) =>
        names
            .GetFields(Declared | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!));

    static bool StaticMember(Assembly mvvm, string qualified)
    {
        var dot = qualified.LastIndexOf('.');
        return mvvm.GetType(qualified.Substring(0, dot)) is { } owner
            && owner
                .GetMember(qualified.Substring(dot + 1), BindingFlags.Public | BindingFlags.Static)
                .Length > 0;
    }
}
