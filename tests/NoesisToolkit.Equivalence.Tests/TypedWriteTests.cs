using Noesis;
using NoesisToolkit.Mvvm;

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
}

[NotInParallel("Noesis")]
public sealed class TypedWriteTests
{
    const int Warmup = 8;
    const int Iterations = 64;

    static long Allocated(Action body)
    {
        for (var i = 0; i < Warmup; i++)
            body();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
            body();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

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

        await Assert.That(Allocated(() => typed.Count++)).IsEqualTo(0);
        await Assert.That(Allocated(() => typed.On = !typed.On)).IsEqualTo(0);
        await Assert.That(Allocated(() => typed.Scale = typed.Scale > 0 ? 0 : 1)).IsEqualTo(0);
        await Assert.That(Allocated(() => typed.Ratio = typed.Ratio > 0 ? 0 : 1)).IsEqualTo(0);
        await Assert
            .That(
                Allocated(() =>
                    typed.Align =
                        typed.Align == VerticalAlignment.Top
                            ? VerticalAlignment.Bottom
                            : VerticalAlignment.Top
                )
            )
            .IsEqualTo(0);
        await Assert.That(Allocated(() => typed.Inset = inset)).IsEqualTo(0);
    }
}
