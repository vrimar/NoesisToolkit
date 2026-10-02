using NoesisToolkit.Mvvm.Generators;

namespace NoesisToolkit.Tests;

public class OverrideRootsTests
{
    const string Attribute = "[global::System.Diagnostics.CodeAnalysis.DynamicDependency(";

    static string Root(string signature, string type) =>
        $"{Attribute}\"{signature}\", typeof({type}))]";

    static GeneratorRun Run(params string[] sources) =>
        GeneratorHarness.Run(new OverrideRootsGenerator(), [], sources);

    [Test]
    public async Task An_override_Noesis_probes_is_rooted_by_its_exact_signature()
    {
        var run = Run(Controls);

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("NoesisOverrideRoots");
        await Assert
            .That(source)
            .Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        await Assert
            .That(source)
            .Contains(Root("MeasureOverride(Noesis.Size)", "global::Sample.Shelf"));
        await Assert
            .That(source)
            .Contains(Root("ArrangeOverride(Noesis.Size)", "global::Sample.Shelf"));
        await Assert
            .That(source)
            .Contains(Root("OnRender(Noesis.DrawingContext)", "global::Sample.Shelf"));
        await Assert.That(source).Contains(Root("OnApplyTemplate", "global::Sample.Shelf"));
    }

    [Test]
    public async Task ToString_and_Equals_are_rooted_on_any_type_Noesis_can_be_handed()
    {
        var source = Run(Controls).Source("NoesisOverrideRoots");

        await Assert.That(source).Contains(Root("ToString", "global::Sample.RowViewModel"));
        await Assert
            .That(source)
            .Contains(Root("Equals(System.Object)", "global::Sample.RowViewModel"));
        await Assert.That(source).DoesNotContain("GetHashCode");
    }

    [Test]
    public async Task An_override_of_a_virtual_Noesis_does_not_own_is_left_alone()
    {
        var source = Run(Controls).Source("NoesisOverrideRoots");

        await Assert.That(source).DoesNotContain("global::Sample.SpriteRenderer");
        await Assert.That(source).DoesNotContain("OnPropertyChanged");
    }

    [Test]
    public async Task A_record_roots_the_ToString_and_Equals_the_compiler_writes_for_it()
    {
        var run = Run(
            "namespace Sample { public record Row(int X); public record struct Cell(int X); }"
        );

        await Assert.That(run.Errors).IsEmpty();
        var source = run.Source("NoesisOverrideRoots");
        await Assert.That(source).Contains(Root("ToString", "global::Sample.Row"));
        await Assert.That(source).Contains(Root("Equals(System.Object)", "global::Sample.Row"));
        await Assert.That(source).Contains(Root("ToString", "global::Sample.Cell"));
        await Assert.That(source).Contains(Root("Equals(System.Object)", "global::Sample.Cell"));
    }

    [Test]
    public async Task A_type_nested_in_an_open_generic_is_named_unbound()
    {
        var run = Run(
            """
            namespace Sample
            {
                public class Outer<T>
                {
                    public class Inner : Noesis.Control
                    {
                        protected override Noesis.Size MeasureOverride(Noesis.Size availableSize) => availableSize;
                    }
                }
            }
            """
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.Source("NoesisOverrideRoots"))
            .Contains(Root("MeasureOverride(Noesis.Size)", "global::Sample.Outer<>.Inner"));
    }

    [Test]
    public async Task A_file_local_type_is_never_named()
    {
        var run = Run(
            "namespace Sample { file class Hidden : Noesis.Control { public override void OnApplyTemplate() { } } }",
            Controls
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.Source("NoesisOverrideRoots")).DoesNotContain("Hidden");
    }

    [Test]
    public async Task An_assembly_named_by_a_keyword_still_names_a_namespace()
    {
        var run = GeneratorHarness.Run(
            new OverrideRootsGenerator(),
            [],
            [Controls],
            assemblyName: "int"
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.Source("NoesisOverrideRoots"))
            .Contains("namespace NoesisToolkitGenerated.@int");
    }

    [Test]
    public async Task Nothing_is_emitted_where_nothing_is_overridden()
    {
        var run = Run("namespace Sample; public class Plain { }");

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.Sources.Keys.Any(k => k.Contains("NoesisOverrideRoots"))).IsFalse();
    }

    [Test]
    public async Task Survives_an_unrelated_edit()
    {
        var reasons = GeneratorHarness.RunAfterUnrelatedEdit(
            new OverrideRootsGenerator(),
            [],
            [Controls],
            null,
            [OverrideRootsGenerator.OverridesStep]
        );

        await Assert.That(reasons).IsNotEmpty();
        await Assert
            .That(
                reasons.Where(r =>
                    r
                        is not (
                            Microsoft.CodeAnalysis.IncrementalStepRunReason.Cached
                            or Microsoft.CodeAnalysis.IncrementalStepRunReason.Unchanged
                        )
                )
            )
            .IsEmpty();
    }

    const string Controls = """
        namespace Sample
        {
            public class Shelf : Noesis.Control
            {
                protected override Noesis.Size MeasureOverride(Noesis.Size availableSize) => availableSize;

                protected override Noesis.Size ArrangeOverride(Noesis.Size finalSize) => finalSize;

                protected override void OnRender(Noesis.DrawingContext context) { }

                public override void OnApplyTemplate() { }

                protected override void OnPropertyChanged(Noesis.DependencyPropertyChangedEventArgs args) { }
            }

            public class RowViewModel
            {
                public override string ToString() => "row";

                public override bool Equals(object? obj) => obj is RowViewModel;

                public override int GetHashCode() => 0;
            }

            public abstract class Renderer
            {
                protected virtual void OnRender(Noesis.DrawingContext context) { }
            }

            public class SpriteRenderer : Renderer
            {
                protected override void OnRender(Noesis.DrawingContext context) { }
            }
        }
        """;
}
