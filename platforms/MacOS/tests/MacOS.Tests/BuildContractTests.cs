using System.Xml.Linq;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

public sealed class BuildContractTests
{
	[Fact]
	public void PackageProps_RegisterBackendAndResizetizerHooksBeforeMauiTargets()
	{
		var root = GetBuildContractsDirectory();
		var props = XDocument.Load(Path.Combine(root,
			"Microsoft.Maui.Platforms.MacOS.props"));
		var targets = XDocument.Load(Path.Combine(root,
			"Microsoft.Maui.Platforms.MacOS.targets"));

		Assert.Equal("macos-appkit", Element(props, "ResizetizerPlatformType")?.Value);
		Assert.Equal("macos", Element(props, "MauiActiveBackend")?.Value);
		Assert.Contains("_MacOSCollectProcessedMauiImages",
			Element(props, "ResizetizerAfterImageProcessingTargets")?.Value);
		Assert.Contains("_MacOSCollectProcessedMauiFonts",
			Element(props, "ResizetizerAfterFontProcessingTargets")?.Value);
		Assert.Contains("_MacOSCollectProcessedMauiAssets",
			Element(props, "ResizetizerAfterAssetProcessingTargets")?.Value);

		var platformFolder = Element(props, "MauiPlatformSpecificFolder");
		Assert.Equal("macos", platformFolder?.Attribute("TargetPlatformIdentifier")?.Value);
		Assert.Equal("macos", platformFolder?.Attribute("BackendIdentity")?.Value);
		Assert.Contains("MSBuildSourceProjectFile", targets.ToString(SaveOptions.DisableFormatting));
	}

	[Fact]
	public void ProcessedAssets_PreserveRawLogicalNamesAndFilenameDefaults()
	{
		var targets = XDocument.Load(Path.Combine(GetBuildContractsDirectory(),
			"Microsoft.Maui.Platforms.MacOS.targets"));
		var images = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSCollectProcessedMauiImages");
		var fonts = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSCollectProcessedMauiFonts");
		var assets = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSCollectProcessedMauiAssets");
		Assert.Contains("Normalize(System.Text.NormalizationForm.FormD)", images.ToString());
		Assert.Contains("Normalize(System.Text.NormalizationForm.FormD)", fonts.ToString());
		Assert.Contains("Normalize(System.Text.NormalizationForm.FormD)", assets.ToString());
		var compatibility = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSCollectMauiResources");
		Assert.Contains("Normalize(System.Text.NormalizationForm.FormD)", compatibility.ToString());
		Assert.Contains(images.Descendants(), element => element.Name.LocalName == "FileWrites" &&
			element.Attribute("Include")?.Value == "@(MauiProcessedImage)");
		Assert.Contains(fonts.Descendants(), element => element.Name.LocalName == "FileWrites" &&
			element.Attribute("Include")?.Value == "@(MauiProcessedFont)");
		var prepare = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSPrepareRawMauiAssets");
		Assert.Equal("ProcessMauiAssets", prepare.Attribute("BeforeTargets")?.Value);
		var rawName = prepare.Descendants().Single(element => element.Name.LocalName == "LogicalName");
		Assert.Equal("%(Filename)%(Extension)", rawName.Value);
		Assert.Contains("'%(MauiAsset.LogicalName)' == ''", rawName.Attribute("Condition")?.Value);

		var asset = assets.Descendants().Single(element =>
			element.Attribute("Include")?.Value == "@(MauiProcessedAsset)");
		var names = asset.Elements().Where(element => element.Name.LocalName == "LogicalName").ToArray();
		Assert.Contains(names, name =>
			name.Value == "%(MauiProcessedAsset.TargetPath)" &&
			name.Attribute("Condition")?.Value.Contains("StartsWith('wwwroot/')") == true);
		Assert.Contains(names, name =>
			name.Value == "%(MauiProcessedAsset.LogicalName)" &&
			name.Attribute("Condition")?.Value.Contains("!$([System.String]::Copy") == true);
		Assert.All(names.Where(name =>
			name.Value is not "%(MauiProcessedAsset.TargetPath)" and
			not "%(MauiProcessedAsset.LogicalName)"), name =>
			Assert.Contains("'%(MauiProcessedAsset.LogicalName)' == ''", name.Attribute("Condition")?.Value));
	}

