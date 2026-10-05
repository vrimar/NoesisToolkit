# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Added

- **`NTK1007` names each binding and trigger that falls back to the engine, and why**, at the
  attribute or element that holds it, along with every binding in markup handed to the parser
  whole. The survey counted fallbacks per file, so finding one meant reading the generated code,
  and it did not count parsed markup at all. It is hidden by default, since a binding native on
  purpose leaves nothing to fix; an audit raises it in a global analyzer config.

### Changed

- **Markup handed to the parser carries only the namespaces it names, each once, and no
  indentation.** Every fragment declared every namespace in scope where it sat, and each element a
  template rebuilt from its `{TemplateBinding}` attributes declared them again, so declarations were
  half of all fragment text, and the generated code pretty-printed what it embedded. A prefix listed
  in `mc:Ignorable` or `mc:MustUnderstand` counts as named. A whitespace run is written as the single
  space the parser reduces it to.

### Fixed

- **A binding on an element whose type the compiler cannot resolve is rooted.** The parser builds
  such an element whole, and its markup was skipped along with its type, so a trimmed build could
  drop what the binding reads without a word. It is now rooted, or reported as `NTK1004`, like any
  other binding the parser builds.
- **`NTK1004` is raised once for a binding held back from an element the parser builds**, at its
  attribute. A binding that names a resource is applied in code after the parse, and was reported
  there a second time, at the element.
- **A binding written in another binding's `FallbackValue`, `TargetNullValue`, `ConverterParameter`
  or `Source` has a reason of its own in the survey**, `binding-in-a-` and the knob's name, such as
  `binding-in-a-fallbackvalue`. It was counted as `unclassified`, or under the reason of the
  multi-binding around it.

## [0.3.7] - 2026-10-02

### Added

- **`NTK1005` names a document parsed at run time.** The compiler builds every other document in
  code, so an app can stop shipping its XAML; a document that wires a handler in markup still loads
  through `GUI.LoadComponent`, which reads its source. The diagnostic is information by default and
  sits on the handler attribute. An app that ships no XAML raises it to an error in a global analyzer
  config, so a handler added in markup fails the build rather than the screen.
- **`NTK1006` reports an `ntk:DataType`, `ntk:AncestorDataType` or `ntk:ItemType` that names no
  type**, at the attribute. A type moved to another namespace left its annotation resolving to
  nothing, and every binding under it was reported as `NTK1004`, undeclared, with no word about the
  annotation that was there. Those bindings are now left to the one report on the annotation.
- **`AttachedObjects.AssociatedObjectOf` reads a behavior's element during a teardown without an
  error.** Noesis detaches a behavior or trigger after it has let the element go, and
  `AssociatedObject` read in `OnDetaching` then logs "Extend already removed" and returns null. This
  returns null without the error, and the element itself everywhere else.

## [0.3.6] - 2026-10-02

### Added

- **A trimmed or NativeAOT build keeps what XAML reaches by name.** Noesis resolves a binding left
  native, markup handed to its parser, an `EventName` and a `DisplayMemberPath` by reflection at run
  time, so an app that trimmed had to root whole assemblies or watch a binding, a parser-built control
  or an event trigger fail without a word. The compiler roots each such member as a
  `[DynamicDependency]` on the method that builds its document, so it is kept exactly as long as the
  document is: each hop of a native binding on the type that declares it, the constructor and CLR
  properties of what the parser builds, the item type a `DisplayMemberPath` or a
  `GridViewColumn.DisplayMemberBinding` reads, the event an `EventName` names, the field that
  registers an attached property met by owner name on an owner that is not a `DependencyObject`,
  and the literals of an enum written as text.
