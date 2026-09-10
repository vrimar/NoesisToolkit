using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The INotifyPropertyChanged subscriptions a source chain holds, subscribed at most once
/// per notifier and dropped together.</summary>
sealed class NotifierSet(PropertyChangedEventHandler handler)
{
    // Identity, not equality: two equal-but-distinct sources each need their own subscription.
    readonly List<INotifyPropertyChanged> _watched = new List<INotifyPropertyChanged>();

    bool Watching(INotifyPropertyChanged notifier)
    {
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

        notifier.PropertyChanged += handler;
        _watched.Add(notifier);
    }

    public void Clear()
    {
        foreach (var notifier in _watched)
            notifier.PropertyChanged -= handler;

        _watched.Clear();
    }
}
