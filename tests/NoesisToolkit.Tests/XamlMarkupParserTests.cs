extern alias Compiler;

namespace NoesisToolkit.Tests;

public class XamlMarkupParserTests
{
    static object Named(Compiler::NoesisToolkit.CodeGen.MarkupCall call, string name) =>
        call.Named.Single(n => n.Key == name).Value;

    [Test]
    public async Task A_bare_extension_has_only_a_name()
    {
        var call = Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.Parse("{Binding}")!;

        await Assert.That(call.Name).IsEqualTo("Binding");
        await Assert.That(call.Positional).IsEmpty();
        await Assert.That(call.Named).IsEmpty();
    }

    [Test]
    public async Task A_positional_argument_is_kept_in_order()
    {
        var call = Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.Parse("{Binding Foo}")!;

        await Assert.That(call.Positional.Count).IsEqualTo(1);
        await Assert.That(call.Positional[0]).IsEqualTo("Foo");
    }

    [Test]
    public async Task Named_arguments_are_keyed_by_name()
    {
        var call = Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.Parse(
            "{Binding Path=Foo, Mode=TwoWay}"
        )!;

        await Assert.That(Named(call, "Path")).IsEqualTo("Foo");
        await Assert.That(Named(call, "Mode")).IsEqualTo("TwoWay");
    }

    [Test]
    public async Task An_escaped_brace_survives_as_literal_text()
    {
        var call = Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.Parse(
            "{Binding Foo, StringFormat={}{0} items}"
        )!;

        await Assert.That(call.Positional[0]).IsEqualTo("Foo");
        await Assert.That(Named(call, "StringFormat")).IsEqualTo("{0} items");
    }

    [Test]
    public async Task A_quoted_comma_does_not_split_the_argument()
    {
        var call = Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.Parse(
            "{Binding Foo, ConverterParameter='a,b'}"
        )!;

        await Assert.That(call.Positional.Count).IsEqualTo(1);
        await Assert.That(Named(call, "ConverterParameter")).IsEqualTo("a,b");
    }

    [Test]
    public async Task A_nested_extension_is_kept_whole()
    {
        var call = Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.Parse(
            "{Binding Foo, RelativeSource={RelativeSource AncestorType={x:Type Button}}}"
        )!;

        var nested = call.Named.Single(n => n.Key == "RelativeSource").Value;
        await Assert.That(nested).IsTypeOf<Compiler::NoesisToolkit.CodeGen.MarkupCall>();
        await Assert
            .That(((Compiler::NoesisToolkit.CodeGen.MarkupCall)nested).Name)
            .IsEqualTo("RelativeSource");
    }

    [Test]
    public async Task Plain_text_is_not_markup()
    {
        await Assert
            .That(Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.IsMarkup("Hello"))
            .IsFalse();
        await Assert
            .That(Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.IsMarkup("{Binding}"))
            .IsTrue();
        await Assert
            .That(Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.IsMarkup("{}{0}"))
            .IsFalse();
    }

    [Test]
    public async Task Unescape_strips_the_leading_escape_only()
    {
        await Assert
            .That(Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.Unescape("{}{0} items"))
            .IsEqualTo("{0} items");
        await Assert
            .That(Compiler::NoesisToolkit.CodeGen.XamlMarkupParser.Unescape("plain"))
            .IsEqualTo("plain");
    }
}