- **The overrides Noesis looks up by reflection are kept.** Noesis calls a control's
  `MeasureOverride`, `ArrangeOverride`, `OnApplyTemplate`, `OnRender` and the rest, and any object's
  own `ToString` and `Equals`, only where reflection finds them declared. A trimmed build keeps an
  override's code but not its metadata, so the override was never called and nothing said so. The
  compiler roots them on every managed type a document references, from any assembly — its
  elements, the types it states, the hops and values of its bindings and the items they hold — and
  a generator in `NoesisToolkit.Mvvm` roots them on every type of an assembly that references it, on
  a module initializer, for a control built only from code. A record's compiler-written `ToString`
  and `Equals` count.
- **`NTK1004` marks a name the compiler cannot root**, because it cannot determine the type it is
  read off: a binding under an undeclared DataContext, a path through `object`, items of unknown
  type, an interface no class the compiler can see implements. State the type with `ntk:DataType`,
  `ntk:AncestorDataType` or `ntk:ItemType`. It is raised at the attribute holding the markup, and
  only where the trim analyzer runs, so a project nothing trims never sees it.
- **`ntk:ItemType` states the type of a list's rows**, on a `GridViewColumn` for its
  `DisplayMemberBinding` and on an items control for its `DisplayMemberPath` and
  `SelectedValuePath`, where `ItemsSource` is bound through a converter or set somewhere the
  compiler cannot see. It types the rows alone, so the column's own bindings, such as its `Header`,
  keep reading the list's DataContext.

### Fixed

- **A `HierarchicalDataTemplate` scopes its bindings to its own `DataType`**, in the compiler and in
  `NTK2001`, as a `DataTemplate` does. They were resolved against the type of the scope around it.
- **A compiled `FindAncestor` binding inside a `Popup`'s content finds the control around the
  popup**, as the native one does. The walk followed visual parents alone, and popup content hangs
  off the popup layer, so the binding found nothing, open or closed. Where an element has no visual
  parent that is an element, the walk now goes on from its logical parent.

## [0.3.5] - 2026-09-28

### Added

- **`Events.OnKeyClaim` lets a key handler mark the key handled.** `Events.OnKey` can only read the
  key, so a handler that acts on it could not stop what Noesis does next: a key that bubbles to the
  root unhandled still drives the view's own keyboard navigation, and an arrow moves focus between
  controls. `OnKeyClaim` takes a handler that returns whether it handled the key and, when it does,
  sets `Handled` on the native args, so a delivery still allocates nothing. It is its own name rather
  than an `OnKey` overload because C# would bind any bool-returning lambda to the new overload.

## [0.3.4] - 2026-09-27

### Added

- **A `[DependencyProperty]` change callback can take its object as an `ElementHandle`.** Noesis holds
  a native element's managed proxy weakly and mints a new one on the first touch after every
  collection, so a callback taking a `DependencyObject` paid a proxy for every native element it ran
  on after each collection: an attached property bound in a card template, say, once per card a list
  rebinds. Declared as `(ElementHandle element, DependencyPropertyChangedEventArgs e)`, the callback
  runs with the native handle and nothing is minted. `DependencyRead`, `DependencyWrite` and
  `Events.On`/`Events.Off` take the handle, an attached property's generated `Get{Name}` gains a
  handle overload, and `ElementHandle.Object` mints the proxy for the rare path that needs one.
  `DependencyWrite.Visibility` sets an element's visibility by handle through Noesis' own setter,
  since a property Noesis registered natively rejects an enum written as bits.

### Changed

- **A property registered without a change callback emits `DependencyWatcher.Metadata(default,
  options)`**, since a `null` callback no longer picks between the object and the handle overloads.
  Generated code needs this runtime or later. A hand-written `Metadata(value, options, null)` or an
  untyped lambda passed to it is ambiguous now; drop the `null`, or pass a method group or a typed
  delegate.

## [0.3.3] - 2026-09-24

### Added

