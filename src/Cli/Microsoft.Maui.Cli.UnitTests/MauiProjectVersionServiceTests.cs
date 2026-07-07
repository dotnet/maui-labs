// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;
using Microsoft.Maui.Cli.Services;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class MauiProjectVersionServiceTests
{
	[Fact]
	public async Task GetVersionInfoAsync_UseMauiProjectWithoutPackageReferences_UsesWorkloadVersion()
	{
		using var directory = TemporaryDirectory.Create();
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			  </PropertyGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var info = await service.GetVersionInfoAsync(projectPath);

		Assert.True(info.IsMauiProject);
		Assert.True(info.UsesMaui);
		Assert.Equal("10.0.41", info.EffectiveVersion);
		Assert.Empty(info.Packages);
	}

	[Fact]
	public async Task GetVersionInfoAsync_UseMauiProjectWithoutPackageReferences_IgnoresCentralPackageVersions()
	{
		using var directory = TemporaryDirectory.Create();
		directory.WriteFile("Directory.Packages.props", """
			<Project>
			  <ItemGroup>
			    <PackageVersion Include="Microsoft.Maui.Controls" Version="10.0.30" />
			  </ItemGroup>
			</Project>
			""");
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			  </PropertyGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var info = await service.GetVersionInfoAsync(projectPath);

		Assert.True(info.IsMauiProject);
		Assert.Equal("10.0.41", info.EffectiveVersion);
		Assert.Empty(info.Packages);
	}

	[Fact]
	public async Task SetVersionAsync_UseMauiProject_AddsMauiVersionProperty()
	{
		using var directory = TemporaryDirectory.Create();
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			  </PropertyGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var result = await service.SetVersionAsync(projectPath, "10.0.60", dryRun: false);

		Assert.True(result.Changed);
		Assert.Contains("<MauiVersion>10.0.60</MauiVersion>", File.ReadAllText(projectPath));
	}

	[Fact]
	public async Task SetVersionAsync_ConditionalFirstPropertyGroup_AddsMauiVersionToUnconditionalGroup()
	{
		using var directory = TemporaryDirectory.Create();
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
			    <UseMaui>true</UseMaui>
			  </PropertyGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var result = await service.SetVersionAsync(projectPath, "10.0.60", dryRun: false);
		var document = XDocument.Load(projectPath);
		var mauiVersionGroup = Assert.Single(document.Root!.Elements(),
			element => element.Elements().Any(child => child.Name.LocalName == "MauiVersion"));

		Assert.True(result.Changed);
		Assert.Null(mauiVersionGroup.Attribute("Condition"));
	}

	[Fact]
	public async Task SetVersionAsync_ConditionalMauiVersion_UpdatesConditionalAndAddsUnconditionalVersion()
	{
		using var directory = TemporaryDirectory.Create();
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
			    <UseMaui>true</UseMaui>
			    <MauiVersion>10.0.30</MauiVersion>
			  </PropertyGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var result = await service.SetVersionAsync(projectPath, "10.0.60", dryRun: false);
		var document = XDocument.Load(projectPath);
		var mauiVersionGroups = document.Root!.Elements()
			.Where(element => element.Elements().Any(child => child.Name.LocalName == "MauiVersion"))
			.ToList();

		Assert.True(result.Changed);
		Assert.Contains(mauiVersionGroups, element => element.Attribute("Condition") is null);
		Assert.All(document.Descendants().Where(element => element.Name.LocalName == "MauiVersion"),
			element => Assert.Equal("10.0.60", element.Value));
	}

	[Fact]
	public async Task SetVersionAsync_ProjectPackageReference_UpdatesExplicitVersion()
	{
		using var directory = TemporaryDirectory.Create();
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.41" />
			  </ItemGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var result = await service.SetVersionAsync(projectPath, "10.0.60", dryRun: false);

		Assert.True(result.Changed);
		Assert.Contains("Version=\"10.0.60\"", File.ReadAllText(projectPath));
	}

	[Fact]
	public async Task SetVersionAsync_CentralPackageVersion_UpdatesCentralFile()
	{
		using var directory = TemporaryDirectory.Create();
		directory.WriteFile("Directory.Packages.props", """
			<Project>
			  <PropertyGroup>
			    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageVersion Include="Microsoft.Maui.Controls" Version="10.0.41" />
			  </ItemGroup>
			</Project>
			""");
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Controls" />
			  </ItemGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var result = await service.SetVersionAsync(projectPath, "10.0.60", dryRun: false);

		Assert.True(result.Changed);
		Assert.Contains("Version=\"10.0.60\"", File.ReadAllText(Path.Combine(directory.Path, "Directory.Packages.props")));
	}

	[Fact]
	public async Task SetVersionAsync_ExplicitPackageAlreadyAtVersion_DoesNotUpdateCentralFile()
	{
		using var directory = TemporaryDirectory.Create();
		directory.WriteFile("Directory.Packages.props", """
			<Project>
			  <ItemGroup>
			    <PackageVersion Include="Microsoft.Maui.Controls" Version="10.0.30" />
			  </ItemGroup>
			</Project>
			""");
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			    <MauiVersion>10.0.60</MauiVersion>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.60" />
			  </ItemGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var result = await service.SetVersionAsync(projectPath, "10.0.60", dryRun: false);
		var centralContent = File.ReadAllText(Path.Combine(directory.Path, "Directory.Packages.props"));

		Assert.False(result.Changed);
		Assert.Contains("Version=\"10.0.30\"", centralContent);
	}

	[Fact]
	public async Task SetVersionAsync_CentralPackageManagementWithoutPackageVersion_AddsCentralVersion()
	{
		using var directory = TemporaryDirectory.Create();
		directory.WriteFile("Directory.Packages.props", """
			<Project>
			  <PropertyGroup>
			    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
			  </PropertyGroup>
			  <ItemGroup />
			</Project>
			""");
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Controls" />
			  </ItemGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var result = await service.SetVersionAsync(projectPath, "10.0.60", dryRun: false);
		var projectContent = File.ReadAllText(projectPath);
		var centralContent = File.ReadAllText(Path.Combine(directory.Path, "Directory.Packages.props"));

		Assert.True(result.Changed);
		Assert.DoesNotContain("PackageReference Include=\"Microsoft.Maui.Controls\" Version=", projectContent);
		Assert.Contains("<PackageVersion Include=\"Microsoft.Maui.Controls\" Version=\"10.0.60\" />", centralContent);
	}

	[Fact]
	public async Task GetVersionInfoAsync_CentralPackageVersionWithProperty_ResolvesEffectiveVersion()
	{
		using var directory = TemporaryDirectory.Create();
		Directory.CreateDirectory(Path.Combine(directory.Path, "eng"));
		directory.WriteFile("eng/Versions.props", """
			<Project>
			  <PropertyGroup>
			    <MicrosoftMauiControlsVersion>10.0.41</MicrosoftMauiControlsVersion>
			    <MicrosoftAspNetCoreComponentsWebViewMauiVersion>$(MicrosoftMauiControlsVersion)</MicrosoftAspNetCoreComponentsWebViewMauiVersion>
			  </PropertyGroup>
			</Project>
			""");
		directory.WriteFile("Directory.Packages.props", """
			<Project>
			  <PropertyGroup>
			    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageVersion Include="Microsoft.Maui.Controls" Version="$(MicrosoftMauiControlsVersion)" />
			    <PackageVersion Include="Microsoft.AspNetCore.Components.WebView.Maui" Version="$(MicrosoftAspNetCoreComponentsWebViewMauiVersion)" />
			  </ItemGroup>
			</Project>
			""");
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Controls" />
			    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Maui" />
			  </ItemGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var info = await service.GetVersionInfoAsync(projectPath);

		Assert.Equal("10.0.41", info.EffectiveVersion);
		Assert.DoesNotContain(info.Packages, package => package.ResolvedVersion is not null && package.ResolvedVersion != "10.0.41");
	}

	[Fact]
	public async Task GetVersionInfoAsync_NonMauiProjectWithCentralMauiVersions_IsNotMauiProject()
	{
		using var directory = TemporaryDirectory.Create();
		directory.WriteFile("Directory.Packages.props", """
			<Project>
			  <ItemGroup>
			    <PackageVersion Include="Microsoft.Maui.Controls" Version="10.0.41" />
			  </ItemGroup>
			</Project>
			""");
		var projectPath = directory.WriteFile("Tool.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			  </PropertyGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var info = await service.GetVersionInfoAsync(projectPath);

		Assert.False(info.IsMauiProject);
		Assert.Empty(info.Packages);
	}

	[Fact]
	public async Task GetVersionInfoAsync_CentralPackageVersions_OnlyIncludesPackagesReferencedByProject()
	{
		using var directory = TemporaryDirectory.Create();
		directory.WriteFile("Directory.Packages.props", """
			<Project>
			  <PropertyGroup>
			    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageVersion Include="Microsoft.Maui.Controls" Version="10.0.41" />
			    <PackageVersion Include="Microsoft.Maui.Essentials" Version="10.0.41" />
			  </ItemGroup>
			</Project>
			""");
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Controls" />
			  </ItemGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var info = await service.GetVersionInfoAsync(projectPath);

		Assert.True(info.IsMauiProject);
		Assert.Contains(info.Packages, package => package.PackageId == "Microsoft.Maui.Controls" && package.Source == "central");
		Assert.DoesNotContain(info.Packages, package => package.PackageId == "Microsoft.Maui.Essentials");
	}

	[Fact]
	public async Task UseWorkloadVersionAsync_RemovesProjectMauiVersionAndUsesWorkloadExpressionForPackages()
	{
		using var directory = TemporaryDirectory.Create();
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			    <MauiVersion>10.0.60</MauiVersion>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.60" />
			  </ItemGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var result = await service.UseWorkloadVersionAsync(projectPath, dryRun: false);
		var content = File.ReadAllText(projectPath);

		Assert.True(result.Changed);
		Assert.DoesNotContain("<MauiVersion>", content);
		Assert.Contains("Version=\"$(MauiVersion)\"", content);
	}

	[Fact]
	public async Task UseWorkloadVersionAsync_PackageOnlyProject_Throws()
	{
		using var directory = TemporaryDirectory.Create();
		var projectPath = directory.WriteFile("App.csproj", """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <MauiVersion>10.0.60</MauiVersion>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.60" />
			  </ItemGroup>
			</Project>
			""");
		var service = CreateService("10.0.41");

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.UseWorkloadVersionAsync(projectPath, dryRun: false));

		Assert.Contains("<UseMaui>true</UseMaui>", exception.Message);
		Assert.Contains("<MauiVersion>10.0.60</MauiVersion>", File.ReadAllText(projectPath));
	}

	[Fact]
	public void DiscoverProjectFile_MultipleProjects_ReturnsNull()
	{
		using var directory = TemporaryDirectory.Create();
		directory.WriteFile("App1.csproj", "<Project />");
		directory.WriteFile("App2.csproj", "<Project />");
		var service = CreateService("10.0.41");

		var projectPath = service.DiscoverProjectFile(directory.Path);

		Assert.Null(projectPath);
	}

	static MauiProjectVersionService CreateService(string workloadVersion) =>
		new(_ => Task.FromResult<string?>(workloadVersion));

	sealed class TemporaryDirectory : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maui-cli-tests", Guid.NewGuid().ToString("N"));

		TemporaryDirectory()
		{
			Directory.CreateDirectory(Path);
		}

		public static TemporaryDirectory Create() => new();

		public string WriteFile(string relativePath, string contents)
		{
			var path = System.IO.Path.Combine(Path, relativePath);
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
			File.WriteAllText(path, contents);
			return path;
		}

		public void Dispose()
		{
			if (Directory.Exists(Path))
				Directory.Delete(Path, recursive: true);
		}
	}
}
