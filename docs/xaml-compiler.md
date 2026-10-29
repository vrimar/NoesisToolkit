# XAML compiler

## Generated surface

Per assembly:

| Type | Role |
|---|---|
| `XamlGenerated.<Assembly>.XamlRegistry` | `Dictionaries`, an array of `(string Path, string Logical, Func<ResourceDictionary> Build)` — one entry per compiled dictionary, `Path` being the document's project-relative path |
| `XamlGenerated.<Assembly>.XamlResources` | `Resolve(logical)`, `SharedByKey(key)`, and the `Defer`/`Flush` pair that settles cross-dictionary lookups |

Per document:

- a `ResourceDictionary` becomes `public static class <Name> { public static ResourceDictionary Build() }`,
  where `<Name>` is the project-relative path with every non-alphanumeric character removed, plus `Xaml`,
  and a leading `_` where the path starts with a digit — so `Views/Theme.xaml` becomes
  `ViewsThemexamlXaml` and `3D/Materials.xaml` becomes `_3DMaterialsxamlXaml`. The whole relative path
  is used, so two folders holding the same file name get distinct names; two paths that differ only in
  their separators, such as `Ui/Buttons.xaml` and `UiButtons.xaml`, share one, which `NTK0001` reports.
- an `x:Class` document contributes `InitializeComponent()`, one accessor per `x:Name` in the root's own
  name scope, and — unless it wires event handlers in markup — a `BuildXamlTree()` that constructs the
  tree directly. An accessor is `public` unless `x:FieldModifier` narrows it or the element's type is
  less visible than the class. A `Style` opens no name scope of its own: a name inside one belongs to
  the scope around it, as the engine registers it

Referenced assemblies that also ran the compiler are discovered through the
`XamlGenerated.Links.A_<assembly>` type each one declares, its assembly name spelled out in full, and
chained, so `Resolve`, `SharedByKey` and `Flush` reach across assembly boundaries even where two
assembly names share one generated namespace. An assembly whose generated namespace a reference already
declares takes that spelled-out name instead — `Foo.App` referencing `FooApp` generates into
`XamlGenerated.A_Foo_x002E_App` — so neither one's types hide the other's.

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

A compiled lookup that finds nothing in the installed graph or the application resources falls back to
`SharedByKey`, which answers from any dictionary this assembly compiled — including one the installed
graph does not hold. The parser would leave that key unresolved, or take the theme's value for an
implicit style. A dictionary loaded on demand relies on this; keep keys you mean to stay
theme-resolved out of compiled dictionaries the app never installs.

`{StaticResource}` and `{DynamicResource}` are not interchangeable, and the compiler keeps them apart.
A `StaticResource` is resolved once, against the dictionaries enclosing the place it is written, and
the value it found is assigned; a later change to that key does not reach it. A `DynamicResource` on a
`FrameworkElement` becomes a `SetResourceReference`, which resolves by tree position and tracks the
key for the element's lifetime — so on a template prototype, which is never in a tree, only the
`StaticResource` form resolves at all. Only the parser keeps one live on anything else, such as a
brush an element holds, so that element is left to the parser whole. Where the element is the
`x:Class` root itself, the whole document stays on `GUI.LoadComponent` and is reported as `NTK1005`.

A `{TemplateBinding X}` is never compiled. It is a one-way expression that converts nothing, and a
`Binding` on the templated parent is neither, but the managed API cannot install the expression. So
every element carrying one is constructed by the parser from its `{TemplateBinding}` attributes
alone — inside a template of the same kind, with the same `TargetType`, one parse per template — and
the compiler applies everything else to that instance. An element left to the parser whole is
parsed inside such a template too. A `{TemplateBinding}` anywhere the parser cannot build it that
way, such as a markup extension's argument, is reported as dead markup.

An attribute in Noesis' own extensions namespace, `clr-namespace:NoesisGUIExtensions` — such as
`noesis:Text.Stroke` or `noesis:Element.PPAAMode` — names a property no managed type declares, so the
element carrying it is built by the parser the same way, from those attributes alone; an element
setting one as a property element is left to the parser whole. A `Setter` naming such a property is
parsed inside a probe `Style`, and a `Trigger` or `Condition` takes the property and its converted
value from one. On the document's root, which the parser cannot build, the property is refused with
`NTK1001`, and so is a `{StaticResource}` value the parser cannot resolve alone.

Markup handed to the parser — a setter, a resource entry, an element left to it whole — is parsed
alone, so every `{StaticResource}` key it names has to resolve there. A setter or a resource entry
carries a copy of each declaration its own file holds; an element left whole has to declare the key
below itself, and its own attributes cannot read its own resources. A key the file does not declare
at all is left to the parser in an `x:Class` document: it is built after install, so the parser reads
the installed application resources, as the document's own parse would. In a dictionary that key is
refused with `NTK1001` — the dictionary is built before its graph is installed, a parser running alone
sees none of that graph, and what it parses is sealed, so the value cannot be supplied later. A
converter declared in `App.xaml` or another merged file, named by a setter's binding, is the common
case: declare it in the setter's own file.

