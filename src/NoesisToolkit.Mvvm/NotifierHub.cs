using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>One PropertyChanged subscription per notifier for every chain that reads it, fanned
/// out here. A notifier bound anywhere already has Noesis' own subscriber, so a subscription taken
/// and dropped per chain rebuilds the event's invocation list every time a binding re-evaluates.</summary>
static class NotifierHub
{
    static readonly ConditionalWeakTable<INotifyPropertyChanged, Fanout> Table =
        new ConditionalWeakTable<INotifyPropertyChanged, Fanout>();

    internal static void Watch(INotifyPropertyChanged notifier, INotifierOwner owner)
    {
        if (!Table.TryGetValue(notifier, out var fanout))
        {
            fanout = new Fanout();
            Table.Add(notifier, fanout);
            notifier.PropertyChanged += fanout.OnChanged;
        }

        fanout.Owners.Add(owner);
    }

    internal static void Unwatch(INotifyPropertyChanged notifier, INotifierOwner owner)
    {
        if (Table.TryGetValue(notifier, out var fanout))
            fanout.Owners.Remove(owner);
    }

    sealed class Fanout
    {
        internal readonly HandlerList<INotifierOwner> Owners = new HandlerList<INotifierOwner>();

        internal void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            using var run = Owners.Start();
            while (run.Next(out var owner))
                owner.SourceChanged(sender, e);
        }
    }
}
