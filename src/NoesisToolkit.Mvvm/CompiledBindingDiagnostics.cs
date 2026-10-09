using System;

namespace NoesisToolkit.Mvvm;

/// <summary>What a compiled binding reports where the native engine would log: its source was not the
/// type the path was compiled against. The binding fails as a native one would, writing the
/// property's default, and says so here rather than throwing out of a Noesis callback.</summary>
public static class CompiledBindingDiagnostics
{
    /// <summary>One miss, described in a line. Raised on the thread that evaluated the binding, every
    /// time it evaluates to a miss.</summary>
    public static event Action<string>? Missed;

    internal static void Miss(string path, object source, object? target) =>
        Missed?.Invoke(
            $"Path '{path}' on {target?.GetType().Name ?? "a released element"} cannot read a "
                + $"{source.GetType().FullName}: the source is not the type the binding was compiled against"
        );
}
