# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

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

[0.2.5]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.5
[0.2.4]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.4
[0.2.3]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.3
[0.2.2]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.2
[0.2.1]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.1
[0.2.0]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.0
[0.1.2]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.2
[0.1.1]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.1
[0.1.0]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.0
