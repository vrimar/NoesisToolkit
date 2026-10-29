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

    // Register refuses a managed property of these with a default, and without one stores it as a pointer.
    internal static readonly string[] Unregistrable =
    {
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

    internal static bool IsUnregistrable(string fullName) =>
        Array.IndexOf(Unregistrable, fullName) >= 0;

    // An x:Static the parser loads unset, logging nothing: every member of a type, or one member.
    internal static readonly string[] UnreadStatics =
    {
        "Noesis.BeforeSetAction",
        "Noesis.BlendMode",
        "Noesis.CaptureMode",
        "Noesis.ClockState.Finished",
        "Noesis.ClockState.Paused",
        "Noesis.CursorType",
        "Noesis.DispatcherPriority",
        "Noesis.DragAction",
        "Noesis.DragDropEffects",
        "Noesis.DragDropKeyStates",
        "Noesis.FocusNavigationDirection",
        "Noesis.FrameworkPropertyMetadataOptions",
        "Noesis.GeneratorDirection",
        "Noesis.GeneratorStatus",
        "Noesis.GridUnitType",
        "Noesis.HitTestFilterBehavior",
        "Noesis.HitTestResultBehavior",
        "Noesis.HoldingState",
        "Noesis.Key",
        "Noesis.KeyStates",
        "Noesis.KeyTimeType",
        "Noesis.LogLevel",
        "Noesis.MinMagFilter",
        "Noesis.MipFilter",
        "Noesis.MouseButton",
        "Noesis.MouseButtonState",
        "Noesis.PlatformID",
        "Noesis.PropertyAccess",
        "Noesis.RenderFlags",
        "Noesis.RiveSourceInputType",
        "Noesis.RoutingStrategy",
        "Noesis.ScrollEventType",
        "Noesis.Slider",
        "Noesis.StencilMode",
        "Noesis.TextureFormat",
        "Noesis.WrapMode",
        "Noesis.XamlDependencyType",
    };

    // Members of a type listed unread that the parser reads all the same.
    internal static readonly string[] ReadStatics = { "Noesis.Key.None" };

    internal static bool ParserReadsStatic(
        string owner,
        bool ownerIsEnum,
        string? ownerAssembly,
        string member,
        bool memberIsCommand
    )
    {
        var full = owner + "." + member;
        if (Array.IndexOf(ReadStatics, full) >= 0)
            return true;

        if (Array.IndexOf(UnreadStatics, owner) >= 0 || Array.IndexOf(UnreadStatics, full) >= 0)
            return false;

        return ownerIsEnum
            || ownerAssembly == "Noesis.GUI"
                && (memberIsCommand || member.EndsWith("StyleKey", StringComparison.Ordinal));
    }

    // Owners whose commands the parser reads as text only without the member's "Command" suffix.
    internal static readonly string[] SuffixlessCommandOwners = { "Noesis.Slider" };

    /// <summary>Whether the parser reads <c>Command="Owner.Written"</c>, which names the property
    /// <paramref name="member"/>: only a Noesis command, by the name it registers natively.</summary>
    internal static bool ParserReadsCommandText(
        string owner,
        string? ownerAssembly,
        string written,
        string member,
        bool memberIsCommand
    ) =>
        ownerAssembly == "Noesis.GUI"
        && memberIsCommand
        && (
            Array.IndexOf(SuffixlessCommandOwners, owner) >= 0
                ? written + "Command" == member
                : written == member
        );
}
