namespace NoesisToolkit.Xaml;

/// <summary>
/// The recursive dictionary lookup both emitters need. It is emitted twice — once into
/// <c>XamlResources</c> and once as a local function per document — so the body lives here rather
/// than being written out in two places that could drift apart.
/// </summary>
static class XamlLookupHelper
{
    public static string Emit(string name) =>
        $"static object {name}(global::Noesis.ResourceDictionary d, object k)\n"
        + "{\n"
        + "    if (d == null) return null;\n"
        + "    if (d.Contains(k)) return d[k];\n"
        + $"    foreach (var m in d.MergedDictionaries) {{ var v = {name}(m, k); if (v != null) return v; }}\n"
        + "    return null;\n"
        + "}";
}
