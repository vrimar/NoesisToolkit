using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>One step of a compiled binding path.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct BindingHop
{
    /// <summary>Creates a hop.</summary>
    /// <param name="name">The property name a change notification carries.</param>
    /// <param name="read">Reads this hop off the object the previous hop produced.</param>
    public BindingHop(string name, Func<object, object?> read)
    {
        Name = name;
        Read = read;
    }

    /// <summary>The property name a change notification carries for this hop.</summary>
    public string Name { get; }

    /// <summary>Reads this hop off the object the previous hop produced.</summary>
    public Func<object, object?> Read { get; }

    /// <summary>A hop that reads only when the object really is <typeparamref name="TOwner"/>. The
    /// path off an element the compiler picked out of the tree, rather than one the document
    /// declared a type for, has to survive landing somewhere else.</summary>
    /// <typeparam name="TOwner">The type the previous hop is expected to have produced.</typeparam>
    /// <param name="name">The property name a change notification carries.</param>
    /// <param name="read">Reads this hop off a source of the expected type.</param>
    /// <returns>The hop, which fails the path off anything else.</returns>
    public static BindingHop Guarded<TOwner>(string name, Func<TOwner, object?> read)
    {
        Guard.NotNull(read, nameof(read));
        return new BindingHop(name, o => o is TOwner typed ? read(typed) : Missed);
    }

    // A member that is null is a value the binding writes; a member that is not there at all fails
    // the binding and writes nothing, which is how the native engine tells the two apart.
    internal static readonly object Missed = new object();
}

