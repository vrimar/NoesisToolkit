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

- A **dictionary** is built *before* its graph is assembled. A key is looked up where the reference
  sits, against the dictionaries enclosing it so far, as the parser does — so a key declared further
  down the same dictionary is not found. Only a key none of them holds, which may live in a sibling,
  defers to `Flush` — after assembly, before `GUI.SetApplicationResources`, which seals `Setter` values.
  A dictionary built after that, as a reload does, resolves against the graph installed at that moment.
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

A `{TemplateBinding X}` is never compiled. It is a one-way expression that converts nothing, and a
`Binding` on the templated parent is neither, but the managed API cannot install the expression. So
every element carrying one is constructed by the parser from its `{TemplateBinding}` attributes
alone — inside a template of the same kind, with the same `TargetType`, one parse per template — and
the compiler applies everything else to that instance. A `{TemplateBinding}` anywhere the parser
cannot build it that way, such as a markup extension's argument, is reported as dead markup.

A binding left to the engine is built in code, where no xmlns is in scope, so an attached property in
its path names its owner by CLR name — `(ui:Badge.Count)` is emitted as
`(MyApp.Controls.Badge.Count)`.

A template holds constructed prototypes, and a clone copies their local values. A control that loads
its own content in its constructor would carry that content into every clone on top of the content
its own constructor loads there, so inside a template its `Content` is cleared after construction,
which is what a parsed template holds.

## Event handlers

A document that wires handlers in markup — including an attached handler such as
`ButtonBase.Click="OnClick"`, which subscribes the owner's routed event — keeps loading through
`GUI.LoadComponent`, and gets a generated `ConnectEvent` instead of a compiled tree. A handler
subscribed from managed code pins the root through the child element; the native loader owns that
lifetime and the compiled path cannot.

## A compiled control inside a template the parser reads

`GUI.LoadComponent` does nothing while the parser builds a template's visual tree, so a control that
loads its own content leaves the template's prototype empty and each clone loads its own. A compiled
`x:Class` builds its tree in its constructor and cannot tell that the parser is building a template
around it: Noesis exposes that state to nothing but its own loader. Used inside a template the
compiler builds, its prototype content is cleared as the parser would leave it. Used inside a
template XAML parsed at run time, the prototype keeps its content and every clone copies it —
duplicate-name warnings, and a copy whose compiled bindings are not wired. Either compile the
document holding that template, or exclude the control's own document from `AdditionalFiles` so it
loads through the parser.

## Triaging a failure

`XamlCompileSurvey.g.cs` also reports `DEAD key <key>` for a key two documents both declare, which
disables the compile-time lookup for it. No survey is emitted when the compilation references no
`Noesis.GUI`, or when no document matched `NoesisXamlExtensions`.

`NTK1001` names the document it failed on.
