using System;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>One path from an element to a value: where it roots, what it subscribes to, and what
/// the hops read. A single binding holds one of these and a MultiBinding or trigger set holds
/// several, so the walk and its subscriptions are stated once.</summary>
sealed class SourceChain : IChangeListener
{
    readonly FrameworkElement _target;
    readonly Func<FrameworkElement, FrameworkElement?>? _resolve;
    readonly DependencyProperty? _sourceProperty;
    readonly BindingHop[] _hops;
    readonly NotifierSet _watched;
    readonly IChainOwner _owner;

    FrameworkElement? _source;

    internal SourceChain(
        FrameworkElement target,
        Func<FrameworkElement, FrameworkElement?>? resolve,
        DependencyProperty? sourceProperty,
        BindingHop[] hops,
        NotifierSet watched,
        IChainOwner owner
    )
    {
        _target = target;
        _resolve = resolve;
        _sourceProperty = sourceProperty;
        _hops = hops;
        _watched = watched;
        _owner = owner;
    }

    internal FrameworkElement? Source => _source;

    /// <summary>A stated source the walk has not found yet, which is what the layout retry is for.</summary>
    internal bool Missing => _resolve is not null && _source is null;

    /// <summary>Re-resolves the root and re-subscribes. True where the root moved.</summary>
    internal bool Resolve() => Attach(_resolve is null ? _target : _resolve(_target));

    internal void Detach() => Attach(null);

    internal bool Watches(string name)
    {
        foreach (var hop in _hops)
        {
            if (string.Equals(hop.Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>The value this chain reads. <paramref name="root"/> is what the walk started from,
    /// <paramref name="owner"/> is the object the last hop
    /// read off, which is what a write back applies to; <paramref name="broke"/> is a path that ran
    /// out, which is not the same as a value that is genuinely null.</summary>
    internal object? Evaluate(out object? root, out object? owner, out bool broke) =>
        Walk(_hops.Length, out root, out owner, out broke);

    internal object? EvaluateOwner(out object? root, out bool broke)
    {
        var owner = Walk(_hops.Length - 1, out root, out _, out broke);
        if (owner is null)
            broke = true;

        return owner;
    }

    // Compared natively: a text box's placeholder trigger would otherwise decode its text per keystroke.
    internal bool? TextEquals(string text) =>
        _hops.Length == 0 && _source is not null && _sourceProperty is not null
            ? DependencyRead.TextEquals(_source, _sourceProperty, text)
            : null;

    object? Walk(int hops, out object? root, out object? owner, out bool broke)
    {
        owner = null;

        var current =
            _source is null ? null
            : _sourceProperty is null ? _source.DataContext
            : DependencyRead.Value(_source, _sourceProperty);

        root = current;

        // A null root is a value the binding writes; only a hop with nothing to read off breaks it.
        broke = _source is null;

        for (var i = 0; i < hops; i++)
        {
            if (current is null)
            {
                owner = null;
                broke = true;
                break;
            }

            _watched.Watch(current);
            owner = current;
            current = _hops[i].Read(current);

            if (ReferenceEquals(current, BindingHop.Missed))
            {
                owner = null;
                current = null;
                broke = true;
                break;
            }
        }

        if (current is not null)
            _watched.Watch(current);

        return current;
    }

    bool Attach(FrameworkElement? source)
    {
        if (ReferenceEquals(source, _source))
            return false;

        if (_source is not null)
        {
            if (_sourceProperty is null)
                ElementEvents.UnwatchDataContext(_source, this);
            else
                DependencyWatcher.Unwatch(_source, _sourceProperty, this);
        }

        _source = source;

        if (_source is not null)
        {
            if (_sourceProperty is null)
                ElementEvents.WatchDataContext(_source, this);
            else
                DependencyWatcher.Watch(_source, _sourceProperty, this);
        }

        return true;
    }

    void IChangeListener.Changed() => _owner.ChainChanged();
}