- **`AllocationCost.Of` in `NoesisToolkit.Testing` measures what a piece of work allocates once
  warm.** It runs the body unmeasured first, so a JIT'd path, a proxy Noesis mints on first sight or
  a cache filling is not charged to steady state, then returns the managed bytes across the measured
  runs. Measure a floor the same way and compare, and a layout pass's own cost cancels out. The
  warmup and the measured runs share a no-GC region when the runtime grants one, since a collection
  between them lets Noesis drop what the warmup paid for and charges it again.

### Fixed

- **A row a list scrolls back to after a collection no longer mints its image again.** The image a
  compiled path-to-image binding converts into was held only while an element showed it, so a list
  that hands its rows to pooled or recycled containers lost the image of every row it scrolled past at
  the next collection, then minted a URI and an image for it and had Noesis resolve the texture again
  when the row came back. The 512 most recently used images are now held past their last element;
  one no element shows and nothing used lately is still let go.

## [0.3.2] - 2026-09-23

### Fixed

- **A browser build no longer traps on the first change to a property the toolkit registered.**
  `NotifyingMetadata` hands Noesis a managed trampoline as its native change callback, and a
  WebAssembly build generates the native entry for such a delegate only when its target carries
  `[MonoPInvokeCallback]`. Without it Noesis called a null function the first time the property
  changed. The trampoline now carries the attribute Noesis' own change callback does.

## [0.3.1] - 2026-09-23

### Fixed

- **A compiled binding, MultiBinding or trigger set no longer mints a proxy after a collection to
  read or write a native element.** It read its source and wrote its target through the element's
  managed proxy, which Noesis lets go at every collection, so the first update after one minted a
  proxy per element touched: a data-context binding on a `TextBlock`, a template trigger and its
  named setter target, a list row's trigger rooted at its `ListBoxItem` on every reload. Reads and
  writes now go through the native handle, dispatched on the property's type as Noesis' own
  `GetValue` and `SetValue` are, and a `TemplatedParent` or `Self` source is found off the handle.

### Changed

- **`CompiledBindingSpec` and `CompiledBindingPart` carry a `Root`**, where the runtime finds a
  source it can reach by handle. Generated code sets it in place of a `Source` resolver for
  `TemplatedParent` and `Self`, so it needs this runtime or later.

## [0.3.0] - 2026-09-23

### Fixed

- **A compiled binding, MultiBinding or trigger set lives as long as its element, not its proxy.**
  Noesis holds a native element's managed proxy weakly, and every compiled graph was reached only
  through tables keyed by that proxy, so the first collection after the element's last proxy went
  took the graph with it while the element lived on. A list row's hover trigger, a template trigger,
  a template binding over an item that raises no change, and any binding on an element unloaded and
  loaded again all stopped updating. Per-element state is now keyed by the native handle and ends
  when Noesis destroys the element; nothing in it holds a proxy, so it cannot pin what it watches.
- **A change on a native element no longer mints a proxy after a collection just to find nothing to
  tell.** Probes, generated properties with no callback and the element events the toolkit binds
  are dispatched by handle; a proxy is resolved only where a callback or a read needs one.
- **A subscription taken through `Events.On` or `DependencyWatcher.Watch` lasts as long as its
  element.** It lasted as long as the proxy it was taken on, so a handler on a native element — a
  tooltip's mouse enter, a scroll viewer's auto-hide — stopped firing at the first collection after
  the caller let the proxy go.
- **A two-way binding that writes back on `LostFocus` no longer pins its element.** It subscribed
  through Noesis' own handler store, which lets go only when the element is destroyed, while holding
  the element's proxy, so the element never was. It hears the event through the toolkit's native
  binding now.

### Changed

- **`DependencyWatcher.Watch` and `Unwatch` take an `Action<FrameworkElement>`**, handed the
  element that changed. A subscription now lives as long as the element, so a handler that captured
  it, or the control around it, would keep both alive; one that takes the element needs no capture.
- **Subscribing the same handler to the same event of an element again does nothing**, as watching
  the same property with the same handler already did.
