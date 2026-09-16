using Noesis;
using Noesis.Interactivity;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class DrSub
{
    public string Name { get; set; } = "sub-name";
}

public sealed class DrModel
{
    public string Name { get; set; } = "model-name";

    public DrSub Child { get; } = new DrSub();
}

public sealed class DrBehavior : Behavior<FrameworkElement>
{
    public static readonly DependencyProperty PayloadProperty = DependencyProperty.Register(
        "Payload",
        typeof(object),
        typeof(DrBehavior),
        new PropertyMetadata(null)
    );

    public object? Payload
    {
        get => GetValue(PayloadProperty);
        set => SetValue(PayloadProperty, value);
    }

    public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(
        "Note",
        typeof(int),
        typeof(DrBehavior),
        new PropertyMetadata(17)
    );

    public int Note
    {
        get => (int)GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }
}

public sealed class DrAction : TriggerAction<FrameworkElement>
{
    public static readonly DependencyProperty ParameterProperty = DependencyProperty.Register(
        "Parameter",
        typeof(object),
        typeof(DrAction),
        new PropertyMetadata(null)
    );

    public object? Parameter
    {
        get => GetValue(ParameterProperty);
        set => SetValue(ParameterProperty, value);
    }

    protected override void Invoke(object parameter) { }
}

public sealed class DrTrigger : TriggerBase<FrameworkElement> { }

public sealed class DrHost : Border
{
    public readonly DrBehavior BuiltinBehavior = new DrBehavior { Note = 1 };

    public readonly KeyBinding BuiltinKey = new KeyBinding { Key = Key.F1 };

    public readonly DrAction BuiltinAction = new DrAction();

    public DrHost()
    {
        Interaction.GetBehaviors(this).Add(BuiltinBehavior);
        InputBindings.Add(BuiltinKey);

        var trigger = new DrTrigger();
        trigger.Actions.Add(BuiltinAction);
        Interaction.GetTriggers(this).Add(trigger);
    }
}

public partial class DrRootFixture : UserControl { }

