using System.ComponentModel;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The revision of the API compiled XAML and the generators emit against. The XAML
/// compiler emits compiled bindings only against a runtime of its own revision.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RuntimeContract
{
    /// <summary>The revision this runtime implements.</summary>
    public const int Version = RuntimeNames.ContractVersion;
}
