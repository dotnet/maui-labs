using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace HandlerTests;

public sealed class BuildContractTests
{
	[Fact]
	public void PackageTargets_EvaluateBackendContractsAfterConsumerProperties()
	{
		var root = GetPlatformRoot();
		var props = XDocument.Load(Path.Combine(root, "src", "Windows.WPF", "buildTransitive",
			"Microsoft.Maui.Platforms.Windows.WPF.props"));
		var targets = XDocument.Load(Path.Combine(root, "src", "Windows.WPF", "buildTransitive",
			"Microsoft.Maui.Platforms.Windows.WPF.targets"));

		Assert.Null(Element(props, "_WpfUseMaui11ResourcePipeline"));
		Assert.Null(Element(props, "MauiActiveBackend"));
		Assert.Equal("wpf", Element(targets, "MauiActiveBackend")?.Value);

		var platformFolder = Element(targets, "MauiPlatformSpecificFolder");
		Assert.Equal("wpf", platformFolder?.Attribute("BackendIdentity")?.Value);
		Assert.Equal("MauiActiveBackend", platformFolder?.Attribute("ActivationProperty")?.Value);

		var text = targets.ToString(SaveOptions.DisableFormatting);
		Assert.Contains("BeforeTargets=\"GetCopyToOutputDirectoryItems;GetCopyToPublishDirectoryItems;ComputeFilesToPublish\"", text);
		Assert.Contains("_WpfProcessedResourcesCollected", text);
		Assert.Contains("MSBuildSourceProjectFile", text);
	}

	static XElement? Element(XDocument document, string localName) =>
		document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName);

	static string GetPlatformRoot([CallerFilePath] string sourcePath = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}
