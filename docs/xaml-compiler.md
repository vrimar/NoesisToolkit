# XAML compiler

## Generated surface

Per assembly:

| Type | Role |
|---|---|
| `XamlGenerated.<Assembly>.XamlRegistry` | `Dictionaries`, an array of `(string Path, string Logical, Func<ResourceDictionary> Build)` — one entry per compiled dictionary, `Path` being the document's project-relative path |
| `XamlGenerated.<Assembly>.XamlResources` | `Resolve(logical)`, `SharedByKey(key)`, and the `Defer`/`Flush` pair that settles cross-dictionary lookups |

Per document:

- a `ResourceDictionary` becomes `public static class <Name> { public static ResourceDictionary Build() }`,
  where `<Name>` is the project-relative path with every non-alphanumeric character removed, plus `Xaml` —
  so `Views/Theme.xaml` becomes `ViewsThemexamlXaml`. The whole relative path is used, so two folders
  holding the same file name cannot collide.
- an `x:Class` document contributes `InitializeComponent()`, one accessor per `x:Name` in the root's own
  name scope, and — unless it wires event handlers in markup — a `BuildXamlTree()` that constructs the
  tree directly

Referenced assemblies that also ran the compiler are discovered through their `XamlResources` type and
chained, so `Resolve`, `SharedByKey` and `Flush` reach across assembly boundaries.

## Resource timing

Resolution has exactly one correct moment, and it differs by document kind:

- A **dictionary** is built *before* its graph is assembled, so a key that may live in a sibling
  defers to `Flush` — after assembly, before `GUI.SetApplicationResources`, which seals `Setter` values.
- A **root** is built *after* install, so it resolves inline.
- A Noesis `Binding` seals on first use, which is *earlier* than `Flush`, so binding markup resolves
  eagerly and a converter can never be attached later.

`SharedByKey` deliberately builds a separate value-source copy per merge point. Sharing one resolved
dictionary across merge points changes style resolution.

`{StaticResource}` and `{DynamicResource}` are not interchangeable, and the compiler keeps them apart.
A `StaticResource` is resolved once, against the dictionaries enclosing the place it is written, and
the value it found is assigned; a later change to that key does not reach it. A `DynamicResource` on a
`FrameworkElement` becomes a `SetResourceReference`, which resolves by tree position and tracks the
key for the element's lifetime — so on a template prototype, which is never in a tree, only the
`StaticResource` form resolves at all.

`{TemplateBinding X}` compiles to a `Binding` on the templated parent, because the managed API cannot
install a template-binding expression. The two agree except where the source and target types differ:
the native parser leaves the target unset there, and the binding converts.

## Event handlers

A document that wires handlers in markup keeps loading through `GUI.LoadComponent`, and gets a
generated `ConnectEvent` instead of a compiled tree. A handler subscribed from managed code pins the
root through the child element; the native loader owns that lifetime and the compiled path cannot.

## Triaging a failure

`XamlCompileSurvey.g.cs` also reports `DEAD key <key>` for a key two documents both declare, which
disables the compile-time lookup for it. No survey is emitted when the compilation references no
`Noesis.GUI`, or when no document matched `NoesisXamlExtensions`.

`NTK1001` names the document it failed on.
