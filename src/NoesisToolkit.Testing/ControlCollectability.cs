using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Noesis;

namespace NoesisToolkit.Testing;

/// <summary>
/// Adds every constructible control in an assembly to a live renderless view, removes it, and reports
/// the ones the heap still holds.
/// <para>
/// Noesis keeps handler delegates and native-held managed objects in static tables cleaned only when
/// the element's native object is destroyed — which cannot happen while one of those entries keeps
/// the managed proxy, and therefore the proxy's native reference, alive. A control that subscribes an
/// instance handler to itself or to a template child, or hands its own template a command that
/// captures it, is pinned for the process lifetime. <c>NTK2101</c> and <c>NTK2102</c> catch the
/// shapes a compiler can see; this catches the rest, including anything markup wired.
/// </para>
/// </summary>
public static class ControlCollectability
{
    static readonly Lazy<string?> Readiness = new(ProbeReadiness);

    /// <summary>
    /// Null when this environment can host the probe, otherwise why it cannot — a headless
    /// <c>GUI.Init</c> that throws, or a renderless view that does not drive Loaded/Unloaded. Call it
    /// first and skip the test on a reason rather than reading an empty sweep as a pass.
    /// </summary>
    public static string? Unavailable() => Readiness.Value;

    /// <summary>Exercises every constructible control the given assemblies define.</summary>
    /// <param name="assemblies">Assemblies to sweep; only public and internal concrete types with a parameterless constructor are exercised.</param>
    /// <param name="exclude">Types to leave out, for a control the host retains by design.</param>
    /// <param name="betweenRounds">Ran once per settle round, for a host that has to tick its own frame work to release what it holds.</param>
    /// <param name="settleRounds">How many collect-and-pump rounds a control gets before it counts as retained.</param>
    public static CollectabilityReport Probe(
        IEnumerable<Assembly> assemblies,
        IEnumerable<Type>? exclude = null,
        Action? betweenRounds = null,
        int settleRounds = 16
    )
    {
        if (assemblies is null)
            throw new ArgumentNullException(nameof(assemblies));

        if (settleRounds < 1)
            throw new ArgumentOutOfRangeException(nameof(settleRounds));

        // Constructing an element before GUI.Init takes the process down rather than throwing.
        if (Readiness.Value is { } reason)
            throw new InvalidOperationException(
                $"The renderless probe cannot run here: {reason}. Call Unavailable() first and skip "
                    + "the test on a reason."
            );

        var excluded = new HashSet<Type>(exclude ?? []);
        var leaked = new List<string>();
        var skipped = new List<string>();
        var verified = 0;

        foreach (var type in Controls(assemblies, excluded))
        {
            var (weak, view, error) = Exercise(type);
            if (error is not null)
            {
                skipped.Add($"{type.Name} ({error})");
                continue;
            }

            if (IsRetained(weak!, view!, betweenRounds, settleRounds))
                leaked.Add(type.FullName!);
            else
                verified++;
        }

        return new CollectabilityReport(leaked, skipped, verified);
    }

    static bool IsRetained(WeakReference weak, View view, Action? betweenRounds, int rounds)
    {
        var time = 10.0;
        for (var i = 0; i < rounds; i++)
        {
            betweenRounds?.Invoke();
            Collect();
            if (!weak.IsAlive)
                return false;
            view.Update(time += 0.016);
        }

        GC.KeepAlive(view);
        return weak.IsAlive;
    }

    // Kept in its own frame so a caller's stack slot cannot hold the element across the collection.
    static (WeakReference? Weak, View? View, string? Error) Exercise(Type type)
    {
        object instance;
        try
        {
            instance = Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return (null, null, inner.GetType().Name);
        }

        var element = (FrameworkElement)instance;
        var weak = new WeakReference(element);
        var panel = new StackPanel();
        var view = GUI.CreateView(panel);
        view.SetSize(400, 300);

        var time = 0.0;
        panel.Children.Add(element);
        view.Update(time += 0.016);
        panel.Children.Remove(element);
        view.Update(time += 0.016);

        element = null;
        instance = null!;
        return (weak, view, null);
    }

    static IEnumerable<Type> Controls(IEnumerable<Assembly> assemblies, HashSet<Type> excluded)
    {
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (
                    type is null
                    || type.IsAbstract
                    || type.IsGenericTypeDefinition
                    || (type.FullName ?? "").Contains('<')
                    || !typeof(FrameworkElement).IsAssignableFrom(type)
                    || type.GetConstructor(Type.EmptyTypes) is null
                    || excluded.Contains(type)
                )
                    continue;

                yield return type;
            }
        }
    }

    // The second collect reclaims what a finalizer resurrected during the first.
    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static string? ProbeReadiness()
    {
        try
        {
            GUI.Init();
        }
        catch (Exception ex)
        {
            return $"GUI.Init is unavailable here ({ex.GetType().Name}: {ex.Message})";
        }

        try
        {
            var panel = new StackPanel();
            var view = GUI.CreateView(panel);
            view.SetSize(400, 300);

            var probe = new LifecycleProbe();
            var time = 0.0;

            panel.Children.Add(probe);
            view.Update(time += 0.016);
            view.Update(time += 0.016);
            panel.Children.Remove(probe);
            view.Update(time += 0.016);
            view.Update(time += 0.016);
            GC.KeepAlive(view);

            return probe.DidLoad && probe.DidUnload
                ? null
                : "a renderless GUI.CreateView does not drive Loaded/Unloaded here";
        }
        catch (Exception ex)
        {
            return $"the renderless view probe threw ({ex.GetType().Name}: {ex.Message})";
        }
    }

    sealed class LifecycleProbe : ContentControl
    {
        public bool DidLoad;
        public bool DidUnload;

        public LifecycleProbe()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        static void OnLoaded(object sender, RoutedEventArgs e) =>
            ((LifecycleProbe)sender).DidLoad = true;

        static void OnUnloaded(object sender, RoutedEventArgs e) =>
            ((LifecycleProbe)sender).DidUnload = true;
    }
}
