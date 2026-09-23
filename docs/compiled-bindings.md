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

## Values and text

A compiled binding has to write what the engine would have written, not what .NET would. The two
disagree on text: the engine shows numbers in its own invariant form whatever the current culture,
rounds a formatted number from its shortest round-trip digits, and shows nothing for a value that is
not a number and has no `ToString` of its own. So a number reaching a text slot, and a `StringFormat`
the compiler can reproduce exactly, are emitted through that rule; a source whose text depends on its
run-time type, and a format the engine applies by rules of its own, keep their native binding.

A converter's result is written as it is when the slot's type holds it. A result of another type is
handed to the engine to convert, as a native binding would; `DependencyProperty.UnsetValue` writes
the slot's default and `Binding.DoNothing` leaves the slot alone.

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
written in the markup overrides the property's default. The compiler still lists the properties that
bind two way by metadata, only to refuse a path into one that has nothing to write back to.

After every write back the source is read again, as the engine does, so a setter that clamps, rounds
or refuses shows its own value in the target, and a setter that throws restores the old one. A
`ConvertBack` that returns `Binding.DoNothing` or `DependencyProperty.UnsetValue` writes nothing.

A value the binding wrote itself is never written back. That matters for an element built before it
is shown: a dependency property reports nothing outside a live view, so the target's first value
reports only as the element enters one, long after the binding put it there. A `LostFocus` binding
writes back on every blur, edited or not, because the engine does.

Watching the target property touches no metadata. A property the `[DependencyProperty]` generator
registered reports its own changes; any other is watched through a hidden attached property bound
to it, one per element and property. Overriding the metadata instead would be free per instance, but
Noesis warns that the owner already has metadata, and on a property the engine registered it corrupts
native state for every instance in the process.

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
style still compiles. A setter that moves a condition of its own set is evaluated again until the set
settles, as the engine does as it applies each setter.

## Lifetime

A compiled binding, MultiBinding or trigger set lives exactly as long as the native element it was
bound to. Noesis holds a native element's managed proxy weakly: one is minted whenever managed code
asks for the element and collected once nothing managed holds it, while the element lives on in the
tree. So what the toolkit keeps per element is keyed by the native handle, not the proxy, and ends
when Noesis destroys the element.

Nothing a binding keeps holds a proxy either. A proxy holds a native reference, and a template
part's binding usually reads its templated parent or an ancestor, which holds the part: a proxy held
from there would keep the whole subtree alive. A binding holds the elements it touches by handle and
resolves a proxy only when it reads or writes one, so after a collection each element it touches
costs one proxy until the next.

An element can be destroyed while a binding still refers to it — the source of an `ElementName`
binding removed from its panel, say. Its handle is told when the element ends and reads as missing
from then on, as a source the resolver could not find does.

Subscriptions taken through `DependencyWatcher.Watch` and `Events.On` follow the same rule: they
last until the element is destroyed, as one on a Noesis event does. Each handler is handed the
element, because a handler that captured it, or the control that holds it, would keep both alive.

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

The same fact has a consequence that stays compiled on purpose. Natively, code that calls `SetValue`
on a one-way bound property replaces the binding, and the value it wrote stays. A compiled binding is
not an expression the engine can replace, so it keeps running: the next source change overwrites the
code's value, and a compiled trigger that deactivates clears it. Detecting such a write would mean
watching every compiled target, which costs about what the compiled binding saves. Code that owns a
property should not also bind it.

| Form | Kind | Why |
|---|---|---|
| a path off an element that is not one of its dependency properties | boundary | nothing else about an element can be watched |
| `StringFormat` or a one-way conversion with a write back | boundary | neither can be run backwards |
| `DataContext="{Binding …}"` on the element itself | boundary | the write would feed the next read its own result |
| a value the target slot's declared type does not accept | boundary | `SetValue` dispatches on that type, so a near-enough value is a crash |
| a trigger setter whose value is a resource lookup | boundary | the parser resolves it and `SetValue` does not |
| a template trigger setter with no `TargetName` | boundary | it writes the templated parent below that element's local value |
| a write back to a source read straight off a dependency property | gap | `SetValue` on that property would serve; only hop writes are emitted |
| a write back to a behavior, input binding or trigger action | boundary | the receiver is found again each time, so nothing stable can be watched |
| `RelativeSource Self` on a behavior, input binding or trigger action | gap | the chain would have to root at the receiver, not its host |
| `{TemplateBinding}` | boundary | a one-way expression that converts nothing; the parser builds it, see [xaml-compiler.md](xaml-compiler.md) |
| a binding on a template element that a template trigger names for the same property | boundary | the compiled local write would outrank the trigger |
| a binding on `ContentPresenter.Content` inside a template | boundary | a presenter the template gives no `Content` takes the templated parent's content and template, and a compiled write arrives after that is decided |
| a style trigger setter on a property the element sets itself — attribute, property element or content — or that a template trigger names | boundary | either one outranks a native style trigger, and a compiled local write would win |
| a template trigger condition read off a root that rebinds `DataContext`, or off `TemplatedParent` | gap | its source type is not what the document states |
| a `DataTrigger` on an `object` path with a non-null `Value` | gap | the engine converts the constant to whatever the path holds at run time |
| a `MultiBinding` into `DataContext`, or on a `ContentPresenter` reading its own `DataContext` | boundary | the write feeds its own read, or the presenter replaces the context with its content |
| text from a `char`, `decimal`, `object`, interface, or a type with no `ToString` override | boundary | what the engine shows depends on the run-time type, or it shows nothing |
| a `StringFormat` other than numbered holes with F, N, P, D or X | gap | the engine formats it by rules of its own the compiler does not mirror |
| an enum slot whose native type the managed side never registered | boundary | the managed side can neither read nor write it |
| `Mode=OneTime` or `OneWayToSource`, `UpdateSourceTrigger=Explicit` | gap | not implemented |
| `FallbackValue`, `TargetNullValue` | gap | not implemented |
| `RelativeSource PreviousData`, or `AncestorLevel` past 1 | gap | not resolved yet |
| a path stepping through an open generic | gap | not resolved yet |
| `Mode=TwoWay` on a path that genuinely has no setter | author | there is nothing to write back to |
| a `DataTemplate` with no declared `ntk:DataType` | author | falls back until the type is stated |
| a keyed template or style with no declared `ntk:DataType` | author | any document or code can apply the key, so the uses one document shows prove nothing |
| bindings below a `DataContext` the compiler cannot type — a converter, a knob, another source, a resource or a property element | author | state the type with `ntk:DataType` below it |
| a template hosted through a `Content`, `ItemsSource` or `Header` binding with a converter or format | author | what the host is handed is not the type its path reads |
| an `ntk:CompileBindings` value that is not a boolean | author | the document has to say which it means |
| an `ElementName` naming an element in another template | boundary | a clone's name scope answers for its own template only |
| an ancestor reached past a `Style` | boundary | a style detaches content from where it was written |

A compiled binding still updates only while its element is in a live view: a dependency property
raises nothing at all outside one, so a detached element settles on its next load rather than
continuously.