A binding left to the engine is built in code, where no xmlns is in scope, so an attached property in
its path names its owner by CLR name — `(ui:Badge.Count)` is emitted as
`(MyApp.Controls.Badge.Count)`.

A template holds constructed prototypes, and a clone copies their local values. A control that loads
its own content in its constructor would carry that content into every clone on top of the content
its own constructor loads there, so inside a template its `Content` is cleared after construction,
which is what a parsed template holds.

A template also clones a keyed element it places, such as a `ContextMenu` resource, so the compiled
bindings inside a keyed element are wired per clone, as a template's own are.

## Trimming

What the generated code constructs and reads, the trimmer sees. What it hands Noesis to resolve by
name, it does not, and a miss is silent: a binding reads nothing, a parser-built element is never
created, an event trigger never fires. So the compiler roots those names itself, as
`[DynamicDependency]` attributes on the method that builds the document — `BuildXamlTree()` for an
`x:Class` document, `Build()` for a dictionary, `InitializeComponent()` for a document left to the
loader. A document the trimmer drops takes its roots with it. A type the generated code cannot name,
such as an internal type of another assembly, is rooted by its name and assembly.

| Reached by name | Rooted |
|---|---|
| a binding left native | each hop's property, on the type that declares it — on every class implementing an interface hop, and on the derived class that declares a member its declared type lacks |
| markup handed to the parser — a setter, a resource, an element carrying a `{TemplateBinding}`, a document left to the loader | the public parameterless constructor of each managed type it builds, and every plain CLR property it sets or binds, whether by attribute, by property element or as content |
| a managed object inside a template | its constructor: Noesis builds each copy of the template by constructing it again |
| a `GridViewColumn.DisplayMemberBinding` | each hop, read off the item type of the list's `ItemsSource`, or the type `ntk:ItemType` states on the column |
| a literal `DisplayMemberPath` or `SelectedValuePath`, and a trigger's `EventName` | the property on the item type, or the type `ntk:ItemType` states on the items control; the event on the trigger's source, on every class implementing it where that is an interface |
| a dependency property Noesis resolves by name — a setter's `Property`, an attribute the parser sets, a hop of a binding left native, an attached property met by owner name | the `XProperty` field on the type that declares it: Noesis registers the property by running that type's class constructor, which a trimmed build keeps only while the field is reachable |
| an enum written or read as text | its literals |

A dependency property's CLR wrapper is never rooted alone: Noesis reads a dependency property
through its own registration, and a wrapper kept without its `XProperty` field would be registered
again as a plain property that shadows it — so where a class implements an interface hop with a
dependency property, its wrapper is left alone too. Nothing of `Noesis.GUI` is rooted member by
member — the engine reflects over its own types far beyond what markup shows, so an app keeps that
assembly whole. A name the compiler cannot type is reported as `NTK1004`, and so is an interface hop
no class the compiler can see implements — an implementer in an assembly that references the
document's own is out of its sight.

A column's own bindings, such as `Header="{Binding Title}"`, read the list's DataContext like any
other attribute of it. So where the rows' type is what the compiler cannot see — `ItemsSource`
bound through a converter, set in a style or from code — `ntk:ItemType` on the `GridViewColumn`, or
on the items control a `DisplayMemberPath` reads, states the rows' type and nothing else.
`ntk:DataType` there would retype the column's own bindings too.

The overrides Noesis looks up by reflection when it first meets a type — `MeasureOverride`,
`ArrangeOverride`, `OnApplyTemplate`, `OnRender` and the rest, and any object's own `ToString` and
`Equals`, a record's compiler-written ones included — are rooted on every managed type a document
references, from whatever assembly: its elements, the types it states, each hop and value of its
bindings, the items those values hold, and every class it roots a member of. Without them a
trimmed build keeps the override's code but not its metadata, and Noesis never calls it.
`NoesisToolkit.Mvvm` also roots them on every type an assembly referencing it declares, on a module
initializer, since a control built only from code has no document to carry them.

## Event handlers

A document that wires handlers in markup — including an attached handler such as
`ButtonBase.Click="OnClick"`, which subscribes the owner's routed event — keeps loading through
`GUI.LoadComponent`, and gets a generated `ConnectEvent` instead of a compiled tree. A handler
subscribed from managed code pins the root through the child element; the native loader owns that
lifetime and the compiled path cannot. Such a document is reported as `NTK1005`, since its source
is the one the app still has to ship.
A root that is no `FrameworkElement`, such as a `ResourceDictionary` with an `x:Class`, has no
`ConnectEvent` for the loader to call, so a handler in its markup is refused with `NTK1001`.

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
