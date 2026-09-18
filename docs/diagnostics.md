# Diagnostics

| Id | Severity | Meaning |
|---|---|---|
| `NTK0001` | Error | two inputs generated the same file name |
| `NTK1001` | Error | XAML construct the compiler does not support |
| `NTK1002` | Warning | coverage note — markup reached that nothing consumes |
| `NTK1003` | Error | the compiler threw; the message carries the stack |
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
