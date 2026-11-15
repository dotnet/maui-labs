// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Maui.Cli.Commands;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class ProfileCommandBuildInjectionResolverTests : IDisposable
{
	// The lowest .NET major the injected helper may depend on. Apps on this version or any newer
	// one (including the -android/-ios/-maccatalyst platform TFMs) can compile against it.
	const int InjectedHelperMaxRuntimeMajorVersion = 9;

	readonly string _root = Path.Combine(Path.GetTempPath(), $"maui-profile-injection-tests-{Guid.NewGuid():N}");

	[Fact]
	public void TryCreateBuildInjection_UsesDownLevelHelperFromBuildDirectory()
	{
		WriteFile(ProfileCommand.ProfilingHelperInjectionTargetsFileName, inBuildDirectory: true);
		WriteFile(ProfileCommand.ProfilingHelperInjectionSourceFileName, inBuildDirectory: true);
		WriteFile(ProfileCommand.ProfilingHelperAssemblyFileName, inBuildDirectory: false);
		var injectedHelper = WriteFile(ProfileCommand.ProfilingHelperAssemblyFileName, inBuildDirectory: true);

		var injection = ProfileCommandBuildInjectionResolver.TryCreateBuildInjection(_root, "127.0.0.1", 9001, injectBootstrap: true);

		Assert.NotNull(injection);
		Assert.Equal(injectedHelper, injection.AssemblyPath);
	}

	[Fact]
	public void TryCreateBuildInjection_DoesNotFallBackToHelperNextToCli()
	{
		WriteFile(ProfileCommand.ProfilingHelperInjectionTargetsFileName, inBuildDirectory: true);
		WriteFile(ProfileCommand.ProfilingHelperInjectionSourceFileName, inBuildDirectory: true);
		WriteFile(ProfileCommand.ProfilingHelperAssemblyFileName, inBuildDirectory: false);

		var injection = ProfileCommandBuildInjectionResolver.TryCreateBuildInjection(_root, "127.0.0.1", 9001, injectBootstrap: true);

		Assert.Null(injection);
	}

	[Fact]
	public void TryCreateBuildInjection_FromCliOutput_ResolvesShippedBuildAssets()
	{
		var injection = ProfileCommandBuildInjectionResolver.TryCreateBuildInjection("127.0.0.1", 9001, injectBootstrap: true);

		Assert.NotNull(injection);
		Assert.Equal(
			Path.Combine(AppContext.BaseDirectory, ProfileCommandBuildInjectionResolver.BuildAssetDirectoryName, ProfileCommand.ProfilingHelperAssemblyFileName),
			injection.AssemblyPath);
		Assert.True(File.Exists(injection.TargetsPath));
	}

	[Fact]
	public void ShippedInjectedHelper_DoesNotReferenceNewerRuntimeThanLowestSupportedApp()
	{
		var helperPath = Path.Combine(
			AppContext.BaseDirectory,
			ProfileCommandBuildInjectionResolver.BuildAssetDirectoryName,
			ProfileCommand.ProfilingHelperAssemblyFileName);

		using var stream = File.OpenRead(helperPath);
		using var peReader = new PEReader(stream);
		var metadata = peReader.GetMetadataReader();
		var frameworkReferences = metadata.AssemblyReferences
			.Select(metadata.GetAssemblyReference)
			.Where(reference => metadata.GetString(reference.Name).StartsWith("System.", StringComparison.Ordinal))
			.ToList();

		Assert.NotEmpty(frameworkReferences);
		Assert.All(
			frameworkReferences,
			reference => Assert.True(
				reference.Version.Major <= InjectedHelperMaxRuntimeMajorVersion,
				$"{metadata.GetString(reference.Name)} {reference.Version} is newer than .NET {InjectedHelperMaxRuntimeMajorVersion}."));
	}

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}

	string WriteFile(string fileName, bool inBuildDirectory)
	{
		var directory = inBuildDirectory
			? Path.Combine(_root, ProfileCommandBuildInjectionResolver.BuildAssetDirectoryName)
			: _root;
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, fileName);
		File.WriteAllText(path, string.Empty);
		return path;
	}
}
