using System.Collections;
using System.Reflection;
using Noesis;
using NoesisToolkit.CodeGen;
using TUnit.Assertions.Enums;

namespace NoesisToolkit.Equivalence.Tests;

public static class ParserStatics
{
    public static string Hint => "hint";
}

public enum ParserEnum
{
    First,
    Second,
}

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

    [Test]
    public async Task Unregistrable_is_every_misrouted_type_Register_refuses_with_a_default()
    {
        NoesisRuntime.Start();

        var refused = new List<string>();
        // Registering a Matrix4 property kills the process, so it is listed without being measured.
        foreach (var name in NoesisSlots.Misrouted.Where(n => n != "Noesis.Matrix4"))
        {
            var type = typeof(DependencyObject).Assembly.GetType(name)!;
            try
            {
                DependencyProperty.Register(
                    "Probe" + type.Name,
                    type,
                    typeof(RegistrationProbe),
                    new PropertyMetadata(Activator.CreateInstance(type))
                );
            }
            catch (ArgumentException)
            {
                refused.Add(name);
            }
        }

        await Assert
            .That(refused.Append("Noesis.Matrix4"))
            .IsEquivalentTo(NoesisSlots.Unregistrable, CollectionOrdering.Any);
    }

    sealed class RegistrationProbe : DependencyObject { }

    [Test]
    public async Task ParserReadsStatic_is_every_x_Static_the_parser_loads()
    {
        NoesisRuntime.Start();

        var noesis = typeof(DependencyObject).Assembly;
        var statics = noesis
            .GetExportedTypes()
            .Where(t => t.Namespace == "Noesis" && !t.IsNested)
            .Append(typeof(ParserStatics))
            .Append(typeof(ParserEnum))
            .SelectMany(owner =>
                owner
                    .GetMembers(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
                    )
                    .Where(m =>
                        m is FieldInfo or PropertyInfo { GetMethod: not null } && !IsIndexer(m)
                    )
                    .Select(m =>
                        (
                            Owner: owner,
                            m.Name,
                            Type: (m as FieldInfo)?.FieldType ?? ((PropertyInfo)m).PropertyType
                        )
                    )
            )
            .Where(s => !s.Name.Contains("value__", StringComparison.Ordinal))
            .ToList();

        var wrong = new List<string>();
        foreach (var (owner, member, type) in statics)
        {
            var prefix = owner.Assembly == noesis ? "" : "t:";
            var border = (Border)
                GUI.ParseXaml(
                    "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
                        + "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' "
                        + $"xmlns:t='clr-namespace:{owner.Namespace};assembly={owner.Assembly.GetName().Name}' "
                        + $"Tag='{{x:Static {prefix}{owner.Name}.{member}}}' />"
                );

            var expected = NoesisSlots.ParserReadsStatic(
                owner.FullName!,
                owner.IsEnum,
                owner.Assembly.GetName().Name,
                member,
                typeof(RoutedCommand).IsAssignableFrom(type)
            );
            if (border.Tag is not null != expected)
                wrong.Add($"{owner.Name}.{member}");
        }

        await Assert.That(statics.Count(s => s.Owner.IsEnum)).IsGreaterThan(0);
        await Assert.That(wrong).IsEmpty();
    }

    static bool IsIndexer(MemberInfo member) =>
        member is PropertyInfo property && property.GetIndexParameters().Length > 0;

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
