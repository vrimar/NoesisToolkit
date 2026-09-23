namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>When a binding settles, releases and retries, for as long as its element lives.
/// A virtualizing panel unloads a container and loads it again later, so unload has to drop every
/// subscription without ending the binding; and an element can gain an ancestor without ever
/// raising Loaded, so an unresolved source retries off the layout pass that must follow.</summary>
sealed class ElementLifecycle : IChangeListener
{
    readonly ElementState _target;
    readonly IElementLifecycleOwner _owner;

    bool _armed;

    internal ElementLifecycle(ElementState target, IElementLifecycleOwner owner)
    {
        _target = target;
        _owner = owner;
        ElementEvents.WatchLifecycle(target, this);
    }

    /// <summary>Keeps the layout retry running while <paramref name="missing"/>, which the caller
    /// re-states every time it resolves.</summary>
    internal void Retry(bool missing)
    {
        if (missing == _armed)
            return;

        _armed = missing;
        if (missing)
            ElementEvents.WatchLayout(_target, this);
        else
            ElementEvents.UnwatchLayout(_target, this);
    }

    internal void OnLoaded() => _owner.Settle();

    internal void OnUnloaded()
    {
        _owner.Release();
        Retry(false);
    }

    internal void OnEnded()
    {
        _armed = false;
        _owner.Release();
    }

    void IChangeListener.Changed() => _owner.Retry();
}
