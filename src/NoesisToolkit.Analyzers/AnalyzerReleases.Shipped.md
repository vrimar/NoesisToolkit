; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
NTK2001 | Reliability | Warning | Binding path does not resolve
NTK2002 | Reliability | Error | clr-namespace does not resolve
NTK2003 | Reliability | Error | x:Static does not resolve
NTK2004 | Reliability | Error | Binding has no declared DataContext type
NTK2101 | Reliability | Warning | Noesis element event subscription is never unsubscribed
