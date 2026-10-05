using System.Collections;
using System.Collections.Generic;

namespace NoesisToolkit.Xaml;

sealed class JournaledSet<T> : IEnumerable<T>
{
    readonly List<T> _order = new List<T>();

    readonly HashSet<T> _members = new HashSet<T>();

    public int Count => _order.Count;

    public bool Add(T item)
    {
        if (!_members.Add(item))
            return false;

        _order.Add(item);
        return true;
    }

    public bool Contains(T item) => _members.Contains(item);

    public void Truncate(int count)
    {
        for (var i = _order.Count - 1; i >= count; i--)
            _members.Remove(_order[i]);

        _order.RemoveRange(count, _order.Count - count);
    }

    public List<T>.Enumerator GetEnumerator() => _order.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
