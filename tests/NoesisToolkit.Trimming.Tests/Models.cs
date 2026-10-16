using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Noesis;
using Noesis.Interactivity;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Trimming.Tests;

// Nothing here reads a property Noesis reads: only the compiler's roots keep those in the binary.
public sealed partial class PanelModel
{
    public Headline Headline { get; } = new("hop");

    public ILabelled Labelled { get; } = new Caption("through");

    public List<Row> Rows { get; } = [new("first"), new("second")];

    public string Title { get; } = "ancestor";

    public int Saves;

    [DelegateCommand]
    void Save() => Saves++;
}

public sealed class Headline(string text)
{
    public string Text { get; } = text;
}

public interface ILabelled
{
    string Label { get; }
}

public sealed class Caption(string label) : ILabelled
{
    public string Label { get; } = label;
}

public sealed class Row(string name)
{
    public string Name { get; } = name;
}

public sealed class Choice(string value)
{
    public string Value { get; } = value;
}

public sealed class Chooser : Control
{
    public IReadOnlyList<Choice> Choices { get; } = [new("male"), new("female")];
}

public sealed class Pinger : Control
{
    public event System.EventHandler? Pinged;

    public void Raise() => Pinged?.Invoke(this, System.EventArgs.Empty);
}

public sealed class OnEvent : TriggerBase<FrameworkElement>
{
    static readonly MethodInfo Handler = typeof(OnEvent).GetMethod(
        nameof(Count),
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    public static int Fired;

    public string EventName { get; set; } = "";

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "The XAML compiler roots the event EventName names; that is what this proves."
    )]
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject.GetType().GetEvent(EventName) is { EventHandlerType: { } type } info)
            info.AddEventHandler(AssociatedObject, Delegate.CreateDelegate(type, this, Handler));
    }

    void Count(object sender, System.EventArgs e) => Fired++;
}
