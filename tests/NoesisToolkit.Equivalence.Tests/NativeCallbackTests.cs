using System.Reflection;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class NativeCallbackTests
{
    /// <summary>Noesis calls the change trampoline from native code, and a browser build generates that
    /// entry only for a method carrying the attribute; without it the call lands on a null function.</summary>
    [Test]
    public async Task The_change_trampoline_Noesis_calls_natively_is_marked_for_the_browser_runtime()
    {
        var onChanged = typeof(DependencyWatcher)
            .Assembly.GetType("NoesisToolkit.Mvvm.CodeGen.NotifyingMetadata", throwOnError: true)!
            .GetMethod("OnChanged", BindingFlags.NonPublic | BindingFlags.Static)!;

        await Assert
            .That(onChanged.GetCustomAttributes().Select(a => a.GetType().Name))
            .Contains("MonoPInvokeCallbackAttribute");
    }
}