- **A template clone's wiring index is read and written typed.** Past the first 256 wirings an
  app registers, every clone boxed its index on the way in.
- **`RenderDeviceTiles` fills its tile arrays for up to 16 tiles up front**, on `Reuse()` and on a
  render thread's first resolve. A pass's tile count follows what is on screen, so a count first
  seen mid-game allocated its array in that frame.

## [0.2.7] - 2026-09-22

### Added

- **A number shown as text rides a text lane.** Where the path ends in an integer, `float` or
  `double` and the slot is a string, bare or through a `StringFormat` the engine formats alike, the
  compiler emits `BindingLane.Text`: the value is formatted into a stack buffer by `SlotText` and
  handed to Noesis without a string. A counter or a slider readout moving through values it never
  showed before allocates nothing, where the cached conversion formatted each new value once and
  every value past its cap every time. A format with an aligned hole keeps the string route.
- **A struct that converts to and from a number through its own operators rides that number's
  lane.** A typed id bound to an `int`, `long`, `float` or `double` slot is cast through its
  explicit operators, both ways, instead of boxing into a Noesis extend on every write.
- **`DependencyWrite.String` writes a string property from a `ReadOnlySpan<char>`**, straight to
  the native setter, so text formatted into a buffer never becomes a managed string.
- **`SlotConversion.TryText` formats an `F`, `N` or `P` number into a caller's buffer**, rounded as
  the engine rounds.

### Changed

- **`HandlerArgs` pools are filled three deep up front.** A focus change nested in a click grew a
  pool mid-frame the first time it happened; now the pools are filled when `Reuse()` first runs.
- **`SlotConversion.Text(double, format)` and its `float` twin format on the stack.** The engine's
  rounding was emulated through a `StringBuilder` and several intermediate strings per value; only
  the result is allocated now.

## [0.2.6] - 2026-09-22

### Added

- **A compiled binding carries a flag or number typed, through a `BindingLane`.** Where the path ends
  in a bool, number or enum and the slot is a bool, int, long, float or double, with no converter or
  format between them, the compiler emits a lane: the last hop is read, converted and written to the
  slot, and a two-way edit read back and written to the source, all through Noesis' typed natives.
  The boxed route kept a box per value per hop, so a slider dragged through values it had never shown
  allocated every frame; a lane allocates nothing. An enum slot keeps the boxed route, since it is
  written through its own accessor.
- **`HandlerArgs.Reuse()` hands every managed event handler one reused args object** per args type
  and nesting depth. Noesis mints a finalizable args object for each handler it delivers to, which a
  pointer move, a key or a click pays once per subscribed element. The args are valid for the
  handler's duration only, as Noesis' own are.
- **`RenderDeviceTiles.Reuse()` hands `ResolveRenderTarget` a reused tile array.** Noesis' callback
  marshals its tiles into a fresh array on every offscreen pass, which every opacity mask, effect and
  group opacity takes.
- **`DependencyRead.Long` and `DependencyWrite.Value(long)`**, and the `[DependencyProperty]`
  generator reads and writes a `long` property through them.
- **`DependencyRead.TryCopyString` copies a string property's text into a caller's buffer** without
  decoding a string, for a reader that only parses or measures it.

### Changed

- **Text is decoded once per distinct text.** `DependencyRead.String`, `DependencyRead.Value` on a
  string property or on an object one holding a string, and a generated string accessor return the
  string already decoded for the same UTF-8, where Noesis decodes a fresh one per read. An object
  property holding a flag or number reads back a shared box.
- **A trigger comparing a string property to a constant compares the native UTF-8**, so a text box's
  placeholder trigger no longer decodes its text on every keystroke.
- **A compiled trigger set writes only what changed.** It tracks its setters as bits over a layout
  shared by every clone of the template, where it rebuilt two dictionaries on every pass.
