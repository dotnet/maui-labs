// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileCommandBuildInjectionResolver
{
	internal const string BuildAssetDirectoryName = "Build";

	internal static ProfilingBuildInjection? TryCreateBuildInjection(
		string exitControlHost,
		int exitControlPort,
		bool injectBootstrap,
		bool enableRuntimePgo = false,
		string? eventPipeOutputPath = null)
		=> TryCreateBuildInjection(
			AppContext.BaseDirectory,
			exitControlHost,
			exitControlPort,
			injectBootstrap,
			enableRuntimePgo,
			eventPipeOutputPath);

	internal static ProfilingBuildInjection? TryCreateBuildInjection(
		string baseDirectory,
		string exitControlHost,
		int exitControlPort,
		bool injectBootstrap,
		bool enableRuntimePgo = false,
		string? eventPipeOutputPath = null)
	{
		var targetsPath = TryResolveBuildAssetPath(baseDirectory, ProfileCommand.ProfilingHelperInjectionTargetsFileName);
		var assemblyPath = TryResolveInjectedAssemblyPath(baseDirectory);
		var sourcePath = TryResolveBuildAssetPath(baseDirectory, ProfileCommand.ProfilingHelperInjectionSourceFileName);

		if (targetsPath is null || assemblyPath is null || sourcePath is null)
			return null;

		return new ProfilingBuildInjection(
			targetsPath,
			assemblyPath,
			exitControlHost,
			exitControlPort,
			injectBootstrap,
			enableRuntimePgo,
			eventPipeOutputPath);
	}

	// The helper injected into the app must be the down-level build the CLI ships in Build/,
	// not the copy next to the CLI: that one matches the CLI's own TFM (net11.0 for the net11.0
	// tool) and would add a System.Runtime 11 reference to a net10.0 app build.
	static string? TryResolveInjectedAssemblyPath(string baseDirectory)
	{
		var path = Path.Combine(baseDirectory, BuildAssetDirectoryName, ProfileCommand.ProfilingHelperAssemblyFileName);
		return File.Exists(path) ? path : null;
	}

	static string? TryResolveBuildAssetPath(string baseDirectory, string fileName)
	{
		var candidates = new[]
		{
			Path.Combine(baseDirectory, fileName),
			Path.Combine(baseDirectory, BuildAssetDirectoryName, fileName)
		};

		return candidates.FirstOrDefault(File.Exists);
	}
}