/// <summary>A binding the compiler resolved into a chain of typed reads, watched through
/// <see cref="INotifyPropertyChanged"/> and, where the path starts at one, a
/// <see cref="DependencyProperty"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledBinding
{
    readonly FrameworkElement _target;
    readonly DependencyProperty _property;
    readonly CompiledBindingSpec _spec;
    readonly Func<FrameworkElement, DependencyObject?>? _receiverResolver;
    readonly NotifierSet _watched;

    readonly bool _twoWay;
    readonly bool _onLostFocus;

    static readonly object NotBroke = new object();

    SourceChain _chain = null!;
    DependencyObject? _receiver;
    object? _writable;
    object? _unset;
    bool _clearWhenUnset;
    object? _brokeFor = NotBroke;
    ElementLifecycle _life = null!;
    bool _pushing;

    CompiledBinding(
        FrameworkElement target,
        DependencyProperty property,
        CompiledBindingSpec spec,
        Func<FrameworkElement, DependencyObject?>? receiverResolver = null
    )
    {
        _watched = new NotifierSet(OnSourceChanged);
        _chain = new SourceChain(
            target,
            spec.Source,
            spec.SourceProperty,
            spec.Hops,
            _watched,
            Rebuild
        );
        _target = target;
        _property = property;
        _spec = spec;
        _receiverResolver = receiverResolver;

        var metadata = property.GetMetadata(target.GetType()) as FrameworkPropertyMetadata;

        _twoWay =
            _receiverResolver is null
            && spec.Write is not null
            && (
                spec.Mode == BindingMode.TwoWay
                || (spec.Mode == BindingMode.Default && metadata?.BindsTwoWayByDefault == true)
            );

        var trigger =
            spec.Trigger == UpdateSourceTrigger.Default
                ? metadata?.DefaultUpdateSourceTrigger ?? UpdateSourceTrigger.PropertyChanged
                : spec.Trigger;
        _onLostFocus = trigger == UpdateSourceTrigger.LostFocus;

        _life = new ElementLifecycle(target, OnSettle, Release, OnLayoutUpdated);

        if (_twoWay)
        {
            DependencyWatcher.Watch(_target, _property, OnTargetChanged);
            if (_onLostFocus)
                _target.LostFocus += OnTargetLostFocus;
        }

        Resolve();
        Rebuild();
    }

    /// <summary>Binds <paramref name="property"/> on <paramref name="target"/> as
    /// <paramref name="spec"/> describes.</summary>
    /// <param name="target">The element to write.</param>
    /// <param name="property">The dependency property to write.</param>
    /// <param name="spec">What the compiler resolved about the binding.</param>
    /// <returns>The live binding, which follows the element for as long as it lives.</returns>
    public static CompiledBinding Bind(
        FrameworkElement target,
        DependencyProperty property,
        CompiledBindingSpec spec
    )
    {
        Guard.NotNull(target, nameof(target));
        Guard.NotNull(property, nameof(property));
        Guard.NotNull(spec, nameof(spec));

        return new CompiledBinding(target, property, spec);
    }

    /// <summary>Binds <paramref name="property"/> on the object <paramref name="receiver"/> finds
    /// off <paramref name="anchor"/> — a behavior, trigger action, or other attached object with no
    /// lifetime of its own. The anchor supplies the DataContext, the load events and the name
    /// scope; the receiver only takes the writes, one-way.</summary>
    /// <param name="anchor">The element the attached object hangs off.</param>
    /// <param name="receiver">Finds the object to write; null skips the write.</param>
    /// <param name="property">The dependency property to write.</param>
    /// <param name="spec">What the compiler resolved about the binding.</param>
    /// <returns>The live binding, which follows the element for as long as it lives.</returns>
    public static CompiledBinding Bind(
        FrameworkElement anchor,
        Func<FrameworkElement, DependencyObject?> receiver,
        DependencyProperty property,
        CompiledBindingSpec spec
    )
    {
        Guard.NotNull(anchor, nameof(anchor));
        Guard.NotNull(receiver, nameof(receiver));
        Guard.NotNull(property, nameof(property));
        Guard.NotNull(spec, nameof(spec));

        return new CompiledBinding(anchor, property, spec, receiver);
    }

    /// <summary>The behavior at <paramref name="index"/> on <paramref name="element"/>, or null
    /// where none is attached yet.</summary>
    /// <param name="element">The element the behavior collection hangs off.</param>
    /// <param name="index">The behavior's document position.</param>
    /// <returns>The behavior, or null.</returns>
    public static DependencyObject? BehaviorAt(FrameworkElement element, int index)
    {
        Guard.NotNull(element, nameof(element));
        var behaviors = Noesis.Interactivity.Interaction.GetBehaviors(element);
        return behaviors is not null && index < behaviors.Count ? behaviors[index] : null;
    }

    /// <summary>The input binding at <paramref name="index"/> on <paramref name="element"/>, or
    /// null where the collection does not reach that far.</summary>
    /// <param name="element">The element the input bindings hang off.</param>
    /// <param name="index">The input binding's document position.</param>
    /// <returns>The input binding, or null.</returns>
    public static DependencyObject? InputBindingAt(FrameworkElement element, int index)
    {
        Guard.NotNull(element, nameof(element));
        var bindings = element.InputBindings;
        return bindings is not null && index < bindings.Count ? bindings[index] : null;
    }

    /// <summary>The trigger action at <paramref name="action"/> inside the trigger at
    /// <paramref name="trigger"/> on <paramref name="element"/>, or null where the collections do
    /// not reach that far.</summary>
    /// <param name="element">The element the trigger collection hangs off.</param>
    /// <param name="trigger">The trigger's document position.</param>
    /// <param name="action">The action's document position inside the trigger.</param>
    /// <returns>The action, or null.</returns>
    public static DependencyObject? ActionAt(FrameworkElement element, int trigger, int action)
    {
        Guard.NotNull(element, nameof(element));
        var triggers = Noesis.Interactivity.Interaction.GetTriggers(element);
        if (triggers is null || trigger >= triggers.Count)
            return null;

        var actions = triggers[trigger].Actions;
        return actions is not null && action < actions.Count ? actions[action] : null;
    }

    /// <summary>The nearest visual ancestor of <paramref name="element"/> the given type accepts,
    /// which is where a FindAncestor binding roots. The logical parent chain stops at a template's
    /// own root, so only the visual one reaches out of template content.</summary>
    /// <param name="element">Where the walk starts; it is not itself a candidate.</param>
    /// <param name="type">The ancestor type to match, subclasses included.</param>
    /// <returns>The ancestor, or null when the tree holds none.</returns>
    public static FrameworkElement? FindAncestor(FrameworkElement element, Type type)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(type, nameof(type));

        for (
            var current = VisualTreeHelper.GetParent(element);
            current is not null;
            current = VisualTreeHelper.GetParent(current)
        )
        {
            if (current is FrameworkElement found && type.IsInstanceOfType(found))
                return found;
        }

        return null;
    }

    /// <summary>The element registered under <paramref name="name"/> in the nearest name scope out
    /// from <paramref name="element"/>. A template clone gets a name scope of its own, so a name the
    /// document declared outside the template is only reachable by walking past it.</summary>
    /// <param name="element">Where the walk starts; its own scope answers first.</param>
    /// <param name="name">The name to resolve.</param>
    /// <returns>The element, or null where no scope on the way up holds the name.</returns>
    public static FrameworkElement? FindNamed(FrameworkElement element, string name)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(name, nameof(name));

        for (var current = element; current is not null; current = Up(current))
        {
            if (current.FindName(name) is FrameworkElement found)
                return found;
        }

        return null;
    }

    // Popup content, template content and ordinary children each hang off the tree by a different
    // link, and only one of the three is set on any given element.
    static FrameworkElement? Up(FrameworkElement element) =>
        element.Parent
        ?? element.TemplatedParent as FrameworkElement
        ?? VisualTreeHelper.GetParent(element) as FrameworkElement;

    /// <summary>The control a template was applied to, which is where a <c>TemplatedParent</c>
    /// binding roots. Null outside template content, and on a template prototype.</summary>
    /// <param name="element">An element inside the applied template.</param>
    /// <returns>The templated parent, or null where the element is not template content.</returns>
    public static FrameworkElement? TemplatedParent(FrameworkElement element)
    {
        Guard.NotNull(element, nameof(element));
        return element.TemplatedParent as FrameworkElement;
    }

    // A source that outlives the container would pin it. This does not end the binding.
    void Release()
    {
        _watched.Clear();
        _chain.Detach();
    }

    void OnSettle()
    {
        // A recycled container can end up under a different source, so this settles again each load.
        Resolve();
        Rebuild();
    }

    void Resolve()
    {
        var receiver = _receiverResolver is null ? _target : _receiverResolver(_target);
        if (!ReferenceEquals(receiver, _receiver))
        {
            _receiver = receiver;

            // The binding occupies the slot either way, so what a failing one shows is the default
            // in force for this receiver -- the one a subclass overrode the property to.
            (_unset, _clearWhenUnset) = receiver is null
                ? (null, false)
                : SlotDefault.For(receiver, _property);
        }

        _chain.Resolve();
        _life.Retry(Unresolved());
    }

    // The receiver is resolved separately from the source, so either being absent keeps the retry
    // running -- an element can gain its ancestor, its behaviors or its input bindings without ever
    // raising Loaded.
    bool Unresolved() => _chain.Missing || (_receiverResolver is not null && _receiver is null);

    void OnLayoutUpdated()
    {
        var source = _chain.Source;
        var receiver = _receiver;
        Resolve();
        if (!ReferenceEquals(_chain.Source, source) || !ReferenceEquals(_receiver, receiver))
            Rebuild();
    }

    void Rebuild()
    {
        if (_pushing)
            return;

        // The attached object is added after the binding is wired, so it may only turn up now.
        if (_receiver is null && _receiverResolver is not null)
            Resolve();

        _watched.Clear();

        // A source the resolver could not find is not a failed read: a native binding with nowhere
        // to look writes nothing at all, so the slot stays the style's to fill.
        if (_chain.Source is null)
        {
            _writable = null;
            _brokeFor = NotBroke;
            _pushing = true;
            try
            {
                _receiver?.ClearValue(_property);
            }
            finally
            {
                _pushing = false;
            }

            return;
        }

        var current = _chain.Evaluate(out var root, out var owner, out var broke);
        _writable = owner;

        if (_receiver is null)
            return;

        _pushing = true;
        try
        {
            // A path that ran out never produced a value. A failing native binding still occupies
            // the slot at local precedence, so the metadata default is what shows -- clearing would
            // hand the slot back to the style, and default(T) would disable an IsEnabled. But only
            // once per root: the native engine re-evaluates on a DataContext change and writes the
            // default again, while a load never re-evaluates it at all, so what the control itself
            // put in the slot meanwhile has to survive our own reload rebuilds.
            if (!broke)
            {
                Assign(_spec.Convert(Forward(current)));
                _brokeFor = NotBroke;
            }
            else if (!ReferenceEquals(root, _brokeFor))
            {
                if (_clearWhenUnset)
                    _receiver.ClearValue(_property);
                else
                    Assign(_unset);

                _brokeFor = root;
            }
        }
        finally
        {
            _pushing = false;
        }
    }

    void Assign(object? value)
    {
        if (_spec.Assign is not null && _receiver is FrameworkElement element)
            _spec.Assign(element, value);
        else
            _receiver?.SetValue(_property, value);
    }

    void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name is the INotifyPropertyChanged signal for "everything changed".
        if (string.IsNullOrEmpty(e.PropertyName) || _chain.Watches(e.PropertyName))
            Rebuild();
    }

    object? Forward(object? value) =>
        _spec.Converter is null
            ? value
            : _spec.Converter.Convert(
                value,
                _spec.TargetType ?? typeof(object),
                _spec.ConverterParameter,
                CultureInfo.CurrentCulture
            );

    void OnTargetChanged()
    {
        if (!_onLostFocus)
            Push();
    }

    void OnTargetLostFocus(object sender, RoutedEventArgs e) => Push();

    void Push()
    {
        if (_pushing || _spec.Write is null || _writable is null)
            return;

        var value = _target.GetValue(_property);
        if (_spec.Converter is not null)
            value = _spec.Converter.ConvertBack(
                value,
                _spec.TargetType ?? typeof(object),
                _spec.ConverterParameter,
                CultureInfo.CurrentCulture
            );

        // The source raising a change would otherwise walk straight back into the target.
        _pushing = true;
        try
        {
            _spec.Write(_writable, value);
        }
        finally
        {
            _pushing = false;
        }
    }
}