- **A compiled conversion is kept per source value.** A number reaching a slot of another numeric
  type or a string, and a `StringFormat` over a number, convert through `SlotConversion.Cached`, which
  boxes or formats each value once per binding site rather than on every evaluation.
- **A path compiled into an `ImageSource` slot shares one image per path** through
  `ImageSources.From`, where every bind minted a URI and a `BitmapImage` and Noesis resolved the
  texture again.
- **A value hop keeps a box per value only for an enum or an `IEquatable<T>`.** Keying any other
  struct boxes the key and reflects over its fields, which costs more than the box it saves.
- **A converter's number reaching a float or double slot is written directly**, rather than through a
  one-time native binding.

## [0.2.5] - 2026-09-21

### Added

- **`ElementGeometry` reads a point or a size without allocating.** `Noesis.Point` and `Noesis.Size`
  declare a marshalling attribute on each of their floats, which stops the marshaller treating them
  as blittable, so the managed `TranslatePoint`, `DesiredSize` and `RenderSize` box one per call. A
  panel pays that per child on every layout pass, and one testing its children against the viewport
  pays it per child per frame; these read the same native result straight.
- **`Events` subscribes to an element's events without allocating.** `Events.On` and `Events.Off`
  take a routed event or one Noesis raises by name, such as `SizeChanged` or `IsVisibleChanged`, and
  `OnKey`/`OffKey` a key event. The handler gets the element, and the key, rather than the args
  object Noesis mints per delivery, through the same native binding the toolkit's own events use.

### Changed

- **`NoesisToolkit.Mvvm` and `NoesisToolkit.Testing` target `net10.0` only.** Reaching past Noesis'
  managed layer needs `UnsafeAccessorType`, and a runtime that cannot has no reason to take this
  release; the analyzer packages stay on `netstandard2.0`, as Roslyn requires.
- **An element's events are bound natively and deliver without allocating.** Loaded, Reloaded,
  Unloaded, DataContextChanged and LayoutUpdated are bound through the same exports Noesis' own
  handler store uses, to one unmanaged callback that finds the element's entry. Noesis' path minted
  a `RoutedEventArgs` per delivery and gave every subscribed element a store, a dictionary, a
  destroyed hook and a delegate per event; this one gives it nothing. A class handler was tried and
  rejected on the way: it makes Noesis call managed code for every element's Loaded and Unloaded,
  bound or not, which a recycling list pays on every hover.
- **A change callback gets one reused args object.** `DependencyWatcher.Metadata` registers metadata
  whose callback Noesis calls straight into the toolkit, which keeps one
  `DependencyPropertyChangedEventArgs` per nesting depth and points it at the native args for the
  call — Noesis' own trampoline minted a finalizable one per change. The `[DependencyProperty]`
  generator, the wiring index and the watcher's probes register through it. The args are valid for
  the callback's duration only: a handler that keeps them reads null afterwards.
- **A generated `[DependencyProperty]` accessor reads and writes without boxing.** A bool, int,
  float, double or enum reads through Noesis' typed natives, and those plus `Thickness`, `Color`,
  `Point`, `Size` and `CornerRadius` write through them; `GetValue` boxed every read and `SetValue`
  every write. A nullable property still goes through `GetValue` and `SetValue`.
- **A compiled binding to a value-type slot passes its source's box through** instead of unboxing
  and reboxing it on every update.
- **The wiring index reads through `DependencyRead`** rather than `e.NewValue`, which boxed the index
  on every container clone.
- **`ControlCollectability.Probe` settles its controls as one batch.** Every control is added and
  removed first, then each collect-and-pump round serves all of them still pending; a sweep paid a
  blocking collection pair per control per round, and now pays one per round.

## [0.2.4] - 2026-09-18

### Added

