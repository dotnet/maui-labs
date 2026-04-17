// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileCommandBuildInjectionResolver
{
	internal static ProfilingBuildInjection? TryCreateBuildInjection(
		string exitControlHost,
		int exitControlPort,
		bool injectBootstrap,
		bool enableRuntimePgo = false,
		string? eventPipeOutputPath = null)
	{
		var targetsPath = TryResolveBuildAssetPath(ProfileCommand.StartupProfilingInjectionTargetsFileName);
		var assemblyPath = TryResolveBuildAssetPath(ProfileCommand.StartupProfilingAssemblyFileName);
		var sourcePath = TryResolveBuildAssetPath(ProfileCommand.StartupProfilingInjectionSourceFileName);

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

	static string? TryResolveBuildAssetPath(string fileName)
	{
		var baseDirectory = AppContext.BaseDirectory;
		var candidates = new[]
		{
			Path.Combine(baseDirectory, fileName),
			Path.Combine(baseDirectory, "Build", fileName)
		};

		return candidates.FirstOrDefault(File.Exists);
	}
}
