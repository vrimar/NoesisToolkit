using Noesis;
using NoesisToolkit.Mvvm.CodeGen;
using NoesisToolkit.Testing;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class StringReadTests
{
    public static IEnumerable<Func<string?>> Texts() =>
        [() => "abc", () => "", () => null, () => "héllo ✓ 𝄞", () => new string('x', 600)];

    [Test]
    [MethodDataSource(nameof(Texts))]
    public async Task A_string_read_is_what_Noesis_reads(string? text)
    {
        NoesisRuntime.Start();
        var block = new TextBlock { Text = text };

        await Assert
            .That(DependencyRead.String(block, TextBlock.TextProperty))
            .IsEqualTo(block.Text);
        await Assert
            .That(DependencyRead.Value(block, TextBlock.TextProperty))
            .IsEqualTo(block.Text);
    }

    [Test]
    [MethodDataSource(nameof(Texts))]
    public async Task A_string_copied_out_is_what_Noesis_reads(string? text)
    {
        NoesisRuntime.Start();
        var block = new TextBlock { Text = text };

        var buffer = new char[1024];
        var copied = DependencyRead.TryCopyString(
            block,
            TextBlock.TextProperty,
            buffer,
            out var written
        );

        await Assert.That(copied).IsTrue();
        await Assert.That(new string(buffer, 0, written)).IsEqualTo(block.Text);
    }

    [Test]
    public async Task A_string_copied_out_allocates_nothing_and_refuses_what_does_not_fit()
    {
        NoesisRuntime.Start();
        var block = new TextBlock { Text = "héllo ✓ 𝄞" };
        var buffer = new char[64];

        var allocated = AllocationCost.Of(() =>
            DependencyRead.TryCopyString(block, TextBlock.TextProperty, buffer, out _)
        );
        var fits = DependencyRead.TryCopyString(
            block,
            TextBlock.TextProperty,
            buffer.AsSpan(0, 4),
            out _
        );

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(fits).IsFalse();
    }

    [Test]
    public async Task Rereading_the_same_text_shares_one_string_and_allocates_nothing()
    {
        NoesisRuntime.Start();
        var first = new TextBlock { Text = "Level 12" };
        var second = new TextBlock { Text = "Level 12" };

        var a = DependencyRead.String(first, TextBlock.TextProperty);
        var b = DependencyRead.String(second, TextBlock.TextProperty);

        await Assert.That(b).IsSameReferenceAs(a);
        await Assert
            .That(AllocationCost.Of(() => DependencyRead.Value(first, TextBlock.TextProperty)))
            .IsEqualTo(0);
    }

    [Test]
    public async Task Text_in_an_object_property_is_shared_too()
    {
        NoesisRuntime.Start();
        var border = new Border { Tag = "tip" };

        var read = DependencyRead.Value(border, FrameworkElement.TagProperty);

        await Assert.That(read).IsEqualTo("tip");
        await Assert
            .That(
                AllocationCost.Of(() => DependencyRead.Value(border, FrameworkElement.TagProperty))
            )
            .IsEqualTo(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    [Arguments(7)]
    [Arguments(1000)]
    [Arguments(2.5f)]
    [Arguments(0.25)]
    public async Task A_primitive_in_an_object_property_reads_as_Noesis_reads_it_and_reuses_its_box(
        object value
    )
    {
        NoesisRuntime.Start();
        var border = new Border { Tag = value };

        var read = DependencyRead.Value(border, FrameworkElement.TagProperty);

        await Assert.That(read).IsEqualTo(border.GetValue(FrameworkElement.TagProperty));
        await Assert
            .That(
                AllocationCost.Of(() => DependencyRead.Value(border, FrameworkElement.TagProperty))
            )
            .IsEqualTo(0);
    }

    [Test]
    public async Task A_non_text_object_property_reads_as_Noesis_reads_it()
    {
        NoesisRuntime.Start();
        var brush = new SolidColorBrush(Colors.Red);
        var border = new Border { Tag = brush };

        await Assert
            .That(DependencyRead.Value(border, FrameworkElement.TagProperty))
            .IsSameReferenceAs(brush);
    }
}