- **`NoesisToolkit.Testing`, a harness that proves controls are collectable.**
  `ControlCollectability.Probe` adds every constructible control in the assemblies you name to a live
  renderless view, removes it, collects, and reports the ones the heap still holds — the leaks
  `NTK2101` and `NTK2102` cannot see because markup wired them or a native property picked them up at
  runtime. `Unavailable()` says whether the environment can host the probe, so a headless machine
  skips rather than reporting a false pass. Run it with the theme installed: an untemplated control
  has no template children to subscribe to and no bindings to carry.
- **`NTK2102` refuses a command that captures the control exposing it.** Setting a native `Command`
  makes Noesis hold the managed command, which holds everything its delegates captured, so a control
  that binds such a command into its own template closes a cycle through native code and never dies.
  It covers a command the control stores itself and a `[DelegateCommand]` on one of its instance
  methods, reported at the attribute because the property it generates is excluded as generated code.

### Fixed

- **The equivalence suite no longer fails once every several runs.** Noesis records the thread that
  owns each object and refuses access from any other; `[NotInParallel]` serialises tests but still
  hands each one whichever pool thread is free, so a fixture built under one test and read under the
  next logged "a different thread owns it" into the warnings a parity assertion compares. Every test
  and hook now runs on one pump thread for the process, and a test fails if it ever does not.
- **`NTK2101` no longer reads remove-before-add as a teardown.** A `-=` the same flow re-attaches
  below guards against a second `OnApplyTemplate` stacking the handler; it never runs when the
  element goes away, so the subscription still pins the element for the process lifetime. Only a
  `-=` that some other path can reach now balances one, and the message names which of the two it
  found. Branches of one `if` stay exempt: those are alternatives, so neither re-attaches the other.

## [0.2.3] - 2026-09-17

### Fixed

- **A container a virtualizing panel recycles no longer leaks a handler per unload.** Noesis keeps
  a `DataContextChanged` or `LayoutUpdated` handler whose removal leaves another handler on the
  event, so a subscription taken and dropped per binding grew each element's invocation list by
  one copy per unload and rebuilt it on every subsequent add or remove. Each element now carries
  one subscription per event, fanned out to its bindings on the managed side and dropped only once
  nothing listens; a copy Noesis left behind is recognised and never joined by another.
- **A binding that re-evaluates allocates nothing of its own.** A notifier bound anywhere already
  carries Noesis' subscriber, so re-subscribing per chain rebuilt the event's invocation list on
  every re-evaluation; each notifier now carries one subscription for every chain that reads it.
  A trigger set keeps its two setter maps across evaluations, and a watched dependency property
  fires its handlers without copying them.
- **A compiled hop that reads a value type no longer boxes it on every evaluation.** A bool reads
  as one of two shared boxes, and any other value type keeps one box per value it has read, so a
  trigger re-evaluated on a recycled container allocates nothing for the conditions it reads.
- **A template clone no longer rebuilds its bindings' specifications.** The generated wiring built
  every `CompiledBindingSpec`, trigger spec, setter and resource lookup again for each clone; they
  are now built once beside the wiring and shared by every clone.
- **A bool or enum dependency property a chain reads no longer boxes on every read.** The read goes
  through Noesis' own typed getter and comes back as a shared box, so a property trigger or a
  binding rooted at such a property allocates nothing to re-evaluate; `DependencyRead` offers the
  same read to generated code.
- **A `[DependencyProperty]` of bool, int or enum type reads through the same shared boxes**, so
  code reading its own properties in a change callback allocates nothing; and an int shown as text
  formats each small value once.
- **Binding an element allocates far less.** A binding listens through interfaces rather than a
  delegate per event, one lifecycle subscription per element serves every binding on it, a
  notifier list and a trigger set's maps exist only once they hold something, and a dependency
  property once probed stays probed across unloads instead of binding a native expression again on
  each load.

## [0.2.2] - 2026-09-17

### Changed

