using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Tests;

public class WpfHybridPackagingTests
{
    [Fact]
    public void GalleryProject_ProvidesWindowsProjectionsAndDeploysBlazorHostContent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var project = XDocument.Load(Path.Combine(directory.FullName,
            "platforms", "Windows.WPF", "samples", "Windows.WPF.Sample", "Windows.WPF.Sample.csproj"));

        var framework = Assert.Single(project.Descendants("TargetFramework")).Value;
        Assert.StartsWith("net10.0-windows10.0.", framework);
        var content = Assert.Single(project.Descendants("Content"),
            element => (string?)element.Attribute("Update") == @"wwwroot\**");
        Assert.Equal("PreserveNewest", (string?)content.Element("CopyToOutputDirectory"));
        Assert.Equal("PreserveNewest", (string?)content.Element("CopyToPublishDirectory"));
        var assetTarget = Assert.Single(project.Descendants("Target"),
            element => (string?)element.Attribute("Name") == "IncludeWpfBlazorStaticWebAssets");
        Assert.Contains("GetCopyToOutputDirectoryItems", (string?)assetTarget.Attribute("BeforeTargets"));
        Assert.Contains("GetCopyToPublishDirectoryItems", (string?)assetTarget.Attribute("BeforeTargets"));
        Assert.Equal("StaticWebAssetsPrepareForRun", (string?)assetTarget.Attribute("DependsOnTargets"));
        var assets = Assert.Single(assetTarget.Descendants("ContentWithTargetPath"));
        Assert.Equal("PreserveNewest", (string?)assets.Attribute("CopyToOutputDirectory"));
        Assert.Equal("PreserveNewest", (string?)assets.Attribute("CopyToPublishDirectory"));

        var hostPage = XDocument.Load(Path.Combine(directory.FullName,
            "platforms", "Windows.WPF", "samples", "Windows.WPF.Sample", "wwwroot", "index.html"));
        var bootScript = Assert.Single(hostPage.Descendants("script"),
            element => (string?)element.Attribute("src") == "_framework/blazor.webview.js");
        Assert.NotEqual("false", (string?)bootScript.Attribute("autostart"));
    }
}
