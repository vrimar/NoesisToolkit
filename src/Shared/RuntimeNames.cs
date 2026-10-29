namespace NoesisToolkit.CodeGen;

static class RuntimeNames
{
    internal const int ContractVersion = 1;

    const string Mvvm = "NoesisToolkit.Mvvm.";

    const string CodeGen = Mvvm + "CodeGen.";

    internal static class RuntimeContract
    {
        internal const string Type = CodeGen + "RuntimeContract";
        internal const string Version = "Version";
    }

    internal static class CompiledBinding
    {
        internal const string Type = CodeGen + "CompiledBinding";
        internal const string Fqn = "global::" + Type;
        internal const string Bind = Fqn + ".Bind";
        internal const string NewBuild = Fqn + ".NewBuild";
        internal const string MarkReceiver = Fqn + ".MarkReceiver";
        internal const string MarkedBehavior = Fqn + ".MarkedBehavior";
        internal const string MarkedInputBinding = Fqn + ".MarkedInputBinding";
        internal const string MarkedAction = Fqn + ".MarkedAction";
        internal const string FindAncestor = Fqn + ".FindAncestor";
        internal const string FindAncestorOrSelf = Fqn + ".FindAncestorOrSelf";
        internal const string FindNamed = Fqn + ".FindNamed";
        internal const string TemplatedParent = Fqn + ".TemplatedParent";
    }

    internal static class BindingHop
    {
        internal const string Type = CodeGen + "BindingHop";
        internal const string Fqn = "global::" + Type;
        internal const string Bool = Fqn + ".Bool";
        internal const string Value = Fqn + ".Value";
        internal const string Checked = Fqn + ".Checked";
        internal const string CheckedBool = Fqn + ".CheckedBool";
        internal const string CheckedValue = Fqn + ".CheckedValue";
        internal const string Guarded = Fqn + ".Guarded";
        internal const string GuardedBool = Fqn + ".GuardedBool";
        internal const string GuardedValue = Fqn + ".GuardedValue";
    }

    internal static class SourceRoot
    {
        internal const string Type = CodeGen + "SourceRoot";
        internal const string Fqn = "global::" + Type;
        internal const string Target = Fqn + ".Target";
        internal const string TemplatedParent = Fqn + ".TemplatedParent";
    }

    internal static class CompiledBindingSetup
    {
        internal const string Type = CodeGen + "CompiledBindingSetup";
        internal const string Fqn = "global::" + Type;
        internal const string Register = Fqn + ".Register";
        internal const string SetIndex = Fqn + ".SetIndex";
    }

    internal static class CompiledBindingPart
    {
        internal const string Type = CodeGen + "CompiledBindingPart";
        internal const string Fqn = "global::" + Type;
        internal const string Source = "Source";
        internal const string Root = "Root";
        internal const string SourceProperty = "SourceProperty";
        internal const string Hops = "Hops";
    }

    internal static class CompiledBindingSpec
    {
        internal const string Type = CodeGen + "CompiledBindingSpec";
        internal const string Fqn = "global::" + Type;
        internal const string Source = CompiledBindingPart.Source;
        internal const string Root = CompiledBindingPart.Root;
        internal const string SourceProperty = CompiledBindingPart.SourceProperty;
        internal const string Hops = CompiledBindingPart.Hops;
        internal const string Convert = "Convert";
        internal const string Assign = "Assign";
        internal const string Write = "Write";
        internal const string Lane = "Lane";
        internal const string Converter = "Converter";
        internal const string ConverterParameter = "ConverterParameter";
        internal const string TargetType = "TargetType";
        internal const string SourceType = "SourceType";
        internal const string Mode = "Mode";
        internal const string Trigger = "Trigger";
    }

    internal static class CompiledMultiBinding
    {
        internal const string Type = CodeGen + "CompiledMultiBinding";
        internal const string Fqn = "global::" + Type;
        internal const string Bind = Fqn + ".Bind";
    }

    internal static class CompiledMultiBindingSpec
    {
        internal const string Type = CodeGen + "CompiledMultiBindingSpec";
        internal const string Fqn = "global::" + Type;
        internal const string Parts = "Parts";
        internal const string Converter = "Converter";
        internal const string ConverterParameter = "ConverterParameter";
        internal const string TargetType = "TargetType";
        internal const string Format = "Format";
        internal const string Convert = "Convert";
        internal const string Assign = "Assign";
    }

    internal static class BindingLane
    {
        internal const string Type = CodeGen + "BindingLane";
        internal const string Fqn = "global::" + Type;
        internal const string Of = Fqn + ".Of";
        internal const string Text = Fqn + ".Text";
    }

