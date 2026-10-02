# Diagnostics

| Id | Severity | Meaning |
|---|---|---|
| `NTK0001` | Error | two inputs generated the same file name |
| `NTK1001` | Error | XAML construct the compiler does not support |
| `NTK1002` | Warning | coverage note — markup reached that nothing consumes |
| `NTK1003` | Error | the compiler threw; the message carries the stack |
| `NTK1004` | Warning | Noesis resolves a name by reflection on a type the compiler cannot determine, so trimming may drop it; raised only where the trim analyzer runs |
| `NTK1005` | Info | a document is parsed at run time, so its source has to ship; raised at the handler attribute that keeps it on the loader |
| `NTK1006` | Warning | `ntk:DataType`, `ntk:AncestorDataType` or `ntk:ItemType` names a type the compiler cannot find |
| `NTK2001` | Warning | binding path does not resolve |
| `NTK2002` | Error | `clr-namespace` does not resolve |
| `NTK2003` | Error | `x:Static` does not resolve |
| `NTK2004` | Error | binding has no declared DataContext type, in a document marked `ntk:CompileBindings` |
| `NTK2005` | Error | enum-valued attribute names a member the enum does not declare |
| `NTK2101` | Warning | Noesis element event subscription is never unsubscribed |
| `NTK2102` | Warning | a command on a Noesis control captures the control |
| `NTK3001` | Error | `[DependencyProperty]` owner is not partial |
| `NTK3101` | Error | `[DelegateCommand]` owner is not a partial class |
| `NTK3102` | Error | `[DelegateCommand]` method returns something other than `void`/`ValueTask` |
| `NTK3103` | Error | `[DelegateCommand]` method takes more than one parameter |

## Why NTK1004 exists

A trimmed or NativeAOT build keeps only what code references, and Noesis reaches some members by
name: a binding left native reads each hop's property by reflection, the parser builds a fragment it
is handed from type names, an `EventName` or `DisplayMemberPath` is a string looked up at run time.
The compiler roots each of these for the trimmer (see
[xaml-compiler.md](xaml-compiler.md#trimming)), but it can only root a member of a type it can name.
Where it cannot — a binding under a DataContext no `ntk:DataType` states, an ancestor walk with no
`ntk:AncestorDataType`, a path that steps through `object`, a `DisplayMemberPath` over items it
cannot type, a name the type does not have, an interface no class it can see implements — NTK1004
points at the attribute holding the markup, because a trimmed build may drop the member and Noesis
then resolves nothing without a word.

State the type: `ntk:DataType` on the element or an enclosing one, `ntk:AncestorDataType` for a
`FindAncestor` binding, or `ntk:ItemType` for rows whose list's `ItemsSource` the compiler cannot
type — on a `GridViewColumn`, or on the items control a `DisplayMemberPath` or `SelectedValuePath`
reads. `ntk:ItemType` types the rows alone; `ntk:DataType` on a column would retype the column's own
bindings, such as its `Header`, too. Where the source really is several unrelated types, give them
an interface that declares what the markup reads, and state that; the compiler roots the property
on every class implementing it. It can only see the classes of the document's own assembly and of
what that references, so markup in a library that binds through an interface only its consumers
implement is reported: state a class the library declares instead.

It is raised only where the trim analyzer runs — a project that publishes trimmed or AOT, or
declares itself trimmable or AOT-compatible — so a project nothing will trim never sees it. The roots
are emitted everywhere, since a library that does not trim itself can be trimmed into one that does.

## Why NTK1005 exists

A compiled document builds its tree in code and never reads its own XAML, so an app can stop
shipping the source of everything the compiler builds — the full layout, every binding path and
every comment. A document that wires a handler in markup is the exception: it keeps loading through
`GUI.LoadComponent` (see [xaml-compiler.md](xaml-compiler.md#event-handlers)), which parses that
source at run time. Left as information it costs nothing to an app that ships its XAML anyway. An
app that does not raises it to an error, so a handler added in markup fails the build instead of a
screen failing to load. The diagnostic sits in a XAML file, which no `.editorconfig` section
reaches, so the severity goes in a global analyzer config — a file holding `is_global = true` and
`dotnet_diagnostic.NTK1005.severity = error`, listed as an `EditorConfigFiles` item.

## Why NTK2101 and NTK2102 exist

Noesis Managed keeps handler delegates, and the managed objects a native property holds, in static
tables. An entry is cleaned when the element's native object is destroyed — and that cannot happen
while the entry keeps the managed proxy, and therefore the proxy's native reference, alive. The
element and everything its DataContext reaches then live for the process lifetime.

Two shapes close that loop, and neither looks wrong:

- an instance handler subscribed to the element itself or to one of its template children. A `-=`
  the same flow re-attaches does not count — that guards against a second `OnApplyTemplate` stacking
  the handler, but never runs when the element goes away. Detach from `Unloaded`, or make the handler
  `static` and take the owner from `sender` (its `TemplatedParent`, for a template child).
- a command whose delegates captured the element, bound into the element's own template. The native
  `Command` property holds the command, the command holds the element, and the element owns the
  template child. Prefer a `static` `Click` handler that reads `TemplatedParent`, or keep commands on
  a ViewModel.

Both rules read the code, so neither sees what markup wires or what a native property picks up at
runtime. [`NoesisToolkit.Testing`](https://www.nuget.org/packages/NoesisToolkit.Testing) closes that
gap by proving collectability against a live renderless view.

To read the generated code:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```
