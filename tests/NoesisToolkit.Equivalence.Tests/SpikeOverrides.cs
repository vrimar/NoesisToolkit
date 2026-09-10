using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public class SpikeBase : FrameworkElement
{
    public static readonly DependencyProperty KeptProperty = DependencyProperty.Register(
        "Kept",
        typeof(string),
        typeof(SpikeBase),
        new PropertyMetadata("base-default")
    );

    public string? Kept
    {
        get => (string?)GetValue(KeptProperty);
        set => SetValue(KeptProperty, value);
    }
}

public class SpikeDerived : SpikeBase { }

public class SpikeOwnCallback : FrameworkElement
{
    public static readonly List<string> Own = new();

    public static readonly DependencyProperty MineProperty = DependencyProperty.Register(
        "Mine",
        typeof(string),
        typeof(SpikeOwnCallback),
        new PropertyMetadata("own-default", (_, e) => Own.Add($"{e.NewValue}"))
    );

    public string? Mine
    {
        get => (string?)GetValue(MineProperty);
        set => SetValue(MineProperty, value);
    }
}

public class SpikeThrower : FrameworkElement
{
    public static readonly DependencyProperty BoomProperty = DependencyProperty.Register(
        "Boom",
        typeof(string),
        typeof(SpikeThrower),
        new PropertyMetadata(null)
    );

    public string? Boom
    {
        get => (string?)GetValue(BoomProperty);
        set => SetValue(BoomProperty, value);
    }
}

// OverrideMetadata is process-global with no uninstall, so each pair is claimed exactly once.
internal static class Overrides
{
    internal static readonly List<string> Seen = new();

    internal static DependencyProperty? LastProperty;

    static bool _installed;

    internal static void Install()
    {
        if (_installed)
            return;

        _installed = true;

        FrameworkElement.TagProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new PropertyMetadata(Record("FrameworkElement.Tag"))
        );

        SpikeBase.KeptProperty.OverrideMetadata(
            typeof(SpikeDerived),
            new PropertyMetadata(Record("SpikeDerived.Kept"))
        );

        SpikeThrower.BoomProperty.OverrideMetadata(
            typeof(SpikeThrower),
            new PropertyMetadata((_, _) => throw new InvalidOperationException("from the hook"))
        );

        SpikeOwnCallback.MineProperty.OverrideMetadata(
            typeof(SpikeOwnCallback),
            new PropertyMetadata(Record("SpikeOwnCallback.Mine"))
        );

        SpikeBase.KeptProperty.OverrideMetadata(
            typeof(SpikeBase),
            new PropertyMetadata(Record("SpikeBase.Kept"))
        );
    }

    static PropertyChangedCallback Record(string which) =>
        (_, e) =>
        {
            LastProperty = e.Property;
            Seen.Add($"{which} -> {e.NewValue}");
        };
}
