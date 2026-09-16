# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

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

[0.1.0]: https://github.com/vrimar/NoesisToolkit/releases/tag/v0.1.0
