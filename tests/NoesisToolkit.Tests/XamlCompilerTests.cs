using Microsoft.CodeAnalysis;
using NoesisToolkit.Mvvm.Generators;
using NoesisToolkit.Xaml;

namespace NoesisToolkit.Tests;

public partial class XamlCompilerTests
{
    static GeneratorRun CompiledBindings() => CompiledBindingsRun.Value;

    static GeneratorRun Theme() => ThemeRun.Value;

    static GeneratorRun Shell() => ShellRun.Value;

    static GeneratorRun Templated() => TemplatedRun.Value;

    static string Host(string name) =>
        $$"""
            namespace Sample;

            public partial class {{name}} : global::Noesis.UserControl
            {
                public {{name}}() => InitializeComponent();
            }
            """;

    static GeneratorRun Run(
        IEnumerable<string> fixtures,
        IEnumerable<string>? sources = null,
        IDictionary<string, string>? properties = null
    ) =>
        GeneratorHarness.Run(
            new XamlCompileGenerator(),
            fixtures,
            sources,
            properties
                ?? new Dictionary<string, string> { ["build_property.ProjectDir"] = "/repo/App" }
        );

    [Test]
    public async Task Dictionary_compiles_to_binding_csharp()
    {
        var run = Theme();

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.AllSources).Contains("XamlRegistry");
        await Assert.That(run.AllSources).Contains("global::Noesis.ResourceDictionary Build()");
    }

    [Test]
    public async Task Implicit_style_keys_a_native_type_by_bare_name()
    {
        var run = Theme();

        await Assert.That(run.Errors).IsEmpty();
        // Getting this wrong makes the style silently never apply.
        await Assert.That(run.Source("Theme")).Contains("__dict0.Add(\"Button\"");
    }

    [Test]
    public async Task Implicit_style_keys_a_managed_type_by_Type()
    {
        var run = Run(["ManagedStyle.xaml"], [ManagedControl]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("ManagedStyle");
        await Assert.That(source).Contains("Add(typeof(global::Sample.MyControl)");
        await Assert.That(source).Contains("Add(\"Button\"");
        await Assert.That(source).DoesNotContain("Add(typeof(global::Noesis.Button)");
    }

    [Test]
    public async Task A_dictionary_reads_a_key_where_it_is_referenced_and_defers_only_a_miss()
    {
        var run = Theme();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("Theme");

        var scoped = source.IndexOf(
            "__scoped(new global::Noesis.ResourceDictionary[] { __dict0 }, \"BaseButton\")",
            StringComparison.Ordinal
        );
        var defer = source.IndexOf("__XamlResources.Defer(__root =>", StringComparison.Ordinal);
        var outer = source.IndexOf(
            "__outer(__root, __dict0, \"BaseButton\", false)",
            StringComparison.Ordinal
        );
        await Assert.That(scoped).IsGreaterThan(-1);
        await Assert.That(defer).IsGreaterThan(scoped);
        await Assert.That(outer).IsGreaterThan(defer);
    }

    [Test]
    public async Task A_root_resolves_its_own_resources_inline()
    {
        var run = Run(["RootResources.xaml"], [Host("RootHost")]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("RootResources");

        await Assert.That(source).Contains(".BasedOn =");
        await Assert.That(source).DoesNotContain("__XamlResources.Defer(__root =>");
    }

    [Test]
    public async Task Resources_may_wrap_their_entries_in_an_explicit_dictionary()
    {
        var run = Run(["WrappedResources.xaml"], [Host("WrappedHost")]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        await Assert.That(source).Contains("MergedDictionaries.Add(");
        await Assert.That(source).Contains(".Add(\"Accent\"");
        await Assert.That(source).Contains(".Resources = ");
    }

    [Test]
    public async Task An_opted_in_root_compiles_the_bindings_it_can_resolve()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert.That(source).Contains("CompiledBinding.Bind(");
        await Assert.That(source).Contains("__o => ((global::Sample.Ui.ItemViewModel)__o).Def");
        await Assert.That(source).Contains("__o => ((global::Sample.Ui.ItemDef)__o).Family");
        await Assert.That(source).Contains("Converter = (global::Noesis.IValueConverter)");
    }

    [Test]
    public async Task An_identity_binding_reads_the_data_context_itself()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.AllSources)
            .Contains("Hops = new global::NoesisToolkit.Mvvm.CodeGen.BindingHop[] {  }");

        // The one in the untyped template: an object slot needs no declared context.
        await Assert
            .That(run.AllSources)
            .Contains("Bind(__e, global::Noesis.FrameworkElement.TagProperty");
    }

    [Test]
    public async Task A_source_the_slot_cannot_hold_is_converted_rather_than_refused()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert
            .That(source)
            .Contains(
                "Convert = __v => __v is null ? null : (object)((int)__v)"
                    + ".ToString(global::System.Globalization.CultureInfo.InvariantCulture)"
            );
        await Assert
            .That(source)
            .Contains("Convert = __v => __v is double __t ? (object)(float)__t");
    }

    [Test]
    public async Task A_slot_the_native_binding_fills_through_a_type_converter_is_constructed()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert
            .That(source)
            .Contains(
                "Convert = __v => __v is int __t ? (object)new global::Noesis.CornerRadius((float)__t)"
            );
        await Assert
            .That(source)
            .Contains(
                "Convert = __v => __v is string __t"
                    + " ? new global::Noesis.BitmapImage(new global::System.Uri(__t,"
                    + " global::System.UriKind.RelativeOrAbsolute)) : null"
            );
    }

    [Test]
    public async Task A_binding_on_an_attached_object_anchors_to_its_host_element()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert.That(source).Contains("CompiledBinding.MarkedBehavior(__a, ");
        await Assert.That(source).Contains("CompiledBinding.MarkedAction(__a, ");
        await Assert
            .That(source)
            .Contains(
                "Bind(this, __a => global::NoesisToolkit.Mvvm.CodeGen.CompiledBinding.MarkedInputBinding(__a, "
            );
        await Assert.That(source).Contains("CompiledBinding.MarkReceiver(");
        await Assert.That(source).Contains("global::Sample.Ui.PokeBehavior.TextProperty");
        await Assert.That(source).Contains("global::Sample.Ui.PokeAction.ParameterProperty");
    }

    [Test]
    public async Task A_resolvable_style_trigger_compiles_and_an_unresolvable_one_stays_native()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert.That(source).Contains("CompiledTriggerSet.Bind(");
        await Assert
            .That(source)
            .Contains(
                "new global::NoesisToolkit.Mvvm.CodeGen.BindingHop(\"Ticked\", __o => ((global::Sample.Ui.ItemViewModel)__o).Ticked)"
            );
        await Assert.That(source).Contains("Value = true");
        await Assert.That(source).Contains("Value = 3");
        await Assert
            .That(source)
            .Contains("Setters = new global::NoesisToolkit.Mvvm.CodeGen.CompiledSetter[]");

        // The trigger reading a member the context does not have keeps its native form.
        await Assert.That(source).Contains("new global::Noesis.Binding(\"Missing\")");
        await Assert.That(source).Contains("new global::Noesis.DataTrigger()");

        // The template-level trigger writes a named child.
        await Assert.That(source).Contains("TargetName = \"Peer\"");
    }

    [Test]
    public async Task A_binding_written_as_an_element_compiles_like_the_markup_form()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.AllSources).Contains("{0} held");
    }

    [Test]
    public async Task A_multi_binding_compiles_its_parts_and_an_unresolvable_child_keeps_it_native()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert.That(source).Contains("CompiledMultiBinding.Bind(");
        await Assert
            .That(source)
            .Contains(
                "Format = __vs => string.Format(global::System.Globalization.CultureInfo"
                    + ".InvariantCulture, \"{0}/{1}\", __vs[0] is null ? (object)\"\" : __vs[0],"
                    + " __vs[1] is null ? (object)\"\" : ((int)__vs[1])"
                    + ".ToString(global::System.Globalization.CultureInfo.InvariantCulture))"
            );
        await Assert.That(source).Contains("Converter = (global::Noesis.IMultiValueConverter)");
        await Assert.That(source).Contains("Converter = new global::Sample.Ui.JoinConverter()");

        // The one whose only child reads a member the context does not have.
        await Assert.That(source).Contains("new global::Noesis.MultiBinding()");
    }

    [Test]
    public async Task A_native_multi_bindings_enum_knob_has_to_name_a_constant()
    {
        var run = Run(
            ["MultiBindingKnobs.xaml"],
            [Host("MultiBindingKnobsHost"), SampleUi, Stubs.Mvvm]
        );

        var messages = run.GeneratorDiagnostics.Select(d => d.GetMessage()).ToList();
        await Assert
            .That(messages.Any(m => m.Contains("Binding.Mode must name a constant, not 'One Way'")))
            .IsTrue();
        await Assert.That(run.AllSources).DoesNotContain("BindingMode.One Way");
    }

    [Test]
    public async Task A_binding_it_cannot_resolve_stays_a_native_binding()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert.That(source).Contains("new global::Noesis.Binding(\"Nope\")");

        // The nested template hands its content a different DataContext, and says nothing about the
        // type ItemViewModel also has a Family, so inheriting it would compile a wrong binding.
        await Assert.That(source).Contains("new global::Noesis.Binding(\"Family\")");

        // Neither mode is implemented, and a read-only path cannot honour the write-back TwoWay asks for.
        await Assert.That(source).Contains("new global::Noesis.Binding(\"Def.Family\")");
        await Assert.That(source).Contains("new global::Noesis.Binding(\"ReadOnlyName\")");
    }

    [Test]
    public async Task An_element_source_roots_the_path_at_that_element()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert.That(source).Contains("__s => this._Sibling");
        await Assert.That(source).Contains("__s => __s.FindName(\"Peer\")");

        // A name the document declared outside the template is reached by walking past the clone's
        // own scope the root's own field would be the prototype's, shared by every clone.
        await Assert.That(source).Contains("CompiledBinding.FindNamed(__s, \"Outer\")");
        await Assert.That(source).DoesNotContain("__s => this._Outer");
        await Assert
            .That(source)
            .Contains("CompiledBinding.FindAncestor(__s, typeof(global::Noesis.ItemsControl))");

        // Which element the walk lands on is derived from the tree, not declared, so it is checked.
        await Assert
            .That(source)
            .Contains("BindingHop.Guarded<global::Sample.Ui.ItemViewModel>(\"Family\"");
    }

    [Test]
    public async Task An_element_source_it_cannot_reach_stays_a_native_binding()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        // Only a dependency property of an element can be watched.
        await Assert.That(source).Contains("new global::Noesis.Binding(\"Name\")");

        // Nothing in the document is a Border, so the walk has no element to resolve against.
        await Assert
            .That(source)
            .DoesNotContain("FindAncestor(__s, typeof(global::Noesis.Border))");

        // Compiled, the write would feed the next read its own result.
        await Assert.That(source).Contains("new global::Noesis.Binding(\"Def\")");
    }

    [Test]
    public async Task A_writable_path_carries_the_write_back_the_target_may_want()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert
            .That(source)
            .Contains(
                "Write = (__o, __v) => ((global::Sample.Ui.ItemDef)__o).Family = __v as string"
            );
        await Assert.That(source).Contains("Mode = global::Noesis.BindingMode.TwoWay");
        await Assert
            .That(source)
            .Contains("Trigger = global::Noesis.UpdateSourceTrigger.LostFocus");

        // Two way without saying so is the target property's own metadata to answer at run time.
        await Assert
            .That(source)
            .Contains(
                "Write = (__o, __v) => ((global::Sample.Ui.ItemViewModel)__o).Ticked = __v is bool __w ? __w : default(bool)"
            );
    }

    [Test]
    public async Task An_element_source_reads_through_a_dependency_property()
    {
        var run = CompiledBindings();

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.AllSources)
            .Contains("SourceProperty = global::Noesis.TextBlock.TextProperty");
    }

    [Test]
    public async Task A_dependency_property_another_generator_emits_is_still_a_binding_target()
    {
        var run = GeneratorHarness.Run(
            [new XamlCompileGenerator(), new DependencyPropertyGenerator()],
            ["GeneratedProperty.xaml"],
            [Host("GeneratedPropertyHost"), BadgeControl, SampleUi, Stubs.Mvvm],
            new Dictionary<string, string> { ["build_property.ProjectDir"] = "/repo/App" }
        );

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert.That(source).Contains("global::Sample.Ui.Badge.LabelProperty");
        await Assert
            .That(source)
            .Contains("SourceProperty = global::Sample.Ui.Badge.LabelProperty");
        await Assert.That(source).DoesNotContain("new global::Noesis.Binding(");
    }

    [Test]
    public async Task Root_gets_InitializeComponent_and_typed_name_accessors()
    {
        var run = Shell();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        await Assert.That(source).Contains("public void InitializeComponent()");
        await Assert.That(source).Contains("BuildXamlTree();");
        await Assert.That(source).Contains("global::Noesis.Button Ok");
        await Assert.That(source).Contains("global::Noesis.Button Cancel");
    }

    [Test]
    public async Task Compiled_root_takes_a_name_field_and_a_name_scope_entry()
    {
        var run = Shell();

        await Assert.That(run.Errors).IsEmpty();

        // The field is what the generated code reads; the scope is what a fallback binding's
        // ElementName resolves through.
        await Assert.That(run.AllSources).Contains("this._Ok =");
        await Assert.That(run.AllSources).Contains("__scope.RegisterName(\"Ok\"");
    }

    [Test]
    public async Task Document_with_event_handlers_stays_on_the_native_loader()
    {
        var run = Run(["Handlers.xaml"], [HandlersPartial]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        await Assert.That(source).Contains("global::Noesis.GUI.LoadComponent");
        await Assert.That(source).Contains("ConnectEvent");
        await Assert.That(source).DoesNotContain("BuildXamlTree");
    }

    [Test]
    public async Task One_handler_on_two_element_types_gets_a_branch_each()
    {
        var run = Run(["SharedHandler.xaml"], [SharedHandlerPartial]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        // Dispatching on the handler name alone would cast a Border to a Button at load.
        await Assert.That(source).Contains("source is global::Noesis.Button");
        await Assert.That(source).Contains("source is global::Noesis.Border");
        await Assert.That(source).Contains("eventName == \"Click\"");
        await Assert.That(source).Contains("eventName == \"Loaded\"");
    }

    [Test]
    public async Task A_handler_on_an_unresolvable_tag_forces_the_loader_path()
    {
        var run = Run(["UnresolvedHandler.xaml"], [UnresolvedHostPartial]);

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.AllSources).Contains("global::Noesis.GUI.LoadComponent");
        await Assert.That(run.AllSources).DoesNotContain("BuildXamlTree");
    }

    [Test]
    public async Task A_template_name_gets_no_code_behind_accessor()
    {
        var run = Run(["TemplateName.xaml"], [Host("TemplateHost")]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        // The template registers the name into its own scope a code-behind field would stay null.
        await Assert.That(source).Contains("RegisterName(\"Inside\"");
        await Assert.That(source).DoesNotContain("_Inside");
        await Assert.That(source).Contains("@event");
    }

    [Test]
    public async Task Name_accessor_uses_the_resolved_type_not_the_xml_tag()
    {
        var run = Shell();

        // The tag says "Button" only symbol resolution knows which Button that is.
        await Assert.That(run.AllSources).DoesNotContain("private Button _Ok;");
        await Assert.That(run.AllSources).Contains("private global::Noesis.Button _Ok;");
    }

    [Test]
    public async Task Pack_prefix_defaults_to_the_assembly_name()
    {
        var run = GeneratorHarness.Run(
            new XamlCompileGenerator(),
            ["Handlers.xaml"],
            [HandlersPartial],
            new Dictionary<string, string> { ["build_property.ProjectDir"] = "/repo/App" },
            assemblyName: "MyApp"
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.AllSources).Contains("\"MyApp;Handlers.xaml\"");
    }

    [Test]
    public async Task Pack_prefix_property_overrides_the_assembly_name()
    {
        var run = Run(
            ["Handlers.xaml"],
            [HandlersPartial],
            new Dictionary<string, string>
            {
                ["build_property.ProjectDir"] = "/repo/App",
                ["build_property.NoesisXamlPackPrefix"] = "Engine",
            }
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.AllSources).Contains("\"Engine;Handlers.xaml\"");
    }

    [Test]
    public async Task Generator_is_silent_without_a_Noesis_reference()
    {
        var run = GeneratorHarness.Run(
            new XamlCompileGenerator(),
            ["Theme.xaml"],
            null,
            new Dictionary<string, string> { ["build_property.ProjectDir"] = "/repo/App" },
            referenceNoesis: false
        );

        await Assert.That(run.Sources).IsEmpty();
        await Assert.That(run.GeneratorDiagnostics).IsEmpty();
    }

    [Test]
    public async Task No_xaml_files_produce_no_output()
    {
        var run = GeneratorHarness.Run(new XamlCompileGenerator(), []);

        await Assert.That(run.Sources).IsEmpty();
        await Assert.That(run.GeneratorDiagnostics).IsEmpty();
    }

    [Test]
    public async Task Without_the_runtime_referenced_every_binding_stays_native()
    {
        var run = Run(["CompiledBindings.xaml"], [Host("CompiledHost"), SampleUi]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        // The emitted graph names the runtime by string, so a project without it would otherwise
        // get generated code that does not compile.
        await Assert.That(source).DoesNotContain("CompiledBinding.Bind(");
        await Assert.That(source).Contains("new global::Noesis.Binding(");
    }

    [Test]
    public async Task A_Double_resource_is_boxed_as_a_float_like_the_parser_boxes_it()
    {
        var run = Run(["Primitives.xaml"]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("Primitives");

        await Assert.That(source).Contains("8F");
        await Assert.That(source).DoesNotContain("8D");
    }

    [Test]
    public async Task Two_references_sharing_one_generated_namespace_chain_once()
    {
        var run = GeneratorHarness.Run(
            new XamlCompileGenerator(),
            ["Theme.xaml"],
            buildProperties: new Dictionary<string, string>
            {
                ["build_property.ProjectDir"] = "/repo/App",
            },
            extraReferences:
            [
                GeneratorHarness.Reference("SideCar", SideCarResources),
                GeneratorHarness.Reference("Side.Car", "namespace Side.Car { class Marker { } }"),
            ]
        );

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;

        await Assert.That(source).Contains("var shared0 = global::XamlGenerated.SideCar");
        await Assert.That(source).DoesNotContain("shared1");
    }

    const string SideCarResources = """
        namespace XamlGenerated.SideCar;

        public static class XamlResources
        {
            public static void Flush(global::Noesis.ResourceDictionary root) { }

            public static global::Noesis.ResourceDictionary? Resolve(string logical) => null;

            public static object? SharedByKey(object key) => null;
        }
        """;

    [Test]
    public async Task An_unsupported_document_names_its_file_in_the_diagnostic()
    {
        var run = Run(["NoClass.xaml"]);

        var message = run.GeneratorDiagnostics.Single(d => d.Id == "NTK1001").GetMessage();
        await Assert.That(message).Contains("NoClass.xaml");
    }

    [Test]
    public async Task Literal_values_convert_to_the_property_type()
    {
        var run = Run(["Conversions.xaml"]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("Conversions");
        await Assert.That(source).Contains("float.NaN");
        await Assert.That(source).Contains("12.5F");
        await Assert.That(source).Contains("global::Noesis.Visibility.Collapsed");
    }

    [Test]
    public async Task A_template_registers_its_names_and_keeps_its_resources()
    {
        var run = Templated();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("Templated");
        await Assert.That(source).Contains("RegisterName(\"Bg\"");
        await Assert.That(source).Contains("Accent");
    }

    [Test]
    public async Task StaticResource_resolves_once_where_DynamicResource_stays_live()
    {
        var run = Templated();

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("Templated");

        // A reference resolves by tree position, which a template prototype never has, and re-reads
        // on every later resource change — neither of which a StaticResource may do.
        await Assert
            .That(source)
            .Contains(
                "SetResourceReference(global::Noesis.Border.BorderBrushProperty, \"Accent\")"
            );
        await Assert
            .That(source)
            .DoesNotContain("SetResourceReference(global::Noesis.Border.BackgroundProperty");
        await Assert.That(source).Contains(".Background = (global::Noesis.Brush)__found");
    }

    const string UnresolvedHostPartial = """
        namespace Sample;

        public partial class UnresolvedHost : global::Noesis.UserControl
        {
            public UnresolvedHost() => InitializeComponent();

            void OnGo(object sender, System.EventArgs e) { }
        }
        """;

    const string ManagedControl = """
        namespace Sample;

        public class MyControl : global::Noesis.Button { }
        """;

    // Lazy, not a field: tests run in parallel and a shared run must be built exactly once.
    [Test]
    public async Task A_template_binding_is_built_by_the_parser_once_per_template()
    {
        var run = Run(["TemplateBound.xaml"]);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("TemplateBound");

        await Assert
            .That(source.Split("__templated(global::Noesis.GUI.ParseXaml(").Length)
            .IsEqualTo(2);
        await Assert.That(source).Contains("TargetType=\"\"{x:Type Button}\"\"");
        await Assert.That(source).Contains("Background=\"\"{TemplateBinding Background}\"\"");
        await Assert.That(source).DoesNotContain("RelativeSource.TemplatedParent");
        await Assert.That(source).DoesNotContain("Padding=\"\"2\"\"");
        await Assert
            .That(run.GeneratorDiagnostics.Select(d => d.GetMessage()))
            .Contains(
                "DEAD TemplateBound.xaml :: a {TemplateBinding} that is not an element's attribute is left unset"
            );
    }

    static readonly Lazy<GeneratorRun> CompiledBindingsRun = new(() =>
        Run(["CompiledBindings.xaml"], [Host("CompiledHost"), SampleUi, Stubs.Mvvm])
    );

    static readonly Lazy<GeneratorRun> ThemeRun = new(() => Run(["Theme.xaml"]));

    static readonly Lazy<GeneratorRun> ShellRun = new(() => Run(["Shell.xaml"], [Host("Shell")]));

    static readonly Lazy<GeneratorRun> TemplatedRun = new(() => Run(["Templated.xaml"]));

    const string SampleUi = """
        namespace Sample.Ui;

        public class ItemDef { public string Family { get; set; } = ""; }

        public class UpConverter : global::Noesis.IValueConverter
        {
            public object? Convert(object? v, System.Type t, object? p, System.Globalization.CultureInfo c) => v;
            public object? ConvertBack(object? v, System.Type t, object? p, System.Globalization.CultureInfo c) => v;
        }

        public class JoinConverter : global::Noesis.IMultiValueConverter
        {
            public object? Convert(object?[] v, System.Type t, object? p, System.Globalization.CultureInfo c) => string.Join(",", v);
            public object?[] ConvertBack(object? v, System.Type[] t, object? p, System.Globalization.CultureInfo c) => [];
        }

        public class PokeBehavior : global::Noesis.Interactivity.Behavior<global::Noesis.FrameworkElement>
        {
            public static readonly global::Noesis.DependencyProperty TextProperty = global::Noesis.DependencyProperty.Register("Text", typeof(string), typeof(PokeBehavior), new global::Noesis.PropertyMetadata());
            public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        }

        public class PokeTrigger : global::Noesis.Interactivity.TriggerBase<global::Noesis.FrameworkElement>
        {
            public string? EventName { get; set; }
        }

        public class PokeAction : global::Noesis.Interactivity.TriggerAction<global::Noesis.DependencyObject>
        {
            public static readonly global::Noesis.DependencyProperty ParameterProperty = global::Noesis.DependencyProperty.Register("Parameter", typeof(object), typeof(PokeAction), new global::Noesis.PropertyMetadata());
            public object? Parameter { get => GetValue(ParameterProperty); set => SetValue(ParameterProperty, value); }
        }

        public class ItemViewModel
        {
            public int DefIdInt { get; set; }
            public double Ratio { get; set; }
            public bool Ticked { get; set; }
            public string Family { get; set; } = "";
            public string ReadOnlyName { get; } = "";
            public ItemDef Def { get; set; } = new ItemDef();
            public System.Windows.Input.ICommand? Go { get; set; }
        }
        """;

    const string BadgeControl = """
        using NoesisToolkit.Mvvm;

        namespace Sample.Ui;

        public partial class Badge : global::Noesis.Control
        {
            [DependencyProperty]
            public partial string? Label { get; set; }
        }
        """;

    const string SharedHandlerPartial = """
        namespace Sample;

        public partial class SharedHandler : global::Noesis.UserControl
        {
            public SharedHandler() => InitializeComponent();

            void OnGo(object sender, System.EventArgs e) { }
        }
        """;

    const string HandlersPartial = """
        namespace Sample;

        public partial class WithHandlers : global::Noesis.UserControl
        {
            public WithHandlers() => InitializeComponent();

            void OnGo(object sender, System.EventArgs e) { }
        }
        """;
}
