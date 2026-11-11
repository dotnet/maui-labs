using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

public sealed class BuildContractTests
{
	[Fact]
	public void PackageTargets_EvaluateBackendContractsAfterConsumerProperties()
	{
		var root = GetPlatformRoot();
		var props = XDocument.Load(Path.Combine(root, "src", "MacOS", "buildTransitive",
			"Microsoft.Maui.Platforms.MacOS.props"));
		var targets = XDocument.Load(Path.Combine(root, "src", "MacOS", "buildTransitive",
			"Microsoft.Maui.Platforms.MacOS.targets"));

		Assert.Null(Element(props, "ResizetizerPlatformType"));
		Assert.Null(Element(props, "MauiActiveBackend"));
		Assert.Equal("macos-appkit", Element(targets, "ResizetizerPlatformType")?.Value);
		Assert.Equal("macos", Element(targets, "MauiActiveBackend")?.Value);

		var platformFolder = Element(targets, "MauiPlatformSpecificFolder");
		Assert.Equal("macos", platformFolder?.Attribute("TargetPlatformIdentifier")?.Value);
		Assert.Equal("macos", platformFolder?.Attribute("BackendIdentity")?.Value);
		Assert.Contains("MSBuildSourceProjectFile", targets.ToString(SaveOptions.DisableFormatting));
	}

	[Fact]
	public void ProcessedAssets_PreserveRawLogicalNamesAndFilenameDefaults()
	{
		var targets = XDocument.Load(Path.Combine(GetPlatformRoot(), "src", "MacOS",
			"buildTransitive", "Microsoft.Maui.Platforms.MacOS.targets"));
		var prepare = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSPrepareRawMauiAssets");
		Assert.Equal("ProcessMauiAssets", prepare.Attribute("BeforeTargets")?.Value);
		var rawName = prepare.Descendants().Single(element => element.Name.LocalName == "LogicalName");
		Assert.Equal("%(Filename)%(Extension)", rawName.Value);
		Assert.Contains("'%(MauiAsset.LogicalName)' == ''", rawName.Attribute("Condition")?.Value);

		var asset = targets.Descendants().Single(element =>
			element.Attribute("Include")?.Value == "@(MauiProcessedAsset)");
		var names = asset.Elements().Where(element => element.Name.LocalName == "LogicalName").ToArray();
		Assert.Equal("%(MauiProcessedAsset.LogicalName)", names[0].Value);
		Assert.All(names.Skip(1), name =>
			Assert.Contains("'%(MauiProcessedAsset.LogicalName)' == ''", name.Attribute("Condition")?.Value));
	}

	static XElement? Element(XDocument document, string localName) =>
		document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName);

	static string GetPlatformRoot([CallerFilePath] string sourcePath = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}
