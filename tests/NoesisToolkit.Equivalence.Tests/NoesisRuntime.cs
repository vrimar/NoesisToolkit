using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// Noesis is a process-global singleton.
internal static class NoesisRuntime
{
    static bool _started;

    internal static void Start()
    {
        if (_started)
            return;

        _started = true;
        GUI.Init();
        GUI.SetXamlProvider(new XamlGraphDump.FileXamlProvider(XamlGraphDump.ProviderRoot()));

        // A cross-file StaticResource resolves for neither side until a graph is installed, and the
        // root has to be rooted: every uri the parser resolves from here inherits that form.
        GUI.LoadApplicationResources("/Fixtures;Fixtures/App.xaml");
    }

    // A template clone is wired a pass later than its host, so six frames leave it unsettled.
    const int Frames = 8;

    internal static View Show(params FrameworkElement[] elements) =>
        Show(new StackPanel { Width = 400, Height = 300 }, elements);

    /// <summary>Puts <paramref name="elements"/> in a live view and settles it.</summary>
    internal static View Show(Panel root, params FrameworkElement[] elements)
    {
        Start();

        foreach (var element in elements)
            root.Children.Add(element);

        var view = GUI.CreateView(root);
        view.SetSize((int)root.Width, (int)root.Height);
        Pump(view, root);
        return view;
    }

    internal static void Pump(View view, params FrameworkElement[] elements)
    {
        for (var i = 0; i < Frames; i++)
            view.Update(i * 0.016);

        foreach (var element in elements)
            element.UpdateLayout();
    }
}
