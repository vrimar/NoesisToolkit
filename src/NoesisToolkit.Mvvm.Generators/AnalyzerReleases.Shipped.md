; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
NTK0001 | NoesisToolkit | Error | Two inputs generated the same file name
NTK3001 | NoesisToolkit | Error | [DependencyProperty] owner must be partial
NTK3101 | NoesisToolkit | Error | [DelegateCommand] owner must be a partial class
NTK3102 | NoesisToolkit | Error | [DelegateCommand] method has an unsupported return type
NTK3103 | NoesisToolkit | Error | [DelegateCommand] method takes too many parameters

## Release 0.6.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
NTK3002 | NoesisToolkit | Error | [DependencyProperty] of a type Noesis cannot write from managed code is settable

## Release 0.6.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
NTK3003 | NoesisToolkit | Error | [DependencyProperty] of a type Noesis cannot register from managed code

## Release 0.7.4

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
NTK3004 | NoesisToolkit | Error | [DependencyProperty] instance property is not partial
NTK3005 | NoesisToolkit | Error | [DependencyProperty] instance property already has an implementing declaration
