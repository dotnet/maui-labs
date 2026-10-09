using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

namespace Microsoft.Maui.DevFlow.Tests;

public class XamlSourceMapTests
{
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
