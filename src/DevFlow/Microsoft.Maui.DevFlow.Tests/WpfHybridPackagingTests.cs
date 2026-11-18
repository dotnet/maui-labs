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
        Assert.Equal("$(WpfSampleTargetFramework)", framework);
        var defaultFramework = Assert.Single(project.Descendants("WpfSampleTargetFramework"));
        Assert.Equal("'$(WpfSampleTargetFramework)' == ''", (string?)defaultFramework.Attribute("Condition"));
        Assert.StartsWith("net11.0-windows10.0.", defaultFramework.Value);
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

    [Fact]
    public void DevFlowWpfSample_RegistersHandlerAndDeploysAutostartingBlazorHost()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var sampleDir = Path.Combine(directory.FullName, "samples", "DevFlow.Sample.WPF");
        var project = XDocument.Load(Path.Combine(sampleDir, "DevFlow.Sample.WPF.csproj"));

        // Razor static web assets (_framework/blazor.webview.js) must reach the output wwwroot.
        var assetTarget = Assert.Single(project.Descendants("Target"),
            element => (string?)element.Attribute("Name") == "IncludeWpfBlazorStaticWebAssets");
        Assert.Equal("StaticWebAssetsPrepareForRun", (string?)assetTarget.Attribute("DependsOnTargets"));

        // The shared MAUI host page disables autostart (a CDP bridge starts Blazor); WPF has no
        // such bridge, so the sample must ship its own host page and not link the shared one.
        var sharedHost = Assert.Single(project.Descendants("Content"),
            element => ((string?)element.Attribute("Include"))?.Contains(@"DevFlow.Sample\wwwroot") == true);
        Assert.Contains("index.html", (string?)sharedHost.Attribute("Exclude"));
        var hostPage = File.ReadAllText(Path.Combine(sampleDir, "wwwroot", "index.html"));
        var bootScript = Assert.Single(System.Text.RegularExpressions.Regex.Matches(hostPage,
            @"<script[^>]*src=""_framework/blazor\.webview\.js""[^>]*>")).Value;
        Assert.DoesNotContain("autostart=\"false\"", bootScript);

        // net11 does not auto-register the WPF BlazorWebView handler.
        var program = File.ReadAllText(Path.Combine(sampleDir, "MauiProgram.cs"));
        Assert.Contains("UsePlatformHandler<Microsoft.AspNetCore.Components.WebView.Maui.WPF.BlazorWebViewHandler>", program);
    }
}
