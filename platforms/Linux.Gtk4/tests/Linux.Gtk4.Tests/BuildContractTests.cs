using System.Xml.Linq;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class BuildContractTests
{
	[Fact]
	public void BuildAssets_UseVersionAppropriateContracts()
	{
		var props = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "BuildContracts",
			"Microsoft.Maui.Platforms.Linux.Gtk4.props"));
		var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "BuildContracts",
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
		Assert.Contains("ResizetizerAfterImageProcessingTargets", targetText);
		Assert.Contains("ResizetizerAfterFontProcessingTargets", targetText);
		Assert.Contains("ResizetizerAfterAssetProcessingTargets", targetText);
		Assert.Contains("DependsOnTargets=\"ProcessMauiAssets;ProcessMauiFonts;ResizetizeImages\"", targetText);
		Assert.Contains("BeforeTargets=\"GetCopyToOutputDirectoryItems;GetCopyToPublishDirectoryItems;ComputeFilesToPublish\"", targetText);
		Assert.DoesNotContain("MauiProcessedImage.IsAppIcon", targetText);
		Assert.DoesNotContain("MauiProcessedImage.TargetPath", targetText);
		Assert.DoesNotContain("MauiProcessedFont.TargetPath", targetText);
		Assert.Contains("Include=\"@(MauiImage)\"", targetText);
		Assert.Contains("Include=\"@(MauiFont)\"", targetText);
		Assert.Contains("Include=\"@(MauiAsset)\"", targetText);
		Assert.Contains("Include=\"@(MauiSplashScreen)\"", targetText);
		Assert.Contains("_LinuxGtk4UseMaui11Contracts", targetText);
		Assert.Contains("hicolor/scalable/apps/", targetText);
		Assert.Contains("MauiProcessedAsset.Link", targetText);
	}

	static XElement? Element(XDocument document, string localName) =>
		document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName);
}
