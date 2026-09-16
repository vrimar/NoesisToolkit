using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public sealed partial class Gauge : Control
{
    public static int Changes;

    [DependencyProperty(0)]
    public partial int Level { get; set; }

    [DependencyProperty(0, nameof(OnReadingChanged))]
    public partial int Reading { get; set; }

    static void OnReadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        Changes++;
}

public sealed class HandGauge : Control
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        "Level",
        typeof(float),
        typeof(HandGauge),
        new FrameworkPropertyMetadata(3f)
    );

    public float Level
    {
        get => (float)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public static readonly DependencyProperty MarkProperty = DependencyProperty.RegisterAttached(
        "Mark",
        typeof(float),
        typeof(HandGauge),
        new FrameworkPropertyMetadata(1f)
    );

    public static void SetMark(DependencyObject element, float value) =>
        element.SetValue(MarkProperty, value);
}

[NotInParallel("Noesis")]
public sealed class ManagedWatchTests
{
    static List<string> CaptureWarnings()
    {
        var warnings = new List<string>();
        Log.SetLogCallback(
            (level, _, message) =>
            {
                if (level is LogLevel.Warning or LogLevel.Error)
                    warnings.Add(message);
            }
        );
        return warnings;
    }

    [Test]
    public async Task A_generated_property_is_watched_without_rewriting_its_metadata()
    {
        NoesisRuntime.Start();
        var warnings = CaptureWarnings();

        var target = new Gauge();
        var seen = 0;
        DependencyWatcher.Watch(target, Gauge.LevelProperty, () => seen++);

        var grid = new Grid { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(grid, target);
        var baseline = seen;

        target.Level = 7;
        NoesisRuntime.Pump(view, grid);

        await Assert.That(target.Level).IsEqualTo(7);
        await Assert.That(seen).IsGreaterThan(baseline);
        await Assert.That(warnings).IsEmpty().Because(string.Join(" | ", warnings));
    }

    [Test]
    public async Task A_generated_property_keeps_its_own_change_callback_while_watched()
    {
        NoesisRuntime.Start();
        var warnings = CaptureWarnings();

        var target = new Gauge();
        var seen = 0;
        DependencyWatcher.Watch(target, Gauge.ReadingProperty, () => seen++);

        var grid = new Grid { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(grid, target);
        var watched = seen;
        var own = Gauge.Changes;

        target.Reading = 5;
        NoesisRuntime.Pump(view, grid);

        await Assert.That(seen).IsGreaterThan(watched);
        await Assert.That(Gauge.Changes).IsGreaterThan(own);
        await Assert.That(warnings).IsEmpty().Because(string.Join(" | ", warnings));
    }

    [Test]
    public async Task A_generated_property_a_style_already_sets_is_watched()
    {
        NoesisRuntime.Start();
        var warnings = CaptureWarnings();

        var style = new Style { TargetType = typeof(Gauge) };
        style.Setters.Add(new Setter { Property = Gauge.LevelProperty, Value = 2 });

        var target = new Gauge { Style = style };
        var grid = new Grid { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(grid, target);

        var seen = 0;
        DependencyWatcher.Watch(target, Gauge.LevelProperty, () => seen++);
        NoesisRuntime.Pump(view, grid);
        var baseline = seen;

        target.Level = 9;
        NoesisRuntime.Pump(view, grid);

        await Assert.That(target.Level).IsEqualTo(9);
        await Assert.That(seen).IsGreaterThan(baseline);
        await Assert.That(warnings).IsEmpty().Because(string.Join(" | ", warnings));
    }

    [Test]
    public async Task A_hand_registered_property_is_watched_without_a_warning()
    {
        NoesisRuntime.Start();
        var warnings = CaptureWarnings();

        var target = new HandGauge();
        var seen = 0;
        DependencyWatcher.Watch(target, HandGauge.LevelProperty, () => seen++);

        var grid = new Grid { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(grid, target);
        var baseline = seen;

        target.Level = 7;
        NoesisRuntime.Pump(view, grid);

        await Assert.That(seen).IsGreaterThan(baseline);
        await Assert.That(warnings).IsEmpty().Because(string.Join(" | ", warnings));
    }

    [Test]
    public async Task A_hand_registered_attached_property_is_watched_without_a_warning()
    {
        NoesisRuntime.Start();
        var warnings = CaptureWarnings();

        var target = new Border();
        var seen = 0;
        DependencyWatcher.Watch(target, HandGauge.MarkProperty, () => seen++);

        var grid = new Grid { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(grid, target);
        var baseline = seen;

        HandGauge.SetMark(target, 4);
        NoesisRuntime.Pump(view, grid);

        await Assert.That(seen).IsGreaterThan(baseline);
        await Assert.That(warnings).IsEmpty().Because(string.Join(" | ", warnings));
    }
}
