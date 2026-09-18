using System.ComponentModel;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The observation half of a compiled binding with more than one source chain: it holds
/// the chains, keeps them subscribed for as long as the element lives, and reports once whenever
/// any of them moves.</summary>
sealed class PartSet : IChainOwner, INotifierOwner, IElementLifecycleOwner
{
    readonly SourceChain[] _chains;
    readonly NotifierSet _watched;
    readonly IPartSetOwner _owner;
    readonly ElementLifecycle _life;

    internal PartSet(FrameworkElement target, CompiledBindingPart[] parts, IPartSetOwner owner)
    {
        _owner = owner;
        _watched = new NotifierSet(this);
        _chains = new SourceChain[parts.Length];

        for (var i = 0; i < parts.Length; i++)
            _chains[i] = new SourceChain(
                target,
                parts[i].Source,
                parts[i].SourceProperty,
                parts[i].Hops,
                _watched,
                this
            );

        _life = new ElementLifecycle(target, this);
    }

    internal int Count => _chains.Length;

    /// <summary>Re-resolves every chain's source. True where one of them moved.</summary>
    internal bool Resolve()
    {
        var moved = false;
        var missing = false;

        foreach (var chain in _chains)
        {
            moved |= chain.Resolve();
            missing |= chain.Missing;
        }

        _life.Retry(missing || _owner.StillMissing);
        return moved;
    }

    /// <summary>Resolves every source and reports the result once, which every consumer needs after
    /// it has finished constructing.</summary>
    internal void Start()
    {
        Resolve();
        _owner.PartsChanged();
    }

    /// <summary>The value one chain currently reads. A path that ran out is
    /// <c>DependencyProperty.UnsetValue</c>, not null: the native engine hands converters the
    /// same.</summary>
    internal object? Evaluate(int index)
    {
        var value = _chains[index].Evaluate(out _, out _, out var broke);
        return broke ? DependencyProperty.UnsetValue : value;
    }

    internal void Unwatch() => _watched.Clear();

    void IChainOwner.ChainChanged() => _owner.PartsChanged();

    void INotifierOwner.SourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name is the INotifyPropertyChanged signal for "everything changed".
        if (string.IsNullOrEmpty(e.PropertyName) || Watches(e.PropertyName))
            _owner.PartsChanged();
    }

    void IElementLifecycleOwner.Settle() => Start();

    // A source that outlives the element would pin it.
    void IElementLifecycleOwner.Release()
    {
        Unwatch();

        foreach (var chain in _chains)
            chain.Detach();
    }

    void IElementLifecycleOwner.Retry()
    {
        if (Resolve())
            _owner.PartsChanged();
    }

    bool Watches(string name)
    {
        foreach (var chain in _chains)
        {
            if (chain.Watches(name))
                return true;
        }

        return false;
    }
}
