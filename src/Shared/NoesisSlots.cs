using System;

namespace NoesisToolkit.CodeGen;

static class NoesisSlots
{
    // Underlying names: GetValue and SetValue reach these through a typed native, nullable or not.
    internal static readonly string[] Typed =
    {
        "System.Boolean",
        "System.Byte",
        "System.Char",
        "System.Decimal",
        "System.Double",
        "System.Int16",
        "System.Int32",
        "System.Int64",
        "System.SByte",
        "System.Single",
        "System.String",
        "System.TimeSpan",
        "System.Type",
        "System.UInt16",
        "System.UInt32",
        "System.UInt64",
        "System.Uri",
        "Noesis.Color",
        "Noesis.CornerRadius",
        "Noesis.Duration",
        "Noesis.Int32Rect",
        "Noesis.KeyTime",
        "Noesis.Point",
        "Noesis.Rect",
        "Noesis.Size",
        "Noesis.Thickness",
    };

    // GetValue and SetValue take these for a component pointer: a read crashes, a write corrupts.
    internal static readonly string[] Misrouted =
    {
        "Noesis.GridLength",
        "Noesis.Matrix",
        "Noesis.Matrix3D",
        "Noesis.Matrix4",
        "Noesis.VirtualizationCacheLength",
    };

    internal static readonly (string Owner, string Name)[] UIntRegistered =
    {
        ("Noesis.PasswordBox", "PasswordChar"),
        ("Noesis.PasswordBox", "ShowLastCharacterDuration"),
        ("Noesis.UniformGrid", "Columns"),
        ("Noesis.UniformGrid", "FirstColumn"),
        ("Noesis.UniformGrid", "Rows"),
    };

    internal static readonly (string Owner, string Name)[] Unregistered =
    {
        ("Noesis.ArcSegment", "SweepDirection"),
        ("Noesis.FrameworkElement", "BlendingMode"),
        ("Noesis.FrameworkElement", "InputScope"),
        ("Noesis.GridSplitter", "ResizeBehavior"),
        ("Noesis.GridSplitter", "ResizeDirection"),
        ("Noesis.Interactivity.ConditionalExpression", "ForwardChaining"),
        ("Noesis.Timeline", "RepeatBehavior"),
        ("Noesis.ToolTipService", "ScaleReferenceElement"),
    };

    internal static bool IsTyped(string fullName) => Array.IndexOf(Typed, fullName) >= 0;

    internal static bool IsMisrouted(string fullName) => Array.IndexOf(Misrouted, fullName) >= 0;
}
