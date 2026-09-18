using System.ComponentModel;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Told that what it watches has changed, in place of a delegate per subscription.</summary>
interface IChangeListener
{
    void Changed();
}

/// <summary>Owns a source chain and hears when the chain's root or a hop it watches moves.</summary>
interface IChainOwner
{
    void ChainChanged();
}

/// <summary>Owns a notifier set and takes the raw property change so it can match the name.</summary>
interface INotifierOwner
{
    void SourceChanged(object? sender, PropertyChangedEventArgs e);
}

/// <summary>Owns an element lifecycle and takes its three moments.</summary>
interface IElementLifecycleOwner
{
    void Settle();

    void Release();

    void Retry();
}

/// <summary>Owns a part set and hears its one combined change.</summary>
interface IPartSetOwner
{
    void PartsChanged();

    /// <summary>True while something beyond the chains is still unresolved, which keeps the
    /// layout retry running.</summary>
    bool StillMissing { get; }
}