[NotInParallel("Noesis")]
public sealed class CompiledDetachedReceiverTests
{
    [Test]
    public async Task Objects_attached_in_a_template_are_written_as_the_native_bindings_write_them()
    {
        NoesisRuntime.Start();

        var compiled = TreeSearch.Named<Grid>(Realize(Compiled<DataTemplate>("Plain")), "Host")!;
        var parsed = TreeSearch.Named<Grid>(Realize(Parsed<DataTemplate>("Plain")), "Host")!;

        var parsedBehavior = (DrBehavior)Interaction.GetBehaviors(parsed)[0];
        var compiledBehavior = (DrBehavior)Interaction.GetBehaviors(compiled)[0];
        await Assert.That(parsedBehavior.Payload).IsEqualTo("model-name");
        await Assert.That(compiledBehavior.Payload).IsEqualTo(parsedBehavior.Payload);
        await Assert.That(Bound(compiledBehavior)).IsFalse();

        await Assert.That(parsed.InputBindings[0].CommandParameter).IsEqualTo("model-name");
        await Assert
            .That(compiled.InputBindings[0].CommandParameter)
            .IsEqualTo(parsed.InputBindings[0].CommandParameter);
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    compiled.InputBindings[0],
                    InputBinding.CommandParameterProperty
                )
            )
            .IsNull();

        var parsedAction = Action(parsed);
        var compiledAction = Action(compiled);
        await Assert.That(parsedAction.Parameter).IsEqualTo("model-name");
        await Assert.That(compiledAction.Parameter).IsEqualTo(parsedAction.Parameter);
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    compiledAction,
                    DrAction.ParameterProperty
                )
            )
            .IsNull();
    }

    [Test]
    public async Task An_ancestor_lookup_from_a_behavior_counts_the_element_it_is_attached_to()
    {
        NoesisRuntime.Start();

        var compiled = Behavior(Realize(Compiled<DataTemplate>("Ancestor")));
        var parsed = Behavior(Realize(Parsed<DataTemplate>("Ancestor")));

        await Assert.That(parsed.Payload).IsEqualTo("host");
        await Assert.That(compiled.Payload).IsEqualTo(parsed.Payload);
        await Assert.That(Bound(compiled)).IsFalse();
    }

    [Test]
    public async Task A_self_source_on_a_behavior_stays_native()
    {
        NoesisRuntime.Start();

        var compiled = Behavior(Realize(Compiled<DataTemplate>("Self")));
        var parsed = Behavior(Realize(Parsed<DataTemplate>("Self")));

        await Assert.That(parsed.Payload).IsEqualTo(99);
        await Assert.That(compiled.Payload).IsEqualTo(parsed.Payload);
        await Assert.That(Bound(compiled)).IsTrue();
    }

    [Test]
    public async Task A_templated_parent_source_on_a_behavior_reads_the_hosts_templated_parent()
    {
        NoesisRuntime.Start();

        var compiled = Behavior(Templated(Compiled<ControlTemplate>("Templated")));
        var parsed = Behavior(Templated(Parsed<ControlTemplate>("Templated")));

        await Assert.That(parsed.Payload).IsEqualTo("stated");
        await Assert.That(compiled.Payload).IsEqualTo(parsed.Payload);
        await Assert.That(Bound(compiled)).IsFalse();
    }

    [Test]
    public async Task A_behavior_on_a_presenter_reads_the_content_it_adopts()
    {
        NoesisRuntime.Start();

        var compiled = Behavior(Realize(Compiled<DataTemplate>("Presenter")));
        var parsed = Behavior(Realize(Parsed<DataTemplate>("Presenter")));

        await Assert.That(parsed.Payload).IsEqualTo("sub-name");
        await Assert.That(compiled.Payload).IsEqualTo(parsed.Payload);
        await Assert.That(Bound(compiled)).IsTrue();
    }

    [Test]
    public async Task A_behavior_the_host_constructor_added_is_not_the_one_the_document_bound()
    {
        NoesisRuntime.Start();

        var compiledRoot = Realize(Compiled<DataTemplate>("Constructed"));
        var parsedRoot = Realize(Parsed<DataTemplate>("Constructed"));
        var compiled = TreeSearch.Named<DrHost>(compiledRoot, "Host")!;
        var parsed = TreeSearch.Named<DrHost>(parsedRoot, "Host")!;

        await Assert.That(parsed.BuiltinBehavior.Payload).IsNull();
        await Assert
            .That(compiled.BuiltinBehavior.Payload)
            .IsEqualTo(parsed.BuiltinBehavior.Payload);
        await Assert
            .That(Interaction.GetBehaviors(compiled).Count)
            .IsEqualTo(Interaction.GetBehaviors(parsed).Count);
        await Assert
            .That((int)compiled.GetValue(CompiledBindingSetup.IndexProperty))
            .IsNotEqualTo(-1);
    }

    [Test]
    public async Task Objects_the_host_constructor_added_do_not_take_the_documents_bindings()
    {
        NoesisRuntime.Start();

        var compiledRoot = new DrRootFixture { DataContext = new DrModel() };
        compiledRoot.InitializeComponent();
        var parsedRoot = (DrRootFixture)GUI.LoadXaml("/Fixtures;Fixtures/DrRoot.xaml");
        parsedRoot.DataContext = new DrModel();
        NoesisRuntime.Show(new Grid { Width = 800, Height = 300 }, compiledRoot, parsedRoot);

        var compiled = TreeSearch.Named<DrHost>(compiledRoot, "Host")!;
        var parsed = TreeSearch.Named<DrHost>(parsedRoot, "Host")!;

        var parsedBehavior = (DrBehavior)Interaction.GetBehaviors(parsed)[1];
        var compiledBehavior = (DrBehavior)Interaction.GetBehaviors(compiled)[1];
        await Assert.That(parsed.BuiltinBehavior.Payload).IsNull();
        await Assert.That(parsedBehavior.Payload).IsEqualTo("model-name");
        await Assert
            .That(compiled.BuiltinBehavior.Payload)
            .IsEqualTo(parsed.BuiltinBehavior.Payload);
        await Assert.That(compiledBehavior.Payload).IsEqualTo(parsedBehavior.Payload);
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    compiledBehavior,
                    DrBehavior.PayloadProperty
                )
            )
            .IsNull();

        var parsedKey = parsed.InputBindings[1];
        var compiledKey = compiled.InputBindings[1];
        await Assert.That(parsed.BuiltinKey.CommandParameter).IsNull();
        await Assert.That(parsedKey.CommandParameter).IsEqualTo("model-name");
        await Assert
            .That(compiled.BuiltinKey.CommandParameter)
            .IsEqualTo(parsed.BuiltinKey.CommandParameter);
        await Assert.That(compiledKey.CommandParameter).IsEqualTo(parsedKey.CommandParameter);
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    compiledKey,
                    InputBinding.CommandParameterProperty
                )
            )
            .IsNull();

        var parsedAction = (DrAction)
            ((Noesis.Interactivity.TriggerBase)Interaction.GetTriggers(parsed)[1]).Actions[0];
        var compiledAction = (DrAction)
            ((Noesis.Interactivity.TriggerBase)Interaction.GetTriggers(compiled)[1]).Actions[0];
        await Assert.That(parsed.BuiltinAction.Parameter).IsNull();
        await Assert.That(parsedAction.Parameter).IsEqualTo("model-name");
        await Assert
            .That(compiled.BuiltinAction.Parameter)
            .IsEqualTo(parsed.BuiltinAction.Parameter);
        await Assert.That(compiledAction.Parameter).IsEqualTo(parsedAction.Parameter);
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    compiledAction,
                    DrAction.ParameterProperty
                )
            )
            .IsNull();
    }

    static T Compiled<T>(string key)
        where T : FrameworkTemplate =>
        (T)XamlGenerated.NoesisToolkitEquivalenceTests.FixturesDrReceiversxamlXaml.Build()[key];

    static T Parsed<T>(string key)
        where T : FrameworkTemplate =>
        (T)((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/DrReceivers.xaml"))[key];

    static Grid Realize(DataTemplate template)
    {
        var host = new ContentControl { Content = new DrModel(), ContentTemplate = template };
        var root = new Grid { Width = 400, Height = 300 };
        NoesisRuntime.Show(root, host);
        return root;
    }

    static Grid Templated(ControlTemplate template)
    {
        var host = new SpikeControl { Template = template, Label = "stated" };
        var root = new Grid { Width = 400, Height = 300 };
        NoesisRuntime.Show(root, host);
        return root;
    }

    static DrBehavior Behavior(Grid root) =>
        (DrBehavior)Interaction.GetBehaviors(TreeSearch.Named<FrameworkElement>(root, "Host")!)[0];

    static DrAction Action(FrameworkElement host) =>
        (DrAction)((Noesis.Interactivity.TriggerBase)Interaction.GetTriggers(host)[0]).Actions[0];

    static bool Bound(DrBehavior behavior) =>
        BindingOperations.GetBindingExpressionBase(behavior, DrBehavior.PayloadProperty)
            is not null;
}
