using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaNotes;

public class BaristaComparisonProfileTests
{
	[Fact]
	public void ComparisonManifest_OnlyDisablesBackup_PreservingPermissionsQueriesAndProvider()
	{
		var normal = ReadProjectXml("Properties/AndroidManifest.xml");
		var comparison = ReadProjectXml("Properties/AndroidManifest.Comparison.xml");
		XNamespace android = "http://schemas.android.com/apk/res/android";

		Assert.Null(normal.Root.Element("application").Attribute(android + "allowBackup"));
		var backup = comparison.Root.Element("application").Attribute(android + "allowBackup");
		Assert.Equal("false", backup.Value);
		backup.Remove();
		normal.DescendantNodes().OfType<XComment>().Remove();
		comparison.DescendantNodes().OfType<XComment>().Remove();

		Assert.True(XNode.DeepEquals(normal.Root, comparison.Root),
			"Comparison manifests must retain the complete normal permissions, queries, provider and application attributes.");
	}

	[Theory]
	[InlineData("timing", "com.comet.sample.perf.baristanotes")]
	[InlineData("diagnostic", "com.comet.sample.perf.diag.baristanotes")]
	public void ComparisonIdentity_KeepsBaristaNotesAsTheFinalScreenSegment(string variant, string expectedId)
	{
		var project = ReadProjectXml("CometComposeProbe.csproj");
		var identity = project.Descendants("_BaristaComparisonApplicationId")
			.Single(element => element.Attribute("Condition").Value.Contains($"'{variant}'"));

		Assert.Equal(expectedId, identity.Value);
		Assert.Equal("baristanotes", CometSamples.SampleScreenResolver.Resolve(null, identity.Value));
		Assert.Single(project.Descendants("Target"), element =>
			(string)element.Attribute("Name") == "ValidateBaristaComparison");
	}

	[Fact]
	public void ComparisonRestorePaths_AreAppLocalAndOptIn()
	{
		var props = ReadProjectXml("Directory.Build.props");
		Assert.Equal(@"..\Directory.Build.props", props.Root.Element("Import").Attribute("Project").Value);
		var group = Assert.Single(props.Root.Elements("PropertyGroup"));
		Assert.Equal("'$(BaristaComparison)' == 'timing' or '$(BaristaComparison)' == 'diagnostic'",
			group.Attribute("Condition").Value);
		Assert.Equal("obj/barista-comparison-$(BaristaComparison)/", group.Element("BaseIntermediateOutputPath").Value);
		Assert.Equal("bin/barista-comparison-$(BaristaComparison)/", group.Element("BaseOutputPath").Value);
		Assert.Contains("bin/**;obj/**", group.Element("DefaultItemExcludes").Value);
		Assert.Empty(props.Descendants("PackageReference"));
	}

	static XDocument ReadProjectXml(string relativePath)
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			var projectDirectory = IOPath.Combine(directory.FullName, "sample", "CometComposeProbe");
			if (File.Exists(IOPath.Combine(projectDirectory, "CometComposeProbe.csproj")))
				return XDocument.Load(IOPath.Combine(projectDirectory, relativePath));
		}

		throw new DirectoryNotFoundException("Cannot locate the CometComposeProbe source tree.");
	}

	[Fact]
	public void LifecycleControl_IsCompiledOnlyForDiagnosticComparison()
	{
		var project = ReadProjectXml("CometComposeProbe.csproj");
		var removal = Assert.Single(project.Descendants("Compile"),
			element => (string)element.Attribute("Remove") == "BaristaNotes/Comparison/BaristaLifecycleControl.cs");
		Assert.Equal("'$(BaristaComparison)' != 'diagnostic'", removal.Parent.Attribute("Condition").Value);
		Assert.Empty(removal.Parent.Elements("PackageReference"));
	}

	[Fact]
	public void FixtureContractAndInputs_AreIncludedOnlyInComparisonProfiles()
	{
		var project = ReadProjectXml("CometComposeProbe.csproj");
		var group = Assert.Single(project.Root.Elements("ItemGroup"),
			element => element.Elements("ProjectReference").Any(reference =>
				((string)reference.Attribute("Include")).EndsWith("BaristaComparison.Contract.csproj")));
		Assert.Equal("'$(BaristaComparison)' == 'timing' or '$(BaristaComparison)' == 'diagnostic'",
			group.Attribute("Condition").Value);
		Assert.Equal(2, group.Elements("AndroidAsset").Count());
		Assert.Empty(group.Elements("PackageReference"));
		var contract = ReadProjectXml("../../tools/perf/baristanotes/Comet/Contract/BaristaComparison.Contract.csproj");
		Assert.Equal(new[] { "../../Shared/FixtureContract.cs", "../../Shared/FixtureValidation.cs", "../../Shared/FixtureStore.cs" },
			contract.Descendants("Compile").Select(element => (string)element.Attribute("Include")));
		Assert.Empty(contract.Descendants("PackageReference"));
	}
}
