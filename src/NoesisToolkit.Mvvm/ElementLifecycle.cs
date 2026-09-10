using System;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>When a binding settles, releases and retries, for as long as its element lives.
/// A virtualizing panel unloads a container and loads it again later, so unload has to drop every
/// subscription without ending the binding; and an element can gain an ancestor without ever
/// raising Loaded, so an unresolved source retries off the layout pass that must follow.</summary>
sealed class ElementLifecycle
{
    readonly FrameworkElement _target;
    readonly Action _settle;
    readonly Action _release;
    readonly Action _retry;

    bool _armed;

    internal ElementLifecycle(FrameworkElement target, Action settle, Action release, Action retry)
    {
        _target = target;
        _settle = settle;
        _release = release;
        _retry = retry;

        _target.Unloaded += OnUnloaded;
        _target.Loaded += OnSettle;
        _target.Reloaded += OnSettle;
    }

    /// <summary>Keeps the layout retry running while <paramref name="missing"/>, which the caller
    /// re-states every time it resolves.</summary>
    internal void Retry(bool missing)
    {
        if (missing == _armed)
            return;

        _armed = missing;
        if (missing)
            _target.LayoutUpdated += OnLayoutUpdated;
        else
            _target.LayoutUpdated -= OnLayoutUpdated;
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _release();
        Retry(false);
    }

    void OnSettle(object sender, RoutedEventArgs e) => _settle();

    void OnLayoutUpdated(object sender, Noesis.EventArgs e) => _retry();
}
