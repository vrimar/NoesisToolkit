using System;
using System.Collections.Generic;
using System.ComponentModel;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The observation half of a compiled binding with more than one source chain: it holds
/// the chains, keeps them subscribed for as long as the element lives, and reports once whenever
/// any of them moves.</summary>
sealed class PartSet
{
    readonly SourceChain[] _chains;
    readonly NotifierSet _watched;
    readonly Action _changed;
    readonly ElementLifecycle _life;
    readonly Func<bool>? _alsoMissing;

    internal PartSet(
        FrameworkElement target,
        CompiledBindingPart[] parts,
        Action changed,
        Func<bool>? alsoMissing = null
    )
    {
        _alsoMissing = alsoMissing;
        _watched = new NotifierSet(OnSourceChanged);
        _changed = changed;
        _chains = new SourceChain[parts.Length];

        for (var i = 0; i < parts.Length; i++)
            _chains[i] = new SourceChain(
                target,
                parts[i].Source,
                parts[i].SourceProperty,
                parts[i].Hops,
                _watched,
                changed
            );

        _life = new ElementLifecycle(target, Start, Release, OnLayoutUpdated);
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

        _life.Retry(missing || (_alsoMissing is not null && _alsoMissing()));
        return moved;
    }

    // A source that outlives the element would pin it.
    void Release()
    {
        Unwatch();

        foreach (var chain in _chains)
            chain.Detach();
    }

    /// <summary>Resolves every source and reports the result once, which every consumer needs after
    /// it has finished constructing.</summary>
    internal void Start()
    {
        Resolve();
        _changed();
    }

    void OnLayoutUpdated()
    {
        if (Resolve())
            _changed();
    }

    void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name is the INotifyPropertyChanged signal for "everything changed".
        if (string.IsNullOrEmpty(e.PropertyName) || Watches(e.PropertyName))
            _changed();
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

    /// <summary>The value one chain currently reads. A path that ran out is
    /// <c>DependencyProperty.UnsetValue</c>, not null: the native engine hands converters the
    /// same.</summary>
    internal object? Evaluate(int index)
    {
        var value = _chains[index].Evaluate(out _, out _, out var broke);
        return broke ? DependencyProperty.UnsetValue : value;
    }

    internal void Unwatch() => _watched.Clear();
}