- **`NTK2001` checks a polymorphic DataContext against every type that can stand behind it.** A path
  on an abstract type, an interface or a subclassed type used to go unchecked. When the type is
  declared in the compilation that owns the document, a member neither it nor any subtype there
  declares is now reported, and a member only a subtype declares still passes. A polymorphic type
  from another assembly stays unchecked, and a hop stops where a subtype redeclares the member with
  another type. Builds that treat warnings as errors fail on bindings this used to miss.

## [0.2.1] - 2026-09-16

### Fixed

- A `Content="{TemplateBinding}"` on a managed `ContentControl` inside a template keeps its content;
  it used to be cleared as the element was built, leaving the control empty.

## [0.2.0] - 2026-09-16

Compiled XAML now behaves as the native parser does wherever the two were found to disagree. Each
fix is pinned by an equivalence test that loads the same document both ways.

### Changed

- **`{TemplateBinding}` is never compiled.** The parser builds every element that carries one, once
  per template, and the compiler applies the rest. It stays one-way and converts nothing, as parsed
  XAML does; it used to compile to a `Binding` on the templated parent that wrote a two-way slot back
  to the control and converted values the parser leaves unset.
- **A keyed template or style needs `ntk:DataType`** for its bindings to compile or be checked. The
  sites one document names the key from no longer type it, because any document or code can apply
  the key. In an `ntk:CompileBindings` document those bindings now raise `NTK2004`.
- A `{DynamicResource}` on a `Binding` or `MultiBinding` knob is `NTK1002` and leaves the knob unset,
  as the parser does. Builds that treat warnings as errors fail on it.
- Two elements sharing an `x:Name`, and an image source the parser cannot resolve, are `NTK1001`.
- A `{TemplateBinding}` the parser cannot build where it is written — a markup extension's argument,
  say — is `NTK1002`.

### Fixed

- A binding whose root value is null writes null, not the slot's default.
- Text reaches a text slot as the engine writes it: numbers in its invariant form, whitespace between
  inlines collapsed as it collapses it, and only a `StringFormat` the compiler can reproduce exactly
  compiled.
- A converter's `DependencyProperty.UnsetValue` writes the slot's default, `Binding.DoNothing` leaves
  it alone, and a result of another type is converted by the engine.
- A two-way binding reads its source back after every write, so a setter that clamps, refuses or
  throws shows the source's value. A value the binding wrote itself is not written back, including
  the first value of an element built before it is shown.
- Literals, resources and construction follow the parser: a key is looked up where it is referenced,
  integers take the type the parser gives them, and a control that loads its own content no longer
  carries it into every clone of a compiled template.
- A trigger and a binding on the same property keep the parser's precedence, and a trigger set whose
  setters move its own conditions settles as the engine settles it.
- An attached handler such as `ButtonBase.Click="OnClick"` subscribes the owner's routed event.
- A compiled binding on a behavior, input binding or trigger action finds its receiver when the
  element's own code adds objects ahead of it.
- A binding left to the engine names an attached property's owner by CLR name, so
  `(prefix:Owner.Property)` resolves.
- A binding on `ContentPresenter.Content` inside a template stays native, so the presenter still
  adopts the templated parent's content and template.
- Watching a dependency property no longer overrides its metadata, which logged a warning the parsed
  document never raises. A `[DependencyProperty]` property reports its own changes; any other is
  watched through a bound probe.

### Known limitations

