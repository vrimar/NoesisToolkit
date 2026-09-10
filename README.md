# NoesisToolkit

Build-time tooling for [NoesisGUI](https://www.noesisengine.com/) on .NET. XAML becomes C# the
compiler can see, binding paths fail the build instead of the frame, and the MVVM boilerplate writes
itself.

Not affiliated with or endorsed by Noesis Technologies.

| Package | What it does |
|---|---|
| `NoesisToolkit.XamlCompiler` | compiles XAML to C# — no runtime parse, plus `InitializeComponent` and typed `x:Name` accessors |
| `NoesisToolkit.Analyzers` | validates binding paths, `clr-namespace` declarations, `x:Static` references, and Noesis event lifetimes |
| `NoesisToolkit.Mvvm` | `[DependencyProperty]`, `[DelegateCommand]`, and the commands behind them |

Each is independent. Take one, take all three.

## NoesisToolkit.XamlCompiler

```xml
<PackageReference Include="NoesisToolkit.XamlCompiler" PrivateAssets="all" />
```

Generated code builds the same object graph the native Noesis parser would, so the tree is
constructed without XML on the hot path and every type reference is a real symbol the compiler and
the trimmer can see.

An `x:Class` document needs the matching partial:

```csharp
public partial class Shell : UserControl
{
    public Shell() => InitializeComponent();
}
```

Wire the flush into startup — after the dictionaries are assembled, before
`GUI.SetApplicationResources`, which seals `Setter` values:

```csharp
var resources = XamlGenerated.MyApp.XamlResources.Resolve("MyApp;Themes/Generic.xaml");
XamlGenerated.MyApp.XamlResources.Flush(resources);
GUI.SetApplicationResources(resources);
```

| Property | Default | Meaning |
|---|---|---|
| `NoesisXamlPackPrefix` | the assembly name | the pack name a `Source=` URI uses: `prefix;Relative/Path.xaml` |
| `NoesisXamlExtensions` | `.xaml` | `;`-separated extensions to compile |
| `NoesisXamlNamespaces` | — | extra xmlns → CLR namespace mappings, `uri=Ns1,Ns2`, `;`-separated; extends the built-ins rather than replacing them |
| `EnableDefaultNoesisXamlItems` | `true` | set `false` to supply the `AdditionalFiles` items yourself |

`clr-namespace:` URIs need no mapping — they name their namespace directly.

Every compiled assembly gets a `XamlCompileSurvey.g.cs` whose header comment lists each document as
`OK`, `FAIL <reason>` or `DEAD <markup the compiler reached but nothing consumes>`. A document the
compiler cannot handle has two escape hatches: give it an event handler in markup, which moves it to
the native loader wholesale, or exclude it from `AdditionalFiles`.

The generated surface, resource timing and the event-handler fallback are in
[docs/xaml-compiler.md](https://github.com/vrimar/NoesisToolkit/blob/main/docs/xaml-compiler.md).

## NoesisToolkit.Analyzers

```xml
<PackageReference Include="NoesisToolkit.Analyzers" PrivateAssets="all" />
```

Noesis resolves binding paths, namespaces and `x:Static` references reflectively at load and only
logs when it misses, so a rename that skips the XAML renders nothing and leaves no trace. These
rules move that failure to the build.

A binding is only checkable where the type its DataContext resolves to is known. A `DataTemplate`
carrying a `DataType` states it; a keyed template, a bare `Style` or a template with no host does
not, and those bindings are skipped rather than guessed. `ntk:DataType` states it where markup
cannot, and `ntk:CompileBindings` makes stating it mandatory. Both live in the toolkit xmlns, which
Noesis drops as unknown, so they reach the build and never reach the graph:

```xml
<ResourceDictionary
  xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
  xmlns:ui="clr-namespace:MyApp.ViewModels"
  xmlns:ntk="https://github.com/vrimar/NoesisToolkit"
  ntk:CompileBindings="True">

  <DataTemplate x:Key="Row" ntk:DataType="ui:ItemViewModel">
    <TextBlock Text="{Binding Title}" />
  </DataTemplate>
</ResourceDictionary>
```

`ntk:DataType` goes on any element and covers everything inside it. Declare `ntk:CompileBindings`
per document rather than globally: turning it on across a codebase that has never used it fails
every document at once.

`NoesisXamlExtensions` and `EnableDefaultNoesisXamlItems` apply here too.
`NoesisAnalyzeXamlBindings` (default `true`) gates `NTK2001`–`NTK2004` only; `NTK2101` analyses C#
rather than XAML and is not gated by a property — adjust it through `.editorconfig` severity like
any other analyzer rule.

With the XAML compiler also referenced, a `{Binding}` in an `x:Class` document that resolves end to
end is emitted as a chain of typed reads instead of a reflective path string, so a rename that
misses the XAML fails the build. Everything it cannot resolve keeps its native `Noesis.Binding`, so
opting a document in is safe regardless — see
[docs/compiled-bindings.md](https://github.com/vrimar/NoesisToolkit/blob/main/docs/compiled-bindings.md).

## NoesisToolkit.Mvvm

```xml
<PackageReference Include="NoesisToolkit.Mvvm" />
```

```csharp
public partial class Shell : UserControl
{
    [DependencyProperty]
    public partial string? Title { get; set; }

    [DelegateCommand]
    void Save() { }                     // SaveCommand

    [DelegateCommand]
    async ValueTask Load() { }          // LoadCommand, refuses re-entry while in flight
}
```

`[DependencyProperty]` takes the metadata Noesis needs:

```csharp
[DependencyProperty(0f, nameof(OnWidthChanged), FrameworkPropertyMetadataOptions.AffectsMeasure)]
public partial float Slot { get; set; }

static void OnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }
```

A `static` partial property registers as an attached property and gains generated `GetSlot` /
`SetSlot` accessors instead.

## Requirements

- `NoesisToolkit.Mvvm` emits partial properties and the `field` keyword, so it needs a C# 14
  compiler (.NET 10 SDK or later). The runtime library targets `netstandard2.0` and `net9.0`.
- It depends on `Noesis.GUI` >= 4.0.0, whose package declares `win10-*` runtime identifiers the
  modern SDK no longer probes — consumers may want `<NoWarn>$(NoWarn);NETSDK1206</NoWarn>`.
- The two analyzer packages need Roslyn 4.8 or later (Visual Studio 2022 17.8 / .NET 8 SDK).

## Diagnostics

Every `NTK*` id the toolkit raises, and how to read the generated code, are in
[docs/diagnostics.md](https://github.com/vrimar/NoesisToolkit/blob/main/docs/diagnostics.md).

## Building

```bash
dotnet build NoesisToolkit.slnx
dotnet run --project tests/NoesisToolkit.Tests
dotnet run --project tests/NoesisToolkit.Equivalence.Tests
csharpier format .
```

`NoesisToolkit.Tests` compiles each generator's output against a stub Noesis surface, so emitted C#
that does not bind fails the suite. `NoesisToolkit.Equivalence.Tests` is the gate that proves
faithfulness: every fixture is realized twice — once by the real Noesis parser, once by the compiled
path — and the two object graphs are diffed. It links `libNoesis.so`, so it needs X11 and GL
present, and it is never skipped.

## Releasing

The version lives in the tag, nowhere in the tree. Push `v<semver>` and the release workflow builds,
runs both suites, packs and pushes to NuGet:

```bash
git tag v0.1.0 && git push origin v0.1.0
```

All three packages share one version. A tag carrying a `-suffix` (`v0.3.0-rc.1`) publishes as a
prerelease. Below `1.0.0` a minor bump may break API; that is what `0.x` is for.

Never reuse a version — NuGet keeps the first upload of one forever. If a push half-fails, re-running
the workflow is safe (`--skip-duplicate`); if a bad package got through, unlist it and ship the next
patch. To rehearse without publishing, run the workflow manually: it takes a version and a `publish`
flag that defaults to off.

## Licence

MIT.
