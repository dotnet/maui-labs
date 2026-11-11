using System.Xml.Linq;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

public sealed class BuildContractTests
{
	[Fact]
	public void PackageTargets_EvaluateBackendContractsAfterConsumerProperties()
	{
		var root = GetBuildContractsDirectory();
		var props = XDocument.Load(Path.Combine(root,
			"Microsoft.Maui.Platforms.MacOS.props"));
		var targets = XDocument.Load(Path.Combine(root,
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
		var targets = XDocument.Load(Path.Combine(GetBuildContractsDirectory(),
			"Microsoft.Maui.Platforms.MacOS.targets"));
		var collect = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSCollectProcessedMauiResources");
		Assert.Contains("ResizetizeImages", collect.Attribute("DependsOnTargets")?.Value);
		Assert.Null(Element(targets, "ResizetizerAfterImageProcessingTargets"));
		Assert.Contains(collect.Descendants(), element => element.Name.LocalName == "FileWrites" &&
			element.Attribute("Include")?.Value == "@(MauiProcessedImage);@(MauiProcessedFont)");
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

	[Fact]
	public void BundleResources_RegisterNativeOutputsBeforeIncrementalClean()
	{
		var targets = XDocument.Load(Path.Combine(GetBuildContractsDirectory(),
			"Microsoft.Maui.Platforms.MacOS.targets"));
		Assert.Contains("_MacOSTrackBundleResourceOutputs",
			Element(targets, "IncrementalCleanDependsOn")?.Value);
		var track = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSTrackBundleResourceOutputs");
		Assert.Equal("_ComputeBundleResourceOutputPaths", track.Attribute("DependsOnTargets")?.Value);
		Assert.Contains("'$(_CanOutputAppBundle)' == 'true'", track.Attribute("Condition")?.Value);
		Assert.Contains("'$(EnableMacOSMauiResourceMapping)' != 'false'", track.Attribute("Condition")?.Value);
		Assert.Equal("@(_BundleResourceWithOutputPath -> '%(OutputPath)')",
			track.Elements().Single().Elements().Single().Attribute("Include")?.Value);
	}

	static XElement? Element(XDocument document, string localName) =>
		document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName);

	[Fact]
	public void LibraryResources_MissingCacheInvalidatesOnlyItsNativeStamp()
	{
		var targets = XDocument.Load(Path.Combine(GetBuildContractsDirectory(),
			"Microsoft.Maui.Platforms.MacOS.targets"));
		var recover = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSRecoverLibraryResourceCaches");
		Assert.Equal("_BeforeUnpackLibraryResources", recover.Attribute("BeforeTargets")?.Value);
		Assert.Equal("_PrepareUnpackLibraryResources", recover.Attribute("DependsOnTargets")?.Value);
		Assert.Contains("'$(_CanOutputAppBundle)' == 'true'", recover.Attribute("Condition")?.Value);
		Assert.Contains("'$(EnableMacOSMauiResourceMapping)' != 'false'", recover.Attribute("Condition")?.Value);
		var delete = recover.Elements().Single();
		Assert.Equal("%(_UnpackLibraryResourceItems.StampFile)", delete.Attribute("Files")?.Value);
		Assert.Equal("!Exists('%(_UnpackLibraryResourceItems.ItemsFile)')", delete.Attribute("Condition")?.Value);
	}

	[Fact]
	public void BlazorAssets_ReplaceMappedResourcesWithCanonicalWebPaths()
	{
		var targets = XDocument.Load(Path.Combine(GetBuildContractsDirectory(),
			"Microsoft.Maui.Platforms.MacOS.BlazorWebView.targets"));
		var bridge = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_IncludeBlazorWebViewBundleResources");
		Assert.Contains("_MacOSCollectProcessedMauiResources", bridge.Attribute("DependsOnTargets")?.Value);
		var remove = bridge.Descendants().Single(element => element.Attribute("Remove") is not null);
		Assert.Equal("FullPath", remove.Attribute("MatchOnMetadata")?.Value);
		Assert.Equal("PathLike", remove.Attribute("MatchOnMetadataOptions")?.Value);
		var include = bridge.Descendants().Single(element => element.Name.LocalName == "BundleResource" &&
			element.Attribute("Include") is not null);
		Assert.Equal("%(_BlazorBundleResourceWithTargetPath.TargetPath)", include.Attribute("LogicalName")?.Value);
	}

	static string GetBuildContractsDirectory() =>
		Path.Combine(AppContext.BaseDirectory, "BuildContracts");
}
