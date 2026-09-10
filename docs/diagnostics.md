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
| `NTK2101` | Warning | Noesis element event subscription is never unsubscribed |
| `NTK3001` | Error | `[DependencyProperty]` owner is not partial |
| `NTK3101` | Error | `[DelegateCommand]` owner is not a partial class |
| `NTK3102` | Error | `[DelegateCommand]` method returns something other than `void`/`ValueTask` |
| `NTK3103` | Error | `[DelegateCommand]` method takes more than one parameter |

To read the generated code:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```
