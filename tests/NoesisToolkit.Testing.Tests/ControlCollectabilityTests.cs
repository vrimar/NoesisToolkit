using Noesis;
using NoesisToolkit.Testing;
using TUnit.Core.Exceptions;

namespace NoesisToolkit.Testing.Tests;

// The probe drives one process-global GUI, so nothing here may run beside anything else.
[NotInParallel("Noesis")]
public class ControlCollectabilityTests
{
    static void EnsureReady()
    {
        if (ControlCollectability.Unavailable() is { } reason)
            throw new SkipTestException($"the renderless probe cannot run here: {reason}.");
    }

    [Test]
    public async Task A_control_is_reported_when_an_instance_handler_outlives_its_removal()
    {
        EnsureReady();

        var report = ControlCollectability.Probe([
            System.Reflection.Assembly.GetExecutingAssembly(),
        ]);

        await Assert.That(report.Leaked).Contains(typeof(PinnedControl).FullName!);
    }

    [Test]
    public async Task A_control_whose_handlers_are_static_is_collectable()
    {
        EnsureReady();

        var report = ControlCollectability.Probe([
            System.Reflection.Assembly.GetExecutingAssembly(),
        ]);

        await Assert.That(report.Leaked).DoesNotContain(typeof(CleanControl).FullName!);
        await Assert.That(report.Verified).IsGreaterThan(0);
    }

    [Test]
    public async Task An_excluded_control_is_neither_verified_nor_reported()
    {
        EnsureReady();

        var report = ControlCollectability.Probe(
            [System.Reflection.Assembly.GetExecutingAssembly()],
            exclude: [typeof(PinnedControl)]
        );

        await Assert.That(report.Leaked).IsEmpty();
    }
}

/// <summary>Pinned on purpose: the '-=' is a re-entry guard, so no teardown path removes it.</summary>
public sealed class PinnedControl : ContentControl
{
    public PinnedControl()
    {
        Loaded -= OnLoaded;
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e) => Focusable = true;
}

public sealed class CleanControl : ContentControl
{
    public CleanControl() => Loaded += OnLoaded;

    static void OnLoaded(object sender, RoutedEventArgs e) =>
        ((CleanControl)sender).Focusable = true;
}