- A compiled `x:Class` placed in a template that XAML parsed at run time builds its content into the
  template's prototype. See
  [docs/xaml-compiler.md](https://github.com/vrimar/NoesisToolkit/blob/main/docs/xaml-compiler.md).
- Code that calls `SetValue` on a property with a compiled one-way binding does not replace the
  binding. See
  [docs/compiled-bindings.md](https://github.com/vrimar/NoesisToolkit/blob/main/docs/compiled-bindings.md).

## [0.1.2] - 2026-09-16

### Fixed

- A Native AOT application can publish against **NoesisToolkit.Mvvm**. 0.1.0 and 0.1.1 failed
  `IL2075` in the dependency watcher, which reflects over a Noesis property's owner type to tell an
  attached property from a plain one. That reflection only ever reaches types in the Noesis
  assembly, which an AOT Noesis application has to root whole anyway.

## [0.1.1] - 2026-09-16

### Added

- **NTK2005** — an enum-valued attribute naming a member the enum does not declare. Noesis parses
  these from their string at load time, so a member left behind by a rename reaches the lookup as a
  name nothing maps. Enums declared by Noesis itself are exempt, because its converters accept
  aliases they never declare.

### Changed

- A write back to a behavior, input binding or trigger action is now classed a boundary rather than
  a gap. The receiver is found by position each time and may be gone, so nothing stable can be
  watched to drive one.

## [0.1.0] - 2026-09-16

First release.

### Added

- **NoesisToolkit.XamlCompiler** — compiles XAML documents to C# that rebuilds the Noesis object graph
  without a runtime parse, and contributes `InitializeComponent`, typed `x:Name` accessors and
  `ConnectEvent` to an `x:Class` partial. Diagnostics `NTK0001`, `NTK1001`–`NTK1003`.
- **NoesisToolkit.Analyzers** — `NTK2001`–`NTK2003` resolve binding paths, `clr-namespace` declarations
  and `x:Static` references against the compilation; `NTK2101` requires balanced Noesis event
  subscriptions.
- **NoesisToolkit.Mvvm** — `[DependencyProperty]` and `[DelegateCommand]` generators
  (`NTK3001`, `NTK3101`–`NTK3103`) plus the `DelegateCommand` / `AsyncDelegateCommand` runtime.
- **Compiled bindings** — a `{Binding}` an opted-in document resolves end to end is emitted as a
  chain of typed reads through `CompiledBinding`, rather than a path string Noesis resolves
  reflectively. `ElementName` and `RelativeSource AncestorType` sources, attached-property and
  identity (`{Binding .}`) paths, `Converter`/`StringFormat` value shaping, and two-way write-back
  settled at run time off the target property's own metadata.
- **Compiled multi-bindings and triggers** — `CompiledMultiBinding` and `CompiledTriggerSet`.
- **`ntk:AncestorDataType`** — states the type a detached `FindAncestor` walk lands on.
- **`XamlCompileSurvey.g.cs`** — per-project and per-file coverage with a named reason for every
  binding that fell back, so the gap is answerable from a build. `NTK2004` reports a binding with no
  declared DataContext type.

### API surface

- `NoesisToolkit.Mvvm` holds only what a person writes: `[DependencyProperty]`, `[DelegateCommand]`,
  `DelegateCommand` and `AsyncDelegateCommand`. The compiled-binding runtime that generated code
  calls lives in `NoesisToolkit.Mvvm.CodeGen` and is marked `[EditorBrowsable(Never)]`, so it stays
  free to change without breaking hand-written code.

### Requirements

- The two analyzer packages need Roslyn 4.8 or later (Visual Studio 2022 17.8 / .NET 8 SDK).
- `NoesisToolkit.Mvvm` emits partial properties and the `field` keyword, so it needs a C# 14 compiler
  (.NET 10 SDK or later). Its runtime targets `netstandard2.0` and `net9.0` and depends on
  `Noesis.GUI` >= 4.0.0.

[0.3.7]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.3.7
[0.3.6]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.3.6
[0.3.5]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.3.5
[0.3.4]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.3.4
[0.3.3]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.3.3
[0.3.2]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.3.2
[0.3.1]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.3.1
[0.3.0]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.3.0
[0.2.7]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.7
[0.2.6]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.6
[0.2.5]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.5
[0.2.4]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.4
[0.2.3]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.3
[0.2.2]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.2
[0.2.1]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.1
[0.2.0]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.0
[0.1.2]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.2
[0.1.1]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.1
[0.1.0]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.0
