using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public enum SpikeStage
{
    First,
    Second,
    Third,
}

public sealed class SpikeItem : INotifyPropertyChanged
{
    string _label = "";

    public string Label
    {
        get => _label;
        set
        {
            _label = value;
            Raise();
        }
    }

    bool _flag;

    public bool Flag
    {
        get => _flag;
        set
        {
            _flag = value;
            Raise();
        }
    }

    int _index;

    public int Index
    {
        get => _index;
        set
        {
            _index = value;
            Raise();
        }
    }

    double _ratio;

    public double Ratio
    {
        get => _ratio;
        set
        {
            _ratio = value;
            Raise();
        }
    }

    object? _payload;

    public object? Payload
    {
        get => _payload;
        set
        {
            _payload = value;
            Raise();
        }
    }

    SpikeStage _stage;

    public SpikeStage Stage
    {
        get => _stage;
        set
        {
            _stage = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Shared per name, so an allocation test measures the binding rather than the model.
    static readonly Dictionary<string, PropertyChangedEventArgs> Args = new();

    void Raise([CallerMemberName] string name = "")
    {
        if (!Args.TryGetValue(name, out var args))
            Args[name] = args = new PropertyChangedEventArgs(name);

        PropertyChanged?.Invoke(this, args);
    }
}

/// <summary>The SpikeItem hops the compiler would emit, written once instead of at every spec.</summary>
public static class Hop
{
    public static BindingHop Label =>
        new BindingHop(nameof(SpikeItem.Label), o => ((SpikeItem)o).Label);

    public static BindingHop Index =>
        new BindingHop(nameof(SpikeItem.Index), o => ((SpikeItem)o).Index);

    public static BindingHop Flag =>
        new BindingHop(nameof(SpikeItem.Flag), o => ((SpikeItem)o).Flag);

    public static BindingHop Ratio =>
        new BindingHop(nameof(SpikeItem.Ratio), o => ((SpikeItem)o).Ratio);

    public static BindingHop Payload =>
        new BindingHop(nameof(SpikeItem.Payload), o => ((SpikeItem)o).Payload);

    public static BindingHop Stage =>
        new BindingHop(nameof(SpikeItem.Stage), o => ((SpikeItem)o).Stage);
}

public class SpikeControl : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        "Label",
        typeof(string),
        typeof(SpikeControl),
        new FrameworkPropertyMetadata("host-default")
    );

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty AlignProperty = DependencyProperty.Register(
        "Align",
        typeof(VerticalAlignment),
        typeof(SpikeControl),
        new FrameworkPropertyMetadata(VerticalAlignment.Center)
    );

    public VerticalAlignment Align
    {
        get => (VerticalAlignment)GetValue(AlignProperty);
        set => SetValue(AlignProperty, value);
    }

    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        "Count",
        typeof(int),
        typeof(SpikeControl),
        new FrameworkPropertyMetadata(3)
    );

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }
}

/// <summary>A generated subclass would key an implicit style by its own exact type, which is why
/// the compiler wires elements in place instead.</summary>
public class BoundTextBlock : TextBlock { }

public sealed class SpikeCommand : System.Windows.Input.ICommand
{
    public int Ran;

    public event System.EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => Ran++;
}

public sealed class SpikeOwner : INotifyPropertyChanged
{
    string _title = "";

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            Raise();
        }
    }

    public VerticalAlignment Align { get; } = VerticalAlignment.Top;

    public System.Collections.ObjectModel.ObservableCollection<SpikeItem> Items { get; } = new();

    public SpikeCommand Poke { get; } = new();

    bool _flag;

    public bool Flag
    {
        get => _flag;
        set
        {
            _flag = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class SpikeProbeHook
{
    public static readonly List<string> Seen = new();

    public static readonly DependencyProperty WatchProperty = DependencyProperty.RegisterAttached(
        "Watch",
        typeof(int),
        typeof(SpikeProbeHook),
        new PropertyMetadata(0, OnWatch)
    );

    public static void SetWatch(DependencyObject target, int value) =>
        target.SetValue(WatchProperty, value);

    public static int GetWatch(DependencyObject target) => (int)target.GetValue(WatchProperty);

    static void OnWatch(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element)
            return;

        Seen.Add("at-callback " + State(element));
        element.Loaded += (_, _) => Seen.Add("at-loaded " + State(element));
    }

    static string State(FrameworkElement element) =>
        $"dc={element.DataContext?.GetType().Name ?? "<null>"} loaded={element.IsLoaded} "
        + $"findName(Peer)={(element.FindName("Peer") as FrameworkElement)?.GetType().Name ?? "<null>"}";
}

public static class SpikeHook
{
    public static readonly List<DependencyObject> Fired = new();

    public static readonly DependencyProperty SetupProperty = DependencyProperty.RegisterAttached(
        "Setup",
        typeof(int),
        typeof(SpikeHook),
        new PropertyMetadata(0, (d, e) => Fired.Add(d))
    );

    public static void SetSetup(DependencyObject target, int value) =>
        target.SetValue(SetupProperty, value);

    public static int GetSetup(DependencyObject target) => (int)target.GetValue(SetupProperty);
}
