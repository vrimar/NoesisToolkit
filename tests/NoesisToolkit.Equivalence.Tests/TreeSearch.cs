using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

/// <summary>Walking the visual tree, which is the only chain that reaches out of a template's
/// content: the logical parent stops at the template's own root.</summary>
internal static class TreeSearch
{
    internal static List<T> All<T>(DependencyObject node)
        where T : DependencyObject
    {
        var found = new List<T>();
        Walk(node, found);
        return found;

        static void Walk(DependencyObject current, List<T> into)
        {
            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T hit)
                    into.Add(hit);

                Walk(child, into);
            }
        }
    }

    internal static T? Named<T>(DependencyObject node, string name)
        where T : FrameworkElement => All<T>(node).FirstOrDefault(e => e.Name == name);

    internal static T? Ancestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        for (
            var current = VisualTreeHelper.GetParent(start);
            current is not null;
            current = VisualTreeHelper.GetParent(current)
        )
        {
            if (current is T hit)
                return hit;
        }

        return null;
    }
}
