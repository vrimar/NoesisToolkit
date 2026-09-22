using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Metadata whose change callback reaches the toolkit with one reused args object per nesting
/// depth, where Noesis' trampoline mints a finalizable one per change.</summary>
sealed class NotifyingMetadata : FrameworkPropertyMetadata
{
    static readonly Dictionary<long, PropertyChangedCallback?> Inner =
        new Dictionary<long, PropertyChangedCallback?>();

    static readonly List<DependencyPropertyChangedEventArgs> Args =
        new List<DependencyPropertyChangedEventArgs>();

    static int _depth;

    // Held: collected, Noesis would call freed code.
    static readonly ManagedPropertyChangedCallback Trampoline = OnChanged;

    internal NotifyingMetadata(
        object? defaultValue,
        FrameworkPropertyMetadataOptions options,
        PropertyChangedCallback? inner
    )
        : base(defaultValue, options)
    {
        lock (Inner)
            Inner[(long)swigCPtr.Handle] = inner;

        Bind(null, swigCPtr, Trampoline);
    }

    static void OnChanged(nint cPtr, nint d, nint e)
    {
        try
        {
            PropertyChangedCallback? inner;
            lock (Inner)
            {
                if (!Inner.TryGetValue((long)cPtr, out inner))
                    return;

                // Noesis reports the metadata's end as a change with neither object nor args.
                if (d == IntPtr.Zero && e == IntPtr.Zero)
                {
                    Inner.Remove((long)cPtr);
                    return;
                }
            }

            if (
                !NoesisInternals.Initialized(null)
                || NoesisInternals.Proxy(null, d, false) is not DependencyObject target
            )
                return;

            var args = Borrow(e);
            try
            {
                inner?.Invoke(target, args);
                DependencyWatcher.OnChanged(target, args);
            }
            finally
            {
                Return(args);
            }
        }
        catch (Exception exception)
        {
            Error.UnhandledException(exception);
        }
    }

    // A callback that sets a property re-enters before returning.
    static DependencyPropertyChangedEventArgs Borrow(nint e)
    {
        if (_depth == Args.Count)
        {
            var fresh = Create(IntPtr.Zero, false);
            GC.SuppressFinalize(fresh);
            Args.Add(fresh);
        }

        var args = Args[_depth++];
        Pointer(args) = new HandleRef(args, e);
        return args;
    }

    // A handler that kept the args reads null, not a freed native object.
    static void Return(DependencyPropertyChangedEventArgs args)
    {
        Pointer(args) = new HandleRef(args, IntPtr.Zero);
        _depth--;
    }

    [UnsafeAccessor(
        UnsafeAccessorKind.StaticMethod,
        Name = "Noesis_PropertyMetadata_BindPropertyChangedCallback"
    )]
    static extern void Bind(
        PropertyMetadata? owner,
        HandleRef cPtr,
        ManagedPropertyChangedCallback callback
    );

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern DependencyPropertyChangedEventArgs Create(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Pointer(DependencyPropertyChangedEventArgs args);
}
