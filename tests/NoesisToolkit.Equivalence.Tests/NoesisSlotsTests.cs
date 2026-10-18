using System.Collections;
using System.Reflection;
using Noesis;
using NoesisToolkit.CodeGen;
using TUnit.Assertions.Enums;

namespace NoesisToolkit.Equivalence.Tests;

// Facts about one Noesis build: an upgrade that moves one fails here.
[NotInParallel("Noesis")]
public sealed class NoesisSlotsTests
{
    [Test]
    [Arguments("_getFunctions")]
    [Arguments("_setFunctions")]
    public async Task Typed_is_what_GetValue_and_SetValue_dispatch_to_a_typed_native(string table)
    {
        NoesisRuntime.Start();

        var routes = (IDictionary)
            typeof(DependencyObject)
                .GetField(table, BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
        var typed = routes
            .Keys.Cast<Type>()
            .Select(t => (Nullable.GetUnderlyingType(t) ?? t).FullName!)
            .Distinct();

        await Assert.That(typed).IsEquivalentTo(NoesisSlots.Typed, CollectionOrdering.Any);
    }

    [Test]
    public async Task Misrouted_is_every_value_type_stored_natively_with_no_typed_native()
    {
        NoesisRuntime.Start();

        var extend = typeof(DependencyObject).Assembly.GetType("Noesis.Extend")!;
        var native = (IDictionary)
            extend
                .GetField("_nativeTypes", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
        var stored = native
            .Values.Cast<object>()
            .Where(info => Read(info, "Kind").ToString() == "Basic")
            .Select(info => Underlying((Type)Read(info, "Type")))
            .Where(t => !t.IsEnum)
            .Select(t => t.FullName!)
            .Distinct();

        await Assert
            .That(stored.Except(NoesisSlots.Typed))
            .IsEquivalentTo(NoesisSlots.Misrouted, CollectionOrdering.Any);
    }

    [Test]
    public async Task The_registration_lists_name_every_Noesis_property_registered_apart_from_its_CLR_type()
    {
        NoesisRuntime.Start();

        var uintRegistered = new List<(string, string)>();
        var unregistered = new List<(string, string)>();

        foreach (var (owner, name, clr, property) in NoesisProperties())
        {
            Type registered;
            try
            {
                registered = property.PropertyType;
            }
            catch (InvalidOperationException)
            {
                unregistered.Add((owner.FullName!, name));
                continue;
            }

            var stored = Underlying(registered);
            if (stored != clr && stored.IsValueType && clr.IsValueType)
                uintRegistered.Add((owner.FullName!, name));
        }

        await Assert
            .That(uintRegistered)
            .IsEquivalentTo(NoesisSlots.UIntRegistered, CollectionOrdering.Any);
        await Assert
            .That(unregistered)
            .IsEquivalentTo(NoesisSlots.Unregistered, CollectionOrdering.Any);
    }

    static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    static object Read(object info, string property) =>
        info.GetType().GetProperty(property)!.GetValue(info)!;

    static IEnumerable<(
        Type Owner,
        string Name,
        Type Clr,
        DependencyProperty Property
    )> NoesisProperties()
    {
        foreach (var owner in typeof(DependencyObject).Assembly.GetExportedTypes())
        {
            var fields = owner
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => f.FieldType == typeof(DependencyProperty))
                .Select(f => (f.Name, Read: (Func<object?>)(() => f.GetValue(null))));
            var properties = owner
                .GetProperties(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
                )
                .Where(p => p.PropertyType == typeof(DependencyProperty))
                .Select(p => (p.Name, Read: (Func<object?>)(() => p.GetValue(null))));

            foreach (var (member, read) in fields.Concat(properties))
            {
                if (!member.EndsWith("Property", StringComparison.Ordinal))
                    continue;

                var name = member.Substring(0, member.Length - "Property".Length);
                var clr =
                    owner
                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(p => p.Name == name)
                        ?.PropertyType
                    ?? owner
                        .GetMethod("Get" + name, BindingFlags.Public | BindingFlags.Static)
                        ?.ReturnType;

                if (clr is null || read() is not DependencyProperty property)
                    continue;

                yield return (owner, name, Nullable.GetUnderlyingType(clr) ?? clr, property);
            }
        }
    }
}
