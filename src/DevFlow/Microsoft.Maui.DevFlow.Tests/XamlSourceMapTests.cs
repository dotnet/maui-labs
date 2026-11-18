using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

namespace Microsoft.Maui.DevFlow.Tests;

public class XamlSourceMapTests
{
    [Theory]
    [InlineData("<ContentPage />")]
    [InlineData("<Label Text=\"日本語\" />\r\n")]
    public void ContentHash_Uses128BitsAndMatchesInspector(string xaml)
    {
        var hash = XamlSourceMap.ComputeContentHash(xaml);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(xaml)), 0, 16).ToLowerInvariant();

        Assert.Matches("^[0-9a-f]{32}$", hash);
        Assert.Equal(expected, hash);
        Assert.Equal(hash, XamlSourceMap.ComputeContentHash(xaml));
        Assert.Equal(hash, Microsoft.Maui.Cli.DevFlow.Inspector.XamlSourcePropertyEditor.ComputeSourceHash(xaml));
        Assert.Equal(hash, XamlSourceMap.Parse(xaml, "Page.xaml")!.ContentHash);
        Assert.NotEqual(hash, XamlSourceMap.ComputeContentHash(xaml + "\n"));
    }

    [Fact]
    public void Parse_UsesProjectWideNamespaces_WithoutChangingSourceLocationsOrHash()
    {
        const string xaml =
            "<ContentPage x:Class=\"Sample.TestPage\">\n" +
            "  <Grid>\n" +
            "    <Label AutomationId=\"MappedLabel\" Text=\"Mapped\" />\n" +
            "  </Grid>\n" +
            "</ContentPage>\n";

        var map = Assert.IsType<XamlSourceMap>(
            XamlSourceMap.Parse(xaml, "TestPage.xaml"));

        Assert.Equal(XamlSourceMap.ComputeContentHash(xaml), map.ContentHash);
        Assert.True(map.TryGet("", out var root));
        Assert.Equal(1, root.Line);
        Assert.Equal(2, root.Column);
        Assert.True(map.TryGet("0/0", out var label));
        Assert.Equal(3, label.Line);
        Assert.Equal(6, label.Column);
        Assert.Equal("MappedLabel", label.AutomationId);
    }

    [Fact]
    public void Parse_UsesProvidedProjectNamespacePrefixes()
    {
        const string xaml =
            "<ContentPage x:Class=\"Sample.TestPage\">\n" +
            "  <widgets:CustomView />\n" +
            "</ContentPage>\n";
        var namespaceMappings = new Dictionary<string, string>
        {
            ["widgets"] = "clr-namespace:Sample.Controls;assembly=Sample"
        };

        var map = Assert.IsType<XamlSourceMap>(
            XamlSourceMap.Parse(xaml, "TestPage.xaml", null, namespaceMappings));

        Assert.True(map.TryGet("0", out var customControl));
        Assert.Equal("Sample.Controls.CustomView", customControl.FullTypeName);
    }

    [Fact]
    public void Parse_PreservesInternalDtdEntityHandling()
    {
        const string xaml =
            "<!DOCTYPE ContentPage [<!ENTITY title \"Hello\">]>\n" +
            "<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\">\n" +
            "  <Label Text=\"&title;\" />\n" +
            "</ContentPage>";

        var map = Assert.IsType<XamlSourceMap>(
            XamlSourceMap.Parse(xaml, "TestPage.xaml"));

        Assert.True(map.TryGet("0", out var label));
        Assert.Equal("Hello", label.Text);
    }

    [Fact]
    public void ApplySourceMap_AttachesRootAndUniqueChildLocations()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="Sample.TestPage">
              <Grid>
                <Label AutomationId="MappedLabel" Text="Mapped" />
              </Grid>
            </ContentPage>
            """;
        var map = Assert.IsType<XamlSourceMap>(
            XamlSourceMap.Parse(xaml, "TestPage.xaml"));
        var walker = new VisualTreeWalker
        {
            SourceMapProvider = new StaticSourceMapProvider("Sample.TestPage", map)
        };
        var tree = new ElementInfo
        {
            Id = "page",
            Type = "ContentPage",
            FullType = "Sample.TestPage",
            Children =
            [
                new ElementInfo
                {
                    Id = "grid",
                    Type = "Grid",
                    FullType = "Microsoft.Maui.Controls.Grid",
                    Children =
                    [
                        new ElementInfo
                        {
                            Id = "label",
                            Type = "Label",
                            FullType = "Microsoft.Maui.Controls.Label",
                            AutomationId = "MappedLabel"
                        }
                    ]
                }
            ]
        };

        walker.ApplySourceMap([tree]);

        var grid = Assert.Single(tree.Children!);
        var label = Assert.Single(grid.Children!);
        Assert.Equal("TestPage.xaml", tree.SourceFile);
        Assert.Equal("TestPage.xaml", grid.SourceFile);
        Assert.Equal("TestPage.xaml", label.SourceFile);
        Assert.True(label.SourceLine!.Value > grid.SourceLine!.Value);
    }

    private sealed class StaticSourceMapProvider(
        string fullTypeName,
        XamlSourceMap map) : IXamlSourceMapProvider
    {
        public XamlSourceMap? GetMap(string requestedType)
            => requestedType == fullTypeName ? map : null;
    }
}