	[Fact]
	public void StandardIconAndSplashItems_UseAppKitPackagingTargets()
	{
		var targets = XDocument.Load(Path.Combine(GetBuildContractsDirectory(),
			"Microsoft.Maui.Platforms.MacOS.targets"));

		var splash = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSPromoteMauiSplashScreens");
		Assert.Contains("ResizetizeCollectItems", splash.Attribute("BeforeTargets")?.Value);
		Assert.Contains(splash.Descendants(), element =>
			element.Name.LocalName == "MauiImage" &&
			element.Attribute("Include")?.Value == "@(MauiSplashScreen)" &&
			element.Attribute("IsAppSplashScreen")?.Value == "true");

		var icon = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSGenerateAppIcon");
		Assert.Contains(icon.Descendants(), element =>
			element.Name.LocalName == "BundleResource" &&
			element.Attribute("Remove")?.Value == "@(MauiIcon)");
		Assert.Contains(icon.Descendants(), element =>
			element.Name.LocalName == "BundleResource" &&
			element.Elements().Any(child =>
				child.Name.LocalName == "LogicalName" &&
				child.Value == "AppIcon.icns"));
		Assert.Contains(icon.Descendants(), element =>
			element.Name.LocalName == "PartialAppManifest");
	}

	[Fact]
	public void BundleResources_RegisterNativeOutputsBeforeIncrementalClean()
	{
		var targets = XDocument.Load(Path.Combine(GetBuildContractsDirectory(),
			"Microsoft.Maui.Platforms.MacOS.targets"));
		Assert.Contains("_MacOSTrackBundleResourceOutputs",
			Element(targets, "IncrementalCleanDependsOn")?.Value);
		Assert.Null(Element(targets, "IncrementalCleanDependsOn")?.Parent?.Attribute("Condition"));
		var track = targets.Descendants().Single(element =>
			element.Attribute("Name")?.Value == "_MacOSTrackBundleResourceOutputs");
		Assert.Equal("_ComputeBundleResourceOutputPaths", track.Attribute("DependsOnTargets")?.Value);
		Assert.Contains("'$(_CanOutputAppBundle)' == 'true'", track.Attribute("Condition")?.Value);
		Assert.Contains("'$(EnableMacOSMauiResourceMapping)' != 'false'", track.Attribute("Condition")?.Value);
		Assert.DoesNotContain("net11.", track.Attribute("Condition")?.Value);
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
		Assert.DoesNotContain("net11.", recover.Attribute("Condition")?.Value);
		var delete = recover.Elements().Single();
		Assert.Equal("%(_UnpackLibraryResourceItems.StampFile)", delete.Attribute("Files")?.Value);
		Assert.Equal("!Exists('%(_UnpackLibraryResourceItems.ItemsFile)')", delete.Attribute("Condition")?.Value);
	}

	[Fact]
	public void BlazorAssets_UseStandardMauiAssetPipelineWithNet10Compatibility()
	{
		var targets = XDocument.Load(Path.Combine(GetBuildContractsDirectory(),
			"Microsoft.Maui.Platforms.MacOS.BlazorWebView.targets"));
		var compatibility = Assert.Single(targets.Descendants(), element =>
			element.Name.LocalName == "Target");
		Assert.Contains("!$([System.String]::Copy('$(TargetFramework)').StartsWith('net11.'))",
			compatibility.Attribute("Condition")?.Value);
		Assert.Contains("'$(TargetPlatformIdentifier)' == 'macos'",
			compatibility.Attribute("Condition")?.Value);
		Assert.Equal("_CollectBundleResources", compatibility.Attribute("BeforeTargets")?.Value);
		Assert.Contains("StaticWebAssetsPrepareForRun", compatibility.Attribute("DependsOnTargets")?.Value);
		var compute = compatibility.Elements().Single(element =>
			element.Name.LocalName == "ComputeStaticWebAssetsTargetPaths");
		Assert.Equal("wwwroot", compute.Attribute("PathPrefix")?.Value);
		Assert.Contains("Normalize(System.Text.NormalizationForm.FormD)", compatibility.ToString());
	}

	static string GetBuildContractsDirectory() =>
		Path.Combine(AppContext.BaseDirectory, "BuildContracts");
}
