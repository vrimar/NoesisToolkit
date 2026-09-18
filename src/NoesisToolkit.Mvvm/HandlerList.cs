using System;
using System.Collections.Generic;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Handlers run in registration order, where a handler may add or remove handlers, itself
/// included, while the list runs. Nothing is allocated per run.</summary>
sealed class HandlerList<THandler>
    where THandler : class
{
    readonly List<THandler?> _handlers = new List<THandler?>(4);
    int _running;
    int _removed;

    internal int Count { get; private set; }

    internal bool Contains(THandler handler) => _handlers.IndexOf(handler) >= 0;

    internal void Add(THandler handler)
    {
        _handlers.Add(handler);
        Count++;
    }

    internal bool Remove(THandler handler)
    {
        var index = _handlers.IndexOf(handler);
        if (index < 0)
            return false;

        Count--;
        if (_running > 0)
        {
            _handlers[index] = null;
            _removed++;
        }
        else
        {
            _handlers.RemoveAt(index);
        }

        return true;
    }

    /// <summary>The handlers present now; one added meanwhile waits for the next run, and one
    /// removed meanwhile does not run.</summary>
    internal Run Start() => new Run(this);

    internal ref struct Run
    {
        readonly HandlerList<THandler> _list;
        readonly int _count;
        int _index;

        internal Run(HandlerList<THandler> list)
        {
            _list = list;
            _count = list._handlers.Count;
            _index = 0;
            list._running++;
        }

        public bool Next(out THandler handler)
        {
            while (_index < _count)
            {
                if (_list._handlers[_index++] is { } present)
                {
                    handler = present;
                    return true;
                }
            }

            handler = null!;
            return false;
        }

        public void Dispose()
        {
            if (--_list._running == 0 && _list._removed > 0)
            {
                _list._handlers.RemoveAll(static handler => handler is null);
                _list._removed = 0;
            }
        }
    }
}
