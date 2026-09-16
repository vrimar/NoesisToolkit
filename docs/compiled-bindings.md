# Compiled bindings

In an `x:Class` document that opts in, a `{Binding}` the compiler can resolve end to end is emitted
as a chain of typed reads instead of a path string Noesis resolves reflectively:

| | emitted |
|---|---|
| reflective | `target.SetBinding(TextBlock.TextProperty, new Binding("Def.Family"))` |
| compiled | `CompiledBinding.Bind(target, TextBlock.TextProperty, new CompiledBindingSpec { Hops = [hop(Def), hop(Family)], … })` |

A rename that misses the XAML then fails the build rather than rendering nothing. Property names do
still reach the binary: `PropertyChangedEventArgs.PropertyName` is a string, so change notification
matches on it whatever the read path looks like.

Everything else stays a `Noesis.Binding`, because a compiled binding that guessed would be worse than
a reflective one. A binding compiles when its source resolves, its path resolves hop by hop off the
declared type, and the value reaching the target property is one that property's slot accepts
— `Converter` and `StringFormat` count, since shaping the value is what they are for. Noesis
dispatches `SetValue` on the property's declared type, so a near-enough value is a crash rather than a
coercion, and an unshaped mismatch falls back.

## Bindings sourced from another element

`ElementName` and `RelativeSource AncestorType` compile too. The source element is found at run time
and re-found each time the target loads, so a recycled container settles on the container it is under
now:

| Written | Found by |
|---|---|
| `ElementName` at the document root | the generated `x:Name` field |
| `ElementName` inside a template | `FindName` off the clone, which answers per clone |
| `RelativeSource AncestorType=T` | the nearest visual ancestor of type `T` |

The ancestor walk is visual rather than logical, because the logical parent chain stops at a
template's own root and never reaches the control that hosts it. Which element it lands on is derived
from the tree, not declared, so the first hop is type-checked at run time and reads null off anything
else rather than throwing.

Off an element the path reads either its `DataContext` — `{Binding ElementName=Grid,
Path=DataContext.SaveCommand}` — or one of its dependency properties, `{Binding ElementName=Box,
Path=Text}`. A dependency property is the only part of an element that can be watched; anything else
falls back. When a source element's DataContext is not what the surrounding markup implies, state it
with `ntk:DataType` on that element.

A `FindAncestor` binding written inside a detached template has no ancestor the document can prove,
so state the type the walk will land on with `ntk:AncestorDataType`:

```xml
<DataTemplate ntk:AncestorDataType="{x:Type app:ShellViewModel}">
  <TextBlock Text="{Binding DataContext.Title,
                    RelativeSource={RelativeSource AncestorType=UserControl}}" />
</DataTemplate>
```

## Two-way bindings

A binding whose last hop has a setter is emitted with a write back, and the direction is settled at
run time off the target property's own `FrameworkPropertyMetadata` — so `Mode=TwoWay` compiles, and so
does a property that binds two way without saying so:

```xml
<TextBox Text="{Binding Title}" />          <!-- two way, pushed on lost focus -->
<CheckBox IsChecked="{Binding Flag}" />     <!-- two way, pushed on every change -->
<TextBlock Text="{Binding Title}" />        <!-- one way: TextBlock.Text is not two way by default -->
```

`BindsTwoWayByDefault` and `DefaultUpdateSourceTrigger` are read from the property itself rather than
from a list in the compiler, so the policy cannot drift from what the engine does. `UpdateSourceTrigger`
written in the markup overrides the property's default.

Watching the target property means overriding its metadata, which is process-global and cannot be
uninstalled. `DependencyWatcher` claims each property exactly once, on its owner type, and fans out to
instances from a table of its own. Two consequences worth knowing: anything that overrides metadata
for the same property *after* the first compiled binding on it silently takes the callback, and there
is no way to read back whether that happened.

## Multi-bindings and triggers

A `MultiBinding` compiles when every child resolves and the whole carries exactly one of a
`Converter` or a `StringFormat`. It is one-way only: a write back would need `ConvertBack`, so a slot
that binds two way by default keeps its native binding unless the markup says `OneWay` itself. A
child whose path runs out reaches the converter as `DependencyProperty.UnsetValue`, exactly as the
native engine hands it over.

A `DataTrigger` or `MultiDataTrigger` in a style compiles into a `CompiledTriggerSet`, which
evaluates the style's triggers together because they share precedence: a later trigger's setter beats
an earlier one's on the same property, and a property no active trigger sets falls back to whatever
the style's ordinary setters give it. The style's own setters stay native — only the triggers are
stripped. A trigger the compiler cannot resolve whole keeps its native form, and the rest of the
style still compiles.

## What does not compile

Everything below stays a `Noesis.Binding`. Opting a document in is safe regardless: what cannot
compile keeps the behaviour it already had — including `ElementName`, which resolves through the name
scope the generated root registers.

`XamlCompileSurvey.g.cs` labels every refusal `Gap`, `Boundary` or `Author`, and is regenerated
every build — read it rather than this table for what a given project is actually leaving native.
Only a **gap** is work. A **boundary** must stay native to keep behaviour, and closing one would be
a defect, not an improvement. An **author** row needs the document to change.

The boundary that catches most people: a compiled write is a *local value*, because `SetValue` is all
the managed API offers. Every construct that natively writes *below* local precedence therefore
cannot be compiled. That rules out a template trigger setter with no `TargetName` — it writes the
templated parent, which the control's own local value outranks. `TemplateTriggerPrecedenceSpikeTests`
proves both halves: the trigger loses to a local value, and wins over the template's own attribute,
which is why the named form does compile.

| Form | Kind | Why |
|---|---|---|
| a path off an element that is not one of its dependency properties | boundary | nothing else about an element can be watched |
| `StringFormat` or a one-way conversion with a write back | boundary | neither can be run backwards |
| `DataContext="{Binding …}"` on the element itself | boundary | the write would feed the next read its own result |
| a value the target slot's declared type does not accept | boundary | `SetValue` dispatches on that type, so a near-enough value is a crash |
| a trigger setter whose value is a resource lookup | boundary | the parser resolves it and `SetValue` does not |
| a template trigger setter with no `TargetName` | boundary | it writes the templated parent below that element's local value |
| a write back to a source read straight off a dependency property | gap | `SetValue` on that property would serve; only hop writes are emitted |
| a write back to a behavior, input binding or trigger action | boundary | the receiver is found by position each time, so nothing stable can be watched |
| `Mode=OneTime` or `OneWayToSource`, `UpdateSourceTrigger=Explicit` | gap | not implemented |
| `FallbackValue`, `TargetNullValue` | gap | not implemented |
| `RelativeSource PreviousData`, or `AncestorLevel` past 1 | gap | not resolved yet |
| a path stepping through an open generic | gap | not resolved yet |
| `Mode=TwoWay` on a path that genuinely has no setter | author | there is nothing to write back to |
| a `DataTemplate` with no declared `ntk:DataType` | author | falls back until the type is stated |
| an `ElementName` naming an element in another template | boundary | a clone's name scope answers for its own template only |
| an ancestor reached past a `Style` | boundary | a style detaches content from where it was written |

A compiled binding still updates only while its element is in a live view: a dependency property
raises nothing at all outside one, so a detached element settles on its next load rather than
continuously.
