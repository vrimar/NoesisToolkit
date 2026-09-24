using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;
using NoesisToolkit.Testing;

namespace NoesisToolkit.Equivalence.Tests;

public sealed partial class Typed : Control
{
    [DependencyProperty(0)]
    public partial int Count { get; set; }

    [DependencyProperty(0.0)]
    public partial double Scale { get; set; }

    [DependencyProperty(0f)]
    public partial float Ratio { get; set; }

    [DependencyProperty(true)]
    public partial bool On { get; set; }

    [DependencyProperty(VerticalAlignment.Top)]
    public partial VerticalAlignment Align { get; set; }

    [DependencyProperty]
    public partial Thickness Inset { get; set; }

    [DependencyProperty("")]
    public partial string Label { get; set; }
}

[NotInParallel("Noesis")]
public sealed class TypedWriteTests
{
    [Test]
    public async Task A_typed_write_lands_where_Noesis_reads_it()
    {
        NoesisRuntime.Start();

        var typed = new Typed();
        NoesisRuntime.Show(typed);

        typed.Count = 5;
        typed.Scale = 1.5;
        typed.Ratio = 0.25f;
        typed.On = false;
        typed.Align = VerticalAlignment.Bottom;
        typed.Inset = new Thickness(1, 2, 3, 4);

        await Assert.That(typed.GetValue(Typed.CountProperty)).IsEqualTo(5);
        await Assert.That(typed.GetValue(Typed.ScaleProperty)).IsEqualTo(1.5);
        await Assert.That(typed.GetValue(Typed.RatioProperty)).IsEqualTo(0.25f);
        await Assert.That(typed.GetValue(Typed.OnProperty)).IsEqualTo(false);
        await Assert.That(typed.GetValue(Typed.AlignProperty)).IsEqualTo(VerticalAlignment.Bottom);
        await Assert.That(typed.GetValue(Typed.InsetProperty)).IsEqualTo(new Thickness(1, 2, 3, 4));
        await Assert.That(typed.Count).IsEqualTo(5);
        await Assert.That(typed.Align).IsEqualTo(VerticalAlignment.Bottom);
    }

    [Test]
    public async Task A_value_typed_property_is_written_and_read_back_without_allocating()
    {
        NoesisRuntime.Start();

        var typed = new Typed();
        NoesisRuntime.Show(typed);
        var inset = new Thickness(2);

        await Assert.That(AllocationCost.Of(() => typed.Count++)).IsEqualTo(0);
        await Assert.That(AllocationCost.Of(() => typed.On = !typed.On)).IsEqualTo(0);
        await Assert
            .That(AllocationCost.Of(() => typed.Scale = typed.Scale > 0 ? 0 : 1))
            .IsEqualTo(0);
        await Assert
            .That(AllocationCost.Of(() => typed.Ratio = typed.Ratio > 0 ? 0 : 1))
            .IsEqualTo(0);
        await Assert
            .That(
                AllocationCost.Of(() =>
                    typed.Align =
                        typed.Align == VerticalAlignment.Top
                            ? VerticalAlignment.Bottom
                            : VerticalAlignment.Top
                )
            )
            .IsEqualTo(0);
        await Assert.That(AllocationCost.Of(() => typed.Inset = inset)).IsEqualTo(0);
    }

    [Test]
    public async Task Text_written_from_a_span_is_what_Noesis_reads_back()
    {
        NoesisRuntime.Start();

        var typed = new Typed();
        NoesisRuntime.Show(typed);
        var longText = new string('x', 700);

        DependencyWrite.String(typed, Typed.LabelProperty, "12,345 ünïcødé".AsSpan());
        await Assert.That(typed.Label).IsEqualTo("12,345 ünïcødé");

        DependencyWrite.String(typed, Typed.LabelProperty, longText.AsSpan());
        await Assert.That(typed.Label).IsEqualTo(longText);

        DependencyWrite.String(typed, Typed.LabelProperty, ReadOnlySpan<char>.Empty);
        await Assert.That(typed.Label).IsEqualTo("");
    }

    [Test]
    public async Task Text_formatted_into_a_buffer_is_written_without_allocating()
    {
        NoesisRuntime.Start();

        var typed = new Typed();
        NoesisRuntime.Show(typed);
        var next = 0;

        var allocated = AllocationCost.Of(() =>
        {
            Span<char> buffer = stackalloc char[16];
            (next++).TryFormat(buffer, out var written);
            DependencyWrite.String(typed, Typed.LabelProperty, buffer[..written]);
        });

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(typed.Label).IsEqualTo((next - 1).ToString());
    }
}
