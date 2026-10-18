using Noesis;
using Path = System.IO.Path;

namespace NoesisToolkit.Equivalence.Tests;

internal static class FixtureFiles
{
    /// <summary>The fixture corpus, copied next to the test binary.</summary>
    internal static string FixtureRoot() => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>What a logical name is resolved against: paths are project-relative, so Fixtures/ is part of them.</summary>
    internal static string ProviderRoot() => AppContext.BaseDirectory;

    /// <summary>Serves the fixture corpus, so a relative or pack-style Source= resolves off disk.</summary>
    internal sealed class FileXamlProvider(string root) : XamlProvider
    {
        public override Stream? LoadXaml(Uri uri)
        {
            var path = uri.ToString().Replace('\\', '/').TrimStart('/');
            var semi = path.IndexOf(';');
            var relative = semi < 0 ? path : path.Substring(semi + 1);

            var full = Path.Combine(root, relative);
            return File.Exists(full) ? File.OpenRead(full) : null;
        }
    }
}
