using System.IO;
using System.Xml.Linq;

namespace HandlerTests;

public sealed class BuildContractTests
{
	[Fact]
	public void PackageTargets_EvaluateBackendContractsAfterConsumerProperties()
	{
		var root = Path.Combine(AppContext.BaseDirectory, "BuildContracts");
		var props = XDocument.Load(Path.Combine(root,
			"Microsoft.Maui.Platforms.Windows.WPF.props"));
		var targets = XDocument.Load(Path.Combine(root,
			"Microsoft.Maui.Platforms.Windows.WPF.targets"));

		Assert.Null(Element(props, "_WpfUseMaui11ResourcePipeline"));
		Assert.Null(Element(props, "MauiActiveBackend"));
		Assert.Equal("wpf", Element(targets, "MauiActiveBackend")?.Value);
		Assert.Contains("_WpfCollectProcessedMauiImages",
			Element(targets, "ResizetizerAfterImageProcessingTargets")?.Value);
		Assert.Contains("_WpfCollectProcessedMauiFonts",
			Element(targets, "ResizetizerAfterFontProcessingTargets")?.Value);
		Assert.Contains("_WpfCollectProcessedMauiAssets",
			Element(targets, "ResizetizerAfterAssetProcessingTargets")?.Value);

		var platformFolder = Element(targets, "MauiPlatformSpecificFolder");
		Assert.Equal("wpf", platformFolder?.Attribute("BackendIdentity")?.Value);
		Assert.Null(platformFolder?.Attribute("ActivationProperty"));
		Assert.Null(platformFolder?.Attribute("ActivationValue"));

		var text = targets.ToString(SaveOptions.DisableFormatting);
		Assert.Contains("BeforeTargets=\"GetCopyToOutputDirectoryItems;GetCopyToPublishDirectoryItems;ComputeFilesToPublish\"", text);
		Assert.Contains("_WpfPrepareMauiResources", text);
		Assert.Contains("MSBuildSourceProjectFile", text);
		Assert.DoesNotContain("_WpfProcessMauiIcon", text);
	}

	static XElement? Element(XDocument document, string localName) =>
		document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName);

}
