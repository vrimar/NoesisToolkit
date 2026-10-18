using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public sealed partial class Gutter : Control
{
    [DependencyProperty]
    public partial GridLength Span { get; }
}

[NotInParallel("Noesis")]
public sealed class MisroutedSlotTests
{
    [Test]
    public async Task A_generated_GridLength_reads_what_the_parser_wrote()
    {
        NoesisRuntime.Start();

        var gutter = (Gutter)
            GUI.ParseXaml(
                "<t:Gutter xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
                    + "xmlns:t='clr-namespace:NoesisToolkit.Equivalence.Tests' Span='10'/>"
            );

        await Assert.That(gutter.Span).IsEqualTo(new GridLength(10));
    }

    [Test]
    public void A_managed_write_to_a_GridLength_slot_is_refused_rather_than_stored_as_a_pointer()
    {
        NoesisRuntime.Start();

        var definition = new ColumnDefinition();

        Assert.Throws<NotSupportedException>(() =>
            DependencyWrite.Value(
                ElementHandle.Of(definition),
                ColumnDefinition.WidthProperty,
                (object)new GridLength(10)
            )
        );
    }
}
