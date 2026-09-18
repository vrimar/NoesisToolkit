using System.Collections.Generic;
using System.ComponentModel;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The INotifyPropertyChanged subscriptions a source chain holds, subscribed at most once
/// per notifier and dropped together.</summary>
sealed class NotifierSet(INotifierOwner owner)
{
    // Identity, not equality: two equal-but-distinct sources each need their own subscription.
    List<INotifyPropertyChanged>? _watched;

    bool Watching(INotifyPropertyChanged notifier)
    {
        if (_watched is null)
            return false;

        foreach (var watched in _watched)
        {
            if (ReferenceEquals(watched, notifier))
                return true;
        }

        return false;
    }

    public void Watch(object? source)
    {
        if (source is not INotifyPropertyChanged notifier || Watching(notifier))
            return;

        NotifierHub.Watch(notifier, owner);
        (_watched ??= new List<INotifyPropertyChanged>()).Add(notifier);
    }

    public void Clear()
    {
        if (_watched is null)
            return;

        foreach (var notifier in _watched)
            NotifierHub.Unwatch(notifier, owner);

        _watched.Clear();
    }
}
