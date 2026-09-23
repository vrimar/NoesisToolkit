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

    /// <summary>A hop reading a bool, boxed once per value rather than on every read.</summary>
    /// <param name="name">The property name a change notification carries.</param>
    /// <param name="read">Reads this hop off the object the previous hop produced.</param>
    /// <returns>The hop.</returns>
    public static BindingHop Bool(string name, Func<object, bool> read)
    {
        Guard.NotNull(read, nameof(read));
        return new BindingHop(name, o => read(o) ? True : False);
    }

    /// <summary>The guarded form of <see cref="Bool"/>.</summary>
    /// <typeparam name="TOwner">The type the previous hop is expected to have produced.</typeparam>
    /// <param name="name">The property name a change notification carries.</param>
    /// <param name="read">Reads this hop off a source of the expected type.</param>
    /// <returns>The hop, which fails the path off anything else.</returns>
    public static BindingHop GuardedBool<TOwner>(string name, Func<TOwner, bool> read)
    {
        Guard.NotNull(read, nameof(read));
        return new BindingHop(name, o => o is TOwner typed ? (read(typed) ? True : False) : Missed);
    }

    /// <summary>A hop reading a value type, boxed again only when the value it reads has
    /// changed.</summary>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="name">The property name a change notification carries.</param>
    /// <param name="read">Reads this hop off the object the previous hop produced.</param>
    /// <returns>The hop.</returns>
    public static BindingHop Value<TValue>(string name, Func<object, TValue> read)
        where TValue : struct
    {
        Guard.NotNull(read, nameof(read));
        var box = new ValueBox<TValue>();
        return new BindingHop(name, o => box.Of(read(o)));
    }

    /// <summary>The guarded form of <see cref="Value{TValue}"/>.</summary>
    /// <typeparam name="TOwner">The type the previous hop is expected to have produced.</typeparam>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="name">The property name a change notification carries.</param>
    /// <param name="read">Reads this hop off a source of the expected type.</param>
    /// <returns>The hop, which fails the path off anything else.</returns>
    public static BindingHop GuardedValue<TOwner, TValue>(string name, Func<TOwner, TValue> read)
        where TValue : struct
    {
        Guard.NotNull(read, nameof(read));
        var box = new ValueBox<TValue>();
        return new BindingHop(name, o => o is TOwner typed ? box.Of(read(typed)) : Missed);
    }

    static readonly object True = true;
    static readonly object False = false;

    // A member that is null is a value the binding writes; a member that is not there at all fails
    // the binding and writes nothing, which is how the native engine tells the two apart.
    internal static readonly object Missed = new object();

    // One hop serves every clone of a template, so the boxes are kept per value, not per read;
    // past the cap a value boxes fresh rather than growing the map without bound.
    sealed class ValueBox<TValue>
        where TValue : struct
    {
        const int Cap = 256;

        // Without IEquatable a lookup boxes the key and reflects over its fields: dearer than the box.
        static readonly bool Keyable =
            typeof(TValue).IsEnum || typeof(IEquatable<TValue>).IsAssignableFrom(typeof(TValue));

        readonly Dictionary<TValue, object> _boxes = new Dictionary<TValue, object>();

        internal object Of(TValue value)
        {
            if (!Keyable)
                return value;

            if (_boxes.TryGetValue(value, out var boxed))
                return boxed;

            boxed = value;
            if (_boxes.Count < Cap)
                _boxes[value] = boxed;

            return boxed;
        }
    }
}

