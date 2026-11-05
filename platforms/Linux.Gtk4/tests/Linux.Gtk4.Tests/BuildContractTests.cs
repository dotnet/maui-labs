using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class BuildContractTests
{
	[Fact]
	public void BuildAssets_UseVersionAppropriateContracts()
	{
		var props = XDocument.Load(Path.Combine(GetPlatformRoot(), "src", "Linux.Gtk4", "buildTransitive",
			"Microsoft.Maui.Platforms.Linux.Gtk4.props"));
		var targets = XDocument.Load(Path.Combine(GetPlatformRoot(), "src", "Linux.Gtk4", "buildTransitive",
			"Microsoft.Maui.Platforms.Linux.Gtk4.targets"));

		Assert.Null(Element(props, "ResizetizerPlatformType"));
		Assert.Null(Element(props, "MauiActiveBackend"));

		var platformFolder = Element(targets, "MauiPlatformSpecificFolder");
		Assert.Equal(@"Platforms\Linux\", platformFolder?.Attribute("Include")?.Value);
		Assert.Equal("linux", platformFolder?.Attribute("TargetPlatformIdentifier")?.Value);
		Assert.Equal("linux", platformFolder?.Attribute("BackendIdentity")?.Value);

		var targetText = targets.ToString(SaveOptions.DisableFormatting);
		Assert.Equal("linux", Element(targets, "ResizetizerPlatformType")?.Value);
		Assert.Equal("linux", Element(targets, "MauiActiveBackend")?.Value);
		Assert.Contains("@(MauiProcessedImage)", targetText);
		Assert.Contains("@(MauiProcessedFont)", targetText);
		Assert.Contains("@(MauiProcessedAsset)", targetText);
		Assert.Contains("DependsOnTargets=\"ProcessMauiAssets;ProcessMauiFonts;ResizetizeImages\"", targetText);
		Assert.Contains("BeforeTargets=\"GetCopyToOutputDirectoryItems;GetCopyToPublishDirectoryItems;ComputeFilesToPublish\"", targetText);
		Assert.Contains("_LinuxGtk4ProcessedResourcesCollected", targetText);
		Assert.Contains("MSBuildSourceProjectFile", targetText);
		Assert.Contains("Include=\"@(MauiImage)\"", targetText);
		Assert.Contains("Include=\"@(MauiFont)\"", targetText);
		Assert.Contains("Include=\"@(MauiAsset)\"", targetText);
		Assert.Contains("_LinuxGtk4UseMaui11Contracts", targetText);
		Assert.Contains("hicolor/scalable/apps/", targetText);
		Assert.Contains("MauiProcessedAsset.LogicalName", targetText);
		Assert.Contains("MauiProcessedAsset.Link", targetText);
	}

	static XElement? Element(XDocument document, string localName) =>
		document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName);

	static string GetPlatformRoot([CallerFilePath] string sourcePath = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}
