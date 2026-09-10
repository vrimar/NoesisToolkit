using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Tests;

public class BindingScannerTests
{
    const string Header = """
        <ResourceDictionary
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          xmlns:ui="clr-namespace:Sample.Ui"
          xmlns:ntk="https://github.com/vrimar/NoesisToolkit">
        """;

    static IReadOnlyList<BindingCheck> Scan(string body) =>
        BindingScanner.ScanAll(Header + "\n" + body + "\n</ResourceDictionary>").Checks;

    static bool Has(IReadOnlyList<BindingCheck> checks, string contextType, string path) =>
        checks.Any(c => c.ContextType == contextType && c.Path == path);

    [Test]
    public async Task TypedDataTemplate_ChecksAgainstDataType()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <TextBlock Text="{Binding DefIdInt}" />
            </DataTemplate>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.ItemViewModel", "DefIdInt")).IsTrue();
    }

    [Test]
    public async Task LineNumber_IsTheAttribute_NotTheElement()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <TextBlock
                Foreground="Red"
                Text="{Binding DefIdInt}" />
            </DataTemplate>
            """
        );

        await Assert.That(checks.Single(c => c.Path == "DefIdInt").Line).IsEqualTo(9);
    }

    [Test]
    public async Task MultiLineBinding_IsParsed()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <TextBlock Foreground="{Binding Item,
                                              Converter={StaticResource Q}}" />
            </DataTemplate>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.ItemViewModel", "Item")).IsTrue();
    }

    [Test]
    public async Task DottedPath_IsCheckedWhole()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <TextBlock Text="{Binding Def.Family}" />
            </DataTemplate>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.ItemViewModel", "Def.Family")).IsTrue();
    }

    [Test]
    [Arguments("{Binding}")]
    [Arguments("{Binding (ScrollViewer.PanningMode)}")]
    [Arguments("{Binding Foo, ElementName=NotDeclaredAnywhere}")]
    public async Task UncheckableBindings_AreSilent(string binding)
    {
        var checks = Scan(
            $$"""
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <TextBlock Text="{{binding}}" />
            </DataTemplate>
            """
        );

        await Assert.That(checks).IsEmpty();
    }

    [Test]
    public async Task ControlTemplate_DoesNotInheritTheDataContext()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <ControlTemplate>
                <TextBlock Text="{Binding DefIdInt}" />
              </ControlTemplate>
            </DataTemplate>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.ItemViewModel", "DefIdInt")).IsFalse();
    }

    [Test]
    public async Task UntypedTemplate_WithNoHost_IsSilent()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <ContentControl>
                <ContentControl.ContentTemplate>
                  <DataTemplate><TextBlock Text="{Binding Attack}" /></DataTemplate>
                </ContentControl.ContentTemplate>
              </ContentControl>
            </DataTemplate>
            """
        );

        await Assert.That(checks).IsEmpty();
    }

    [Test]
    public async Task UntypedTemplate_InfersFromContent()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <ContentControl Content="{Binding Labels}">
                <ContentControl.ContentTemplate>
                  <DataTemplate><TextBlock Text="{Binding Attack}" /></DataTemplate>
                </ContentControl.ContentTemplate>
              </ContentControl>
            </DataTemplate>
            """
        );

        var inferred = checks.Single(c => c.Path == "Attack");
        await Assert.That(inferred.ContextType).IsEqualTo("Sample.Ui.ItemViewModel");
        await Assert.That(inferred.Hops.Single().Path).IsEqualTo("Labels");
        await Assert.That(inferred.Hops.Single().IsCollection).IsFalse();
    }

    [Test]
    public async Task ItemTemplate_InfersTheCollectionElement()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <ItemsControl ItemsSource="{Binding Rows}">
                <ItemsControl.ItemTemplate>
                  <DataTemplate><TextBlock Text="{Binding Label}" /></DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </DataTemplate>
            """
        );

        var inferred = checks.Single(c => c.Path == "Label");
        await Assert.That(inferred.Hops.Single().Path).IsEqualTo("Rows");
        await Assert.That(inferred.Hops.Single().IsCollection).IsTrue();
    }

    [Test]
    public async Task TemplateBinding_ChecksTheTargetType()
    {
        var checks = Scan(
            """
            <Style TargetType="{x:Type ui:Checkbox}">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate>
                    <TextBlock Text="{TemplateBinding InfoTooltipText}" />
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.Checkbox", "InfoTooltipText")).IsTrue();
    }

    [Test]
    public async Task SetterTargetName_ChecksTheNamedElement_NotTheTargetType()
    {
        var checks = Scan(
            """
            <Style TargetType="{x:Type ui:Checkbox}">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate>
                    <Ellipse x:Name="Glyph" />
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsEnabled">
                        <Setter TargetName="Glyph" Property="Fill" />
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """
        );

        await Assert.That(Has(checks, "Noesis.Ellipse", "Fill")).IsTrue();
        await Assert.That(Has(checks, "Sample.Ui.Checkbox", "Fill")).IsFalse();
    }

    [Test]
    public async Task AmbiguousName_ResolvesToNothing()
    {
        var checks = Scan(
            """
            <Style TargetType="{x:Type ui:Checkbox}">
              <Ellipse x:Name="Glyph" />
              <Rectangle x:Name="Glyph" />
              <Trigger Property="IsEnabled">
                <Setter TargetName="Glyph" Property="Fill" />
              </Trigger>
            </Style>
            """
        );

        await Assert.That(checks.Any(c => c.Path == "Fill")).IsFalse();
    }

    [Test]
    public async Task RelativeSourceSelf_InsideACondition_UsesTheStyledType()
    {
        var checks = Scan(
            """
            <Style TargetType="{x:Type ui:Checkbox}">
              <MultiDataTrigger>
                <MultiDataTrigger.Conditions>
                  <Condition Binding="{Binding IsChecked, RelativeSource={RelativeSource Self}}" />
                </MultiDataTrigger.Conditions>
              </MultiDataTrigger>
            </Style>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.Checkbox", "IsChecked")).IsTrue();
    }

    [Test]
    public async Task DisplayMemberBinding_IsSilent()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <GridViewColumn DisplayMemberBinding="{Binding Rank}" />
            </DataTemplate>
            """
        );

        await Assert.That(checks).IsEmpty();
    }

    [Test]
    public async Task DataContextAttribute_ChecksAgainstTheParentScope()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <Grid DataContext="{Binding Labels}" />
            </DataTemplate>
            """
        );

        var check = checks.Single();
        await Assert.That(check.Path).IsEqualTo("Labels");
        await Assert.That(check.Hops).IsEmpty();
    }

    [Test]
    public async Task ElementNameDataContextHop_UsesTheNamedElementScope()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <Grid x:Name="Root">
                <Button Command="{Binding DataContext.SaveCommand, ElementName=Root}" />
              </Grid>
            </DataTemplate>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.ItemViewModel", "SaveCommand")).IsTrue();
    }

    [Test]
    public async Task ElementNameDataContextHop_AmbiguousName_IsSilent()
    {
        var checks = Scan(
            """
            <DataTemplate DataType="{x:Type ui:ItemViewModel}">
              <Grid x:Name="Root" />
              <Border x:Name="Root" />
              <Button Command="{Binding DataContext.SaveCommand, ElementName=Root}" />
            </DataTemplate>
            """
        );

        await Assert.That(checks.Any(c => c.Path == "SaveCommand")).IsFalse();
    }

    [Test]
    public async Task AnnotationMarker_TypesAKeyedTemplate()
    {
        var checks = Scan(
            """
            <DataTemplate x:Key="Row" ntk:DataType="ui:ItemViewModel">
              <TextBlock Text="{Binding DefIdInt}" />
            </DataTemplate>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.ItemViewModel", "DefIdInt")).IsTrue();
    }

    [Test]
    public async Task AnnotationMarker_AlsoWorksAsTheTemplatesFirstLine()
    {
        var checks = Scan(
            """
            <DataTemplate x:Key="Row">
              <StackPanel ntk:DataType="ui:ItemViewModel">
                <TextBlock Text="{Binding DefIdInt}" />
              </StackPanel>
            </DataTemplate>
            """
        );

        await Assert.That(Has(checks, "Sample.Ui.ItemViewModel", "DefIdInt")).IsTrue();
    }

    [Test]
    public async Task AnnotationMarker_DoesNotLeakToALaterSibling()
    {
        var checks = Scan(
            """
            <DataTemplate x:Key="Row" ntk:DataType="ui:ItemViewModel">
              <TextBlock Text="{Binding DefIdInt}" />
            </DataTemplate>
            <DataTemplate x:Key="Other">
              <TextBlock Text="{Binding NotOnItemViewModel}" />
            </DataTemplate>
            """
        );

        await Assert.That(checks.Any(c => c.Path == "NotOnItemViewModel")).IsFalse();
    }

    [Test]
    public async Task KeyedTemplate_WithoutAnnotation_IsSilent()
    {
        var checks = Scan(
            """
            <DataTemplate x:Key="Row">
              <TextBlock Text="{Binding DefIdInt}" />
            </DataTemplate>
            """
        );

        await Assert.That(checks).IsEmpty();
    }

    [Test]
    public async Task MalformedXaml_ProducesNothing()
    {
        var checks = BindingScanner.ScanAll("<Root><Unclosed></Root>").Checks;

        await Assert.That(checks).IsEmpty();
    }
}