/// <summary>A binding the compiler resolved into a chain of typed reads, watched through
/// <see cref="INotifyPropertyChanged"/> and, where the path starts at one, a
/// <see cref="DependencyProperty"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledBinding
    : IChainOwner,
        INotifierOwner,
        IElementLifecycleOwner,
        IChangeListener
{
    readonly ElementState _target;
    readonly DependencyProperty _property;
    readonly CompiledBindingSpec _spec;
    readonly Func<FrameworkElement, DependencyObject?>? _receiverResolver;
    readonly NotifierSet _watched;

    readonly bool _twoWay;
    readonly bool _onLostFocus;

    static readonly object NotBroke = new object();

    static readonly nint LostFocus = BaseComponent.getCPtr(UIElement.LostFocusEvent).Handle;

    SourceChain _chain = null!;
    ElementState? _receiver;
    object? _writable;
    object? _unset;
    bool _clearWhenUnset;
    object? _brokeFor = NotBroke;
    ElementLifecycle _life = null!;
    bool _pushing;
    object? _written;
    ulong _writtenSlot;

    CompiledBinding(
        FrameworkElement target,
        DependencyProperty property,
        CompiledBindingSpec spec,
        Func<FrameworkElement, DependencyObject?>? receiverResolver = null
    )
    {
        _target = ElementState.Of(target);
        _watched = new NotifierSet(this);
        _chain = new SourceChain(
            _target,
            spec.Source,
            spec.Root,
            spec.SourceProperty,
            spec.Hops,
            _watched,
            this
        );
        _property = property;
        _spec = spec;
        _receiverResolver = receiverResolver;

        var metadata = property.GetMetadata(target.GetType()) as FrameworkPropertyMetadata;

        _twoWay =
            _receiverResolver is null
            && (spec.Lane?.WritesBack ?? spec.Write is not null)
            && (
                spec.Mode == BindingMode.TwoWay
                || (spec.Mode == BindingMode.Default && metadata?.BindsTwoWayByDefault == true)
            );

        var trigger =
            spec.Trigger == UpdateSourceTrigger.Default
                ? metadata?.DefaultUpdateSourceTrigger ?? UpdateSourceTrigger.PropertyChanged
                : spec.Trigger;
        _onLostFocus = trigger == UpdateSourceTrigger.LostFocus;

        _life = new ElementLifecycle(_target, this);

        if (_twoWay)
        {
            DependencyWatcher.Watch(_target, _property, this);
            if (_onLostFocus)
                ElementEvents.WatchRouted(_target, LostFocus, new LostFocusListener(this));
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

    /// <summary>The mark <see cref="MarkReceiver"/> leaves on an attached object. A local value, so
    /// a template's clone of the object carries its prototype's.</summary>
    public static readonly DependencyProperty ReceiverProperty =
        DependencyProperty.RegisterAttached(
            "Receiver",
            typeof(string),
            typeof(CompiledBinding),
            new PropertyMetadata(null)
        );

    /// <summary>Marks the attached object a compiled binding writes, so it is found among whatever
    /// else its element holds, including objects the element's own code added.</summary>
    /// <param name="receiver">The behavior, input binding or trigger action.</param>
    /// <param name="key">Identifies the object among those on the same element.</param>
    public static void MarkReceiver(DependencyObject receiver, string key)
    {
        Guard.NotNull(receiver, nameof(receiver));
        Guard.NotNull(key, nameof(key));
        receiver.SetValue(ReceiverProperty, key);
    }

    /// <summary>The behavior on <paramref name="element"/> marked <paramref name="key"/>, or null
    /// where none is attached yet.</summary>
    /// <param name="element">The element the behavior collection hangs off.</param>
    /// <param name="key">The mark the behavior carries.</param>
    /// <returns>The behavior, or null.</returns>
    public static DependencyObject? MarkedBehavior(FrameworkElement element, string key)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(key, nameof(key));

        var behaviors = Noesis.Interactivity.Interaction.GetBehaviors(element);
        for (var i = 0; behaviors is not null && i < behaviors.Count; i++)
        {
            if (Marked(behaviors[i], key))
                return behaviors[i];
        }

        return null;
    }

    /// <summary>The input binding on <paramref name="element"/> marked <paramref name="key"/>, or
    /// null where none is there yet.</summary>
    /// <param name="element">The element the input bindings hang off.</param>
    /// <param name="key">The mark the input binding carries.</param>
    /// <returns>The input binding, or null.</returns>
    public static DependencyObject? MarkedInputBinding(FrameworkElement element, string key)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(key, nameof(key));

        var bindings = element.InputBindings;
        for (var i = 0; bindings is not null && i < bindings.Count; i++)
        {
            if (Marked(bindings[i], key))
                return bindings[i];
        }

        return null;
    }

    /// <summary>The trigger action marked <paramref name="key"/> in any trigger on
    /// <paramref name="element"/>, or null where none is there yet.</summary>
    /// <param name="element">The element the trigger collection hangs off.</param>
    /// <param name="key">The mark the action carries.</param>
    /// <returns>The action, or null.</returns>
    public static DependencyObject? MarkedAction(FrameworkElement element, string key)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(key, nameof(key));

        var triggers = Noesis.Interactivity.Interaction.GetTriggers(element);
        for (var t = 0; triggers is not null && t < triggers.Count; t++)
        {
            var actions = triggers[t].Actions;
            for (var a = 0; actions is not null && a < actions.Count; a++)
            {
                if (Marked(actions[a], key))
                    return actions[a];
            }
        }

        return null;
    }

    static bool Marked(DependencyObject? candidate, string key) =>
        candidate is not null
        && string.Equals(
            DependencyRead.Value(candidate, ReceiverProperty) as string,
            key,
            StringComparison.Ordinal
        );

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

    /// <summary>The nearest of <paramref name="element"/> and its visual ancestors the given type
    /// accepts, which is where a FindAncestor binding on an object attached to the element roots:
    /// that walk counts the element the object hangs off.</summary>
    /// <param name="element">Where the walk starts; it is itself the first candidate.</param>
    /// <param name="type">The ancestor type to match, subclasses included.</param>
    /// <returns>The element or ancestor, or null when neither matches.</returns>
    public static FrameworkElement? FindAncestorOrSelf(FrameworkElement element, Type type)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(type, nameof(type));

        return type.IsInstanceOfType(element) ? element : FindAncestor(element, type);
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
    void IElementLifecycleOwner.Release()
    {
        _watched.Clear();
        _chain.Detach();
    }

    // A recycled container can end up under a different source, so this settles again each load.
    void IElementLifecycleOwner.Settle()
    {
        Resolve();
        Rebuild();
    }

    void IChainOwner.ChainChanged() => Rebuild();

    void IChangeListener.Changed() => OnTargetChanged();

    void Resolve()
    {
        var receiver =
            _receiverResolver is null ? _target
            : _target.Element is { } anchor && _receiverResolver(anchor) is { } found
                ? ElementState.Of(found)
            : null;
        if (!ReferenceEquals(receiver, _receiver))
        {
            _receiver = receiver;

            // The binding occupies the slot either way, so what a failing one shows is the default
            // in force for this receiver -- the one a subclass overrode the property to.
            (_unset, _clearWhenUnset) = receiver?.Object is { } resolved
                ? SlotDefault.For(resolved, _property)
                : (null, false);
        }

        _chain.Resolve();
        _life.Retry(Unresolved());
    }

    // The receiver is resolved separately from the source, so either being absent keeps the retry
    // running -- an element can gain its ancestor, its behaviors or its input bindings without ever
    // raising Loaded.
    bool Unresolved() =>
        _chain.Missing || (_receiverResolver is not null && _receiver is not { Alive: true });

    void IElementLifecycleOwner.Retry()
    {
        var source = _chain.Source;
        var receiver = _receiver;
        Resolve();
        if (!ReferenceEquals(_chain.Source, source) || !ReferenceEquals(_receiver, receiver))
            Rebuild();
    }

    void Rebuild()
    {
        if (_pushing || !_target.Alive)
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
                if (_receiver is { Alive: true } cleared)
                    DependencyWrite.Clear(cleared.Handle, _property);

                Settle();
            }
            finally
            {
                _pushing = false;
            }

            return;
        }

        object? current = null;
        ulong slot = 0;
        object? root;
        bool broke;
        if (_spec.Lane is { } lane)
        {
            var owner = _chain.EvaluateOwner(out root, out broke);
            if (!broke && !lane.TryRead(owner!, out slot))
            {
                owner = null;
                broke = true;
            }

            _writable = owner;
        }
        else
        {
            current = _chain.Evaluate(out root, out var owner, out broke);
            _writable = owner;
        }

        if (_receiver is not { Alive: true } receiver)
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
                if (_spec.Lane is { } typed)
                    typed.Write(receiver.Handle, _property, slot);
                else
                    WriteTarget(receiver, Forward(current));

                _brokeFor = NotBroke;
            }
            else if (!ReferenceEquals(root, _brokeFor))
            {
                WriteDefault(receiver);
                _brokeFor = root;
            }

            Settle();
        }
        finally
        {
            _pushing = false;
        }
    }

    void WriteTarget(ElementState receiver, object? produced)
    {
        var value =
            ReferenceEquals(produced, DependencyProperty.UnsetValue)
            || ReferenceEquals(produced, Binding.DoNothing)
                ? produced
                : _spec.Convert(produced);

        if (ReferenceEquals(value, Binding.DoNothing))
            return;

        if (ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            WriteDefault(receiver);
            return;
        }

        if (!ReferenceEquals(value, SlotConversion.Unconverted))
        {
            Assign(receiver, value);
            return;
        }

        // Off a view the engine converts on attach, outside _pushing; the load rebuilds instead.
        if (
            produced is not null
            && _target.Element is { IsLoaded: true }
            && receiver.Object is { } resolved
            && !SlotConversion.Convert(resolved, _property, produced)
        )
            WriteDefault(receiver);
    }

    void WriteDefault(ElementState receiver)
    {
        if (_clearWhenUnset)
            DependencyWrite.Clear(receiver.Handle, _property);
        else
            Assign(receiver, _unset);
    }

    void Assign(ElementState receiver, object? value)
    {
        if (_spec.Assign is not null && receiver.Element is { } element)
            _spec.Assign(element, value);
        else
            DependencyWrite.Value(receiver.Handle, _property, value);
    }

    void INotifierOwner.SourceChanged(object? sender, PropertyChangedEventArgs e)
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

    // What the binding itself left in the target is not an edit, however late the change reports.
    void Settle()
    {
        if (!_twoWay || !_target.Alive)
            return;

        if (_spec.Lane is { } lane)
            _writtenSlot = lane.Read(_target.Handle, _property);
        else
            _written = DependencyRead.Value(_target.Handle, _property);
    }

    void OnTargetChanged()
    {
        if (_onLostFocus || !_target.Alive)
            return;

        var edited = _spec.Lane is { } lane
            ? !lane.Same(lane.Read(_target.Handle, _property), _writtenSlot)
            : !Equals(DependencyRead.Value(_target.Handle, _property), _written);
        if (edited)
            Push();
    }

    void Push()
    {
        if (_pushing || _writable is null || !_target.Alive)
            return;

        if (_spec.Lane is { } lane)
        {
            if (lane.WritesBack)
                PushTyped(lane, _target.Handle, _writable);

            return;
        }

        if (_spec.Write is null)
            return;

        var value = DependencyRead.Value(_target.Handle, _property);
        if (_spec.Converter is not null)
            value = _spec.Converter.ConvertBack(
                value,
                _spec.TargetType ?? typeof(object),
                _spec.ConverterParameter,
                CultureInfo.CurrentCulture
            );

        if (
            ReferenceEquals(value, Binding.DoNothing)
            || ReferenceEquals(value, DependencyProperty.UnsetValue)
        )
            return;

        // The source raising a change would otherwise walk straight back into the target.
        _pushing = true;
        try
        {
            _spec.Write(_writable, value);
        }
        finally
        {
            _pushing = false;

            // Natively the source is read back after every write, even one that refused or threw.
            Rebuild();
        }
    }

    void PushTyped(BindingLane lane, nint target, object writable)
    {
        var slot = lane.Read(target, _property);

        _pushing = true;
        try
        {
            lane.WriteBack(writable, slot);
        }
        finally
        {
            _pushing = false;
            Rebuild();
        }
    }

    sealed class LostFocusListener(CompiledBinding owner) : IChangeListener
    {
        public void Changed() => owner.Push();
    }
}