    internal static class SlotText
    {
        internal const string Type = CodeGen + "SlotText";
        internal const string Fqn = "global::" + Type;
        internal const string Literal = "Literal";
        internal const string Integer = "Integer";
        internal const string Number = "Number";
        internal const string Fixed = "Fixed";
        internal const string Done = "Done";
    }

    internal static class SlotConversion
    {
        internal const string Type = CodeGen + "SlotConversion";
        internal const string Fqn = "global::" + Type;
        internal const string Text = Fqn + ".Text";
        internal const string Unconverted = Fqn + ".Unconverted";
        internal const string Cached = Fqn + ".Cached";
        internal const string Back = Fqn + ".Back";
    }

    internal static class ImageSources
    {
        internal const string Type = CodeGen + "ImageSources";
        internal const string Fqn = "global::" + Type;
        internal const string From = Fqn + ".From";
    }

    internal static class CompiledTriggerSet
    {
        internal const string Type = CodeGen + "CompiledTriggerSet";
        internal const string Fqn = "global::" + Type;
        internal const string Bind = Fqn + ".Bind";
    }

    internal static class CompiledTriggerSpec
    {
        internal const string Type = CodeGen + "CompiledTriggerSpec";
        internal const string Fqn = "global::" + Type;
        internal const string Conditions = "Conditions";
        internal const string Setters = "Setters";
        internal const string TemplateScoped = "TemplateScoped";
        internal const string PropertyDriven = "PropertyDriven";
    }

    internal static class CompiledTriggerCondition
    {
        internal const string Type = CodeGen + "CompiledTriggerCondition";
        internal const string Fqn = "global::" + Type;
        internal const string Part = "Part";
        internal const string Path = "Path";
        internal const string Value = "Value";
    }

    internal static class CompiledSetter
    {
        internal const string Type = CodeGen + "CompiledSetter";
        internal const string Fqn = "global::" + Type;
        internal const string Property = "Property";
        internal const string Value = "Value";
        internal const string Assign = "Assign";
        internal const string TargetName = "TargetName";
    }

    internal static class DependencyWatcher
    {
        internal const string Type = CodeGen + "DependencyWatcher";
        internal const string Fqn = "global::" + Type;
        internal const string SelfNotifying = Fqn + ".SelfNotifying";
        internal const string Metadata = Fqn + ".Metadata";
    }

    internal static class DependencyRead
    {
        internal const string Type = CodeGen + "DependencyRead";
        internal const string Fqn = "global::" + Type;
        internal const string Value = Fqn + ".Value";
        internal const string String = Fqn + ".String";
        internal const string Bool = Fqn + ".Bool";
        internal const string Int = Fqn + ".Int";
        internal const string Long = Fqn + ".Long";
        internal const string Float = Fqn + ".Float";
        internal const string Double = Fqn + ".Double";
        internal const string Enum = Fqn + ".Enum";
        internal const string Thickness = Fqn + ".Thickness";
        internal const string CornerRadius = Fqn + ".CornerRadius";
        internal const string Point = Fqn + ".Point";
        internal const string Size = Fqn + ".Size";
        internal const string Color = Fqn + ".Color";
        internal const string Capturing = Fqn + ".Capturing";
    }

    internal static class DependencyWrite
    {
        internal const string Type = CodeGen + "DependencyWrite";
        internal const string Fqn = "global::" + Type;
        internal const string Value = Fqn + ".Value";
    }

    internal static class ElementHandle
    {
        internal const string Type = Mvvm + "ElementHandle";
        internal const string Fqn = "global::" + Type;
    }

    internal static class DelegateCommand
    {
        internal const string Type = Mvvm + "DelegateCommand";
        internal const string Generic = Type + "`1";
        internal const string Fqn = "global::" + Type;
    }

    internal static class AsyncDelegateCommand
    {
        internal const string Type = Mvvm + "AsyncDelegateCommand";
        internal const string Generic = Type + "`1";
        internal const string Fqn = "global::" + Type;
    }

    internal static class DelegateCommandAttribute
    {
        internal const string Type = Mvvm + "DelegateCommandAttribute";
    }

    internal static class DependencyPropertyAttribute
    {
        internal const string Type = Mvvm + "DependencyPropertyAttribute";

        internal static class Ctor
        {
            internal const string DefaultValue = "defaultValue";
            internal const string PropertyChanged = "propertyChanged";
            internal const string MetadataOptions = "metadataOptions";
        }
    }
}
