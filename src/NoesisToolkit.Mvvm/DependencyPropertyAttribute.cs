using System;
using Noesis;

namespace NoesisToolkit.Mvvm;

/// <summary>
/// Marks a partial property as a dependency property. The generator emits the
/// <c>{Name}Property</c> registration and the accessor bodies into the containing partial type.
/// </summary>
/// <remarks>
/// A <see langword="static"/> partial property registers as an attached property instead, and gains
/// generated <c>Get{Name}</c> / <c>Set{Name}</c> accessors.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class DependencyPropertyAttribute : Attribute
{
    /// <summary>Registers with default metadata.</summary>
    public DependencyPropertyAttribute() { }

    /// <summary>Registers with the given metadata.</summary>
    /// <param name="defaultValue">The property's default value, or <see langword="null"/> for none.</param>
    /// <param name="propertyChanged">
    /// Name of a static change callback on the owning type, matching Noesis' <c>PropertyChangedCallback</c>.
    /// </param>
    /// <param name="metadataOptions">Framework metadata flags, such as layout or render invalidation.</param>
    public DependencyPropertyAttribute(
        object? defaultValue,
        string? propertyChanged = null,
        FrameworkPropertyMetadataOptions metadataOptions = FrameworkPropertyMetadataOptions.None
    )
    {
        DefaultValue = defaultValue;
        PropertyChanged = propertyChanged;
        MetadataOptions = metadataOptions;
    }

    /// <summary>Name of the static change callback, if one was given.</summary>
    public string? PropertyChanged { get; }

    /// <summary>The default value, if one was given.</summary>
    public object? DefaultValue { get; }

    /// <summary>The framework metadata flags.</summary>
    public FrameworkPropertyMetadataOptions MetadataOptions { get; }
}
