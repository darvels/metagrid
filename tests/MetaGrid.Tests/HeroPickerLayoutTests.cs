using System.Xml.Linq;

namespace MetaGrid.Tests;

public sealed class HeroPickerLayoutTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument Layout() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "HeroPicker.xaml"));
    private static XElement Named(XDocument document, string name)
        => document.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    [Fact]
    public void TextInput_IsEditable_AndHasNoMouseToggleOrFocusRedirect()
    {
        var input = Named(Layout(), "GuideHeroSearch");
        Assert.Equal("TextBox", input.Name.LocalName);
        Assert.DoesNotContain(input.Attributes(), attribute => attribute.Name.LocalName.Contains("Mouse") || attribute.Name.LocalName.Contains("Focus"));
        Assert.Null(input.Attribute("IsReadOnly"));
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", (string?)input.Attribute("Text"));
        Assert.Null(input.Attribute("IsEnabled"));
        Assert.Null(input.Attribute("IsChecked"));
    }

    [Fact]
    public void PickerAndArrow_AreCompactSeparateHitTargets()
    {
        var host = Named(Layout(), "GuidePickerHost");
        Assert.Equal("240", (string?)host.Attribute("Width"));
        Assert.Equal("200", (string?)host.Attribute("MinWidth"));
        Assert.Equal("240", (string?)host.Attribute("MaxWidth"));
        Assert.Equal("Left", (string?)host.Attribute("HorizontalAlignment"));
        var columns = host.Elements().First(element => element.Name.LocalName == "Grid.ColumnDefinitions");
        Assert.Equal("24", (string?)columns.Elements().Last().Attribute("Width"));
        var arrow = host.Elements().Single(element => element.Name.LocalName == "ToggleButton");
        Assert.Contains("IsGuidePickerOpen", (string?)arrow.Attribute("IsChecked"));
        Assert.NotNull(arrow.Attribute("AutomationProperties.Name"));
    }

    [Theory]
    [InlineData(607, false)]
    [InlineData(608, true)]
    [InlineData(609, true)]
    [InlineData(690, true)]
    [InlineData(830, true)]
    public void ResponsiveColumns_UseContentBudget(double width, bool split)
    {
        Assert.Equal(split, MetaGrid.UI.Services.GuidesLayoutMetrics.IsSplit(width));
        if (split) Assert.True(MetaGrid.UI.Services.GuidesLayoutMetrics.EditorWidth(width) >= 343);
    }

    [Fact]
    public void SearchPlaceholder_UsesSamePaddedTemplateAreaAsEditor()
    {
        var theme = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PickerTheme.xaml"));
        var style = theme.Descendants().Single(e => (string?)e.Attribute(X + "Key") == "GuideSearchTextBoxStyle");
        var content = style.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "PART_ContentHost");
        var placeholder = style.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "SearchPlaceholder");
        Assert.Same(content.Parent, placeholder.Parent);
        Assert.Equal("0", (string?)content.Attribute("Margin"));
        Assert.Equal("0", (string?)content.Attribute("Padding"));
        Assert.Equal("{TemplateBinding Padding}", (string?)placeholder.Attribute("Margin"));
        Assert.Null(content.Parent!.Parent!.Attribute("Padding"));
    }

    [Fact]
    public void Popup_DoesNotCaptureTextInput_AndResultsRemainBounded()
    {
        var layout = Layout();
        var popup = Named(layout, "GuideHeroPopup");
        Assert.Equal("True", (string?)popup.Attribute("StaysOpen"));
        Assert.Equal("GuidePickerOutsideMouseDown", (string?)layout.Root!.Attribute("PreviewMouseDown"));
        Assert.Equal("GuidePickerWindowDeactivated", (string?)layout.Root.Attribute("Deactivated"));
        Assert.Equal("330", (string?)popup.Elements().Single().Attribute("MaxHeight"));
        var list = Named(layout, "GuideHeroResults");
        Assert.Equal("310", (string?)list.Attribute("MaxHeight"));
        Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));
        Assert.Equal("True", (string?)list.Attribute("VirtualizingPanel.IsVirtualizing"));
        Assert.Equal("GuidePickerKeyDown", (string?)list.Attribute("PreviewKeyDown"));
        Assert.Equal("GuidePickerKeyDown", (string?)Named(layout, "GuidePickerHost").Attribute("PreviewKeyDown"));
    }

    [Fact]
    public void RoleChips_AreContentSizedAndCompactWithoutSmallerLabels()
    {
        var layout = Layout();
        var theme = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PickerTheme.xaml"));
        var style = theme.Descendants().Single(e => (string?)e.Attribute(X + "Key") == "GuideRoleChipStyle");
        var setters = style.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
        Assert.DoesNotContain(setters, e => (string?)e.Attribute("Property") is "Width" or "FontSize");
        Assert.Contains(setters, e => (string?)e.Attribute("Property") == "Padding" && (string?)e.Attribute("Value") == "10,9");
        Assert.Contains(setters, e => (string?)e.Attribute("Property") == "Margin" && (string?)e.Attribute("Value") == "0,0,6,8");
        var card = Named(layout, "GuideDetailsPanel");
        Assert.Equal("12,16", (string?)card.Attribute("Padding"));
        var roles = card.Descendants().Single(e => (string?)e.Attribute("ItemsSource") == "{Binding GuideRoleOptions}");
        Assert.Contains(roles.Descendants(), e => e.Name.LocalName == "WrapPanel");
        Assert.Contains(roles.Descendants(), e => e.Name.LocalName == "TextBlock" && (string?)e.Attribute("FontSize") == "13");
    }
}
