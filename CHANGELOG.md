# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

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

[0.2.2]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.2
[0.2.1]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.1
[0.2.0]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.2.0
[0.1.2]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.2
[0.1.1]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.1
[0.1.0]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.0
