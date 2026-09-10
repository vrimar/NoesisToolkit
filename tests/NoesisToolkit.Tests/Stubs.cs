namespace NoesisToolkit.Tests;

/// <summary>The stub sources the harnesses compile against, read once for the whole run.</summary>
static class Stubs
{
    public static readonly string Mvvm = Read("MvvmStub.cs.txt");

    public static readonly string[] Sample = [Read("SampleStub.cs.txt")];

    static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
}
