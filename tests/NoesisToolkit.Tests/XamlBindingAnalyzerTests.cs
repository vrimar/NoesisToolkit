using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NoesisToolkit.Analyzers;

namespace NoesisToolkit.Tests;

public class XamlBindingAnalyzerTests
{
    static async Task<ImmutableArray<Diagnostic>> Bindings(string dataType, string body)
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new XamlBindingAnalyzer(),
            "/tmp/Probe.xaml",
            $$"""
            <ResourceDictionary
              xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:ui="clr-namespace:Sample.Ui"
              xmlns:ntk="https://github.com/vrimar/NoesisToolkit">
              <DataTemplate DataType="{x:Type ui:{{dataType}}}">
                {{body}}
              </DataTemplate>
            </ResourceDictionary>
            """,
            Stubs.Sample
        );

        return diagnostics
            .Where(d => d.Id == XamlBindingAnalyzer.UnresolvedBindingId)
            .ToImmutableArray();
    }

    static Task<ImmutableArray<Diagnostic>> Document(string body) =>
        XamlProbe.Analyze(body, XamlBindingAnalyzer.UndeclaredContextId);

    [Test]
    public async Task An_ancestor_type_matches_a_subclass_of_it()
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new XamlBindingAnalyzer(),
            "/tmp/Probe.xaml",
            """
            <ResourceDictionary
              xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:ui="clr-namespace:Sample.Ui"
              xmlns:ntk="https://github.com/vrimar/NoesisToolkit">
              <ui:TypedListHost ntk:DataType="ui:ItemViewModel">
                <DataTemplate>
                  <TextBlock
                    Text="{Binding DataContext.Nope,
                                   RelativeSource={RelativeSource AncestorType={x:Type ui:ListHost}}}" />
                </DataTemplate>
              </ui:TypedListHost>
            </ResourceDictionary>
            """,
            Stubs.Sample
        );

        var unresolved = diagnostics
            .Where(d => d.Id == XamlBindingAnalyzer.UnresolvedBindingId)
            .ToImmutableArray();

        await Assert.That(unresolved.Length).IsEqualTo(1);
        await Assert.That(unresolved[0].GetMessage()).Contains("Nope");
    }

    [Test]
    public async Task An_untyped_binding_is_silent_without_the_marker()
    {
        var diagnostics = await Document(
            """
            <DataTemplate x:Key="Row">
              <TextBlock Text="{Binding Whatever}" />
            </DataTemplate>
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task An_untyped_binding_is_an_error_once_the_document_opts_in()
    {
        var diagnostics = await Document(
            """
            <DataTemplate x:Key="Row" ntk:CompileBindings="True">
              <TextBlock Text="{Binding Whatever}" />
            </DataTemplate>
            """
        );

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostics[0].GetMessage()).Contains("Whatever");
        await Assert.That(diagnostics[0].GetMessage()).Contains("ntk:DataType");
    }

    [Test]
    public async Task Annotating_the_template_satisfies_the_marker()
    {
        var diagnostics = await Document(
            """
            <DataTemplate x:Key="Row" ntk:CompileBindings="True" ntk:DataType="ui:ItemViewModel">
              <TextBlock Text="{Binding DefIdInt}" />
            </DataTemplate>
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_keyed_template_is_not_typed_by_the_site_that_names_it()
    {
        var diagnostics = await Document(
            """
            <DataTemplate x:Key="Row" ntk:CompileBindings="True">
              <TextBlock Text="{Binding DefIdInt}" />
            </DataTemplate>
            <DataTemplate ntk:DataType="ui:ShellViewModel" ntk:CompileBindings="True">
              <ItemsControl ItemsSource="{Binding Items}" ItemTemplate="{StaticResource Row}" />
            </DataTemplate>
            """
        );

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].GetMessage()).Contains("DefIdInt");
    }

    [Test]
    public async Task A_source_written_as_a_property_element_still_scopes_its_template()
    {
        var diagnostics = await XamlProbe.Analyze(
            """
            <DataTemplate ntk:DataType="ui:ShellViewModel">
              <ItemsControl>
                <ItemsControl.ItemTemplate>
                  <DataTemplate>
                    <TextBlock Text="{Binding DefIdInt}" />
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
                <ItemsControl.ItemsSource>
                  <Binding Path="Items" />
                </ItemsControl.ItemsSource>
              </ItemsControl>
            </DataTemplate>
            """,
            XamlBindingAnalyzer.UndeclaredContextId,
            @"ntk:CompileBindings=""True"""
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_keyed_template_named_from_several_sites_is_checked_against_none()
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new XamlBindingAnalyzer(),
            "/tmp/Probe.xaml",
            """
            <ResourceDictionary
              xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:ui="clr-namespace:Sample.Ui"
              xmlns:ntk="https://github.com/vrimar/NoesisToolkit"
              ntk:CompileBindings="True">
              <DataTemplate x:Key="Row">
                <TextBlock Text="{Binding DefIdInt}" />
              </DataTemplate>
              <DataTemplate ntk:DataType="ui:ShellViewModel">
                <ItemsControl ItemsSource="{Binding Items}" ItemTemplate="{StaticResource Row}" />
                <ItemsControl ItemsSource="{Binding Others}" ItemTemplate="{StaticResource Row}" />
              </DataTemplate>
            </ResourceDictionary>
            """,
            Stubs.Sample
        );

        await Assert
            .That(diagnostics.Where(d => d.Id == XamlBindingAnalyzer.UnresolvedBindingId))
            .IsEmpty();

        var undeclared = diagnostics
            .Where(d => d.Id == XamlBindingAnalyzer.UndeclaredContextId)
            .ToImmutableArray();

        await Assert.That(undeclared.Length).IsEqualTo(1);
        await Assert.That(undeclared[0].GetMessage()).Contains("DefIdInt");
    }

    [Test]
    public async Task An_item_template_takes_the_element_type_of_the_items_source()
    {
        var diagnostics = await Document(
            """
            <DataTemplate ntk:DataType="ui:ShellViewModel" ntk:CompileBindings="True">
              <ItemsControl ItemsSource="{Binding Items}">
                <ItemsControl.ItemTemplate>
                  <DataTemplate>
                    <TextBlock Text="{Binding DefIdInt}" />
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </DataTemplate>
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task An_inline_style_keeps_the_enclosing_DataContext()
    {
        var diagnostics = await Document(
            """
            <DataTemplate ntk:DataType="ui:ItemViewModel" ntk:CompileBindings="True">
              <TextBlock>
                <TextBlock.Style>
                  <Style TargetType="TextBlock">
                    <Setter Property="Text" Value="{Binding DefIdInt}" />
                  </Style>
                </TextBlock.Style>
              </TextBlock>
            </DataTemplate>
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_subtree_can_opt_back_out_of_the_marker()
    {
        var diagnostics = await Document(
            """
            <DataTemplate x:Key="Row" ntk:CompileBindings="True">
              <TextBlock ntk:CompileBindings="False" Text="{Binding Whatever}" />
            </DataTemplate>
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_path_the_DataContext_does_not_have_is_reported()
    {
        var diagnostics = await Bindings(
            "ItemViewModel",
            """<TextBlock Text="{Binding Nope}" />"""
        );

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].GetMessage()).Contains("ItemViewModel");
        await Assert.That(diagnostics[0].GetMessage()).Contains("Nope");
    }

    [Test]
    public async Task A_path_that_resolves_is_not_reported()
    {
        var diagnostics = await Bindings(
            "ItemViewModel",
            """<TextBlock Text="{Binding DefIdInt}" />"""
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_dotted_path_is_checked_hop_by_hop()
    {
        await Assert
            .That(
                (await Bindings("ItemViewModel", """<TextBlock Text="{Binding Def.Family}" />"""))
            )
            .IsEmpty();

        var bad = await Bindings("ItemViewModel", """<TextBlock Text="{Binding Def.Nope}" />""");
        await Assert.That(bad.Length).IsEqualTo(1);
        await Assert.That(bad[0].GetMessage()).Contains("Nope");
    }

    [Test]
    public async Task An_interface_nothing_implements_is_not_checked()
    {
        await Assert
            .That(
                (
                    await Bindings(
                        "ItemViewModel",
                        """<TextBlock Text="{Binding Thing.Anything}" />"""
                    )
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task An_abstract_context_nothing_derives_from_is_not_checked()
    {
        await Assert
            .That((await Bindings("AbstractViewModel", """<TextBlock Text="{Binding Nope}" />""")))
            .IsEmpty();
    }

    [Test]
    public async Task A_member_a_subclass_declares_is_not_reported()
    {
        // A DerivedViewModel instance may sit behind a BaseViewModel DataContext.
        await Assert
            .That((await Bindings("BaseViewModel", """<TextBlock Text="{Binding OnDerived}" />""")))
            .IsEmpty();
    }

    [Test]
    public async Task A_member_no_subclass_declares_is_reported()
    {
        var diagnostics = await Bindings(
            "BaseViewModel",
            """<TextBlock Text="{Binding Nope}" />"""
        );

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].GetMessage()).Contains("BaseViewModel");
        await Assert.That(diagnostics[0].GetMessage()).Contains("Nope");
    }

    [Test]
    [Arguments("Title")]
    [Arguments("Balance")]
    public async Task An_abstract_context_resolves_what_it_or_a_subclass_declares(string path)
    {
        await Assert
            .That(
                (await Bindings("ShelfViewModel", $$"""<TextBlock Text="{Binding {{path}}}" />"""))
            )
            .IsEmpty();
    }

    [Test]
    public async Task An_abstract_context_reports_what_no_subclass_declares()
    {
        var diagnostics = await Bindings(
            "ShelfViewModel",
            """<TextBlock Text="{Binding Nope}" />"""
        );

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].GetMessage()).Contains("ShelfViewModel");
        await Assert.That(diagnostics[0].GetMessage()).Contains("Nope");
    }

    [Test]
    public async Task The_walk_continues_through_an_abstract_hop()
    {
        await Assert
            .That(
                (
                    await Bindings(
                        "ItemViewModel",
                        """<TextBlock Text="{Binding Shelf.Balance}" />"""
                    )
                )
            )
            .IsEmpty();

        var bad = await Bindings("ItemViewModel", """<TextBlock Text="{Binding Shelf.Nope}" />""");
        await Assert.That(bad.Length).IsEqualTo(1);
        await Assert.That(bad[0].GetMessage()).Contains("Nope");
    }

    [Test]
    [Arguments("Panel.Detail.Tone")]
    [Arguments("Panel.Detail.Family")]
    public async Task A_member_a_subclass_redeclares_with_another_type_stops_the_walk(string path)
    {
        await Assert
            .That(
                (await Bindings("ItemViewModel", $$"""<TextBlock Text="{Binding {{path}}}" />"""))
            )
            .IsEmpty();
    }

    [Test]
    public async Task A_hop_into_another_assemblys_type_subclassed_here_stops_the_walk()
    {
        await Assert
            .That(
                (await Bindings("ItemViewModel", """<TextBlock Text="{Binding Error.Code}" />"""))
            )
            .IsEmpty();
    }

    [Test]
    public async Task An_interface_is_checked_against_its_implementations()
    {
        await Assert
            .That(
                (
                    await Bindings(
                        "ItemViewModel",
                        """<TextBlock Text="{Binding Labelled.Tone}" />"""
                    )
                )
            )
            .IsEmpty();

        var bad = await Bindings(
            "ItemViewModel",
            """<TextBlock Text="{Binding Labelled.Nope}" />"""
        );
        await Assert.That(bad.Length).IsEqualTo(1);
        await Assert.That(bad[0].GetMessage()).Contains("ILabelled");
    }

    [Test]
    public async Task An_interface_from_another_assembly_is_not_checked_whatever_implements_it_here()
    {
        await Assert
            .That(
                (await Bindings("ItemViewModel", """<TextBlock Text="{Binding Rows.Count}" />"""))
            )
            .IsEmpty();
    }

    [Test]
    public async Task The_rule_is_off_when_the_property_says_so()
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new XamlBindingAnalyzer(),
            "/tmp/Probe.xaml",
            """
            <ResourceDictionary
              xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:ui="clr-namespace:Sample.Ui"
              xmlns:ntk="https://github.com/vrimar/NoesisToolkit">
              <DataTemplate DataType="{x:Type ui:ItemViewModel}">
                <TextBlock Text="{Binding Nope}" />
              </DataTemplate>
            </ResourceDictionary>
            """,
            Stubs.Sample,
            new Dictionary<string, string>
            {
                ["build_property.NoesisAnalyzeXamlBindings"] = "false",
            }
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_document_with_a_different_extension_is_skipped()
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new XamlBindingAnalyzer(),
            "/tmp/Probe.nxaml",
            """
            <ResourceDictionary
              xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:ui="clr-namespace:Sample.Ui"
              xmlns:ntk="https://github.com/vrimar/NoesisToolkit">
              <DataTemplate DataType="{x:Type ui:ItemViewModel}">
                <TextBlock Text="{Binding Nope}" />
              </DataTemplate>
            </ResourceDictionary>
            """,
            Stubs.Sample
        );

        await Assert.That(diagnostics).IsEmpty();
    }
}
