// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileMibcReferenceResolver
{
	internal static async Task<IReadOnlyList<string>> ResolveAsync(ProfileSessionContext context, CancellationToken cancellationToken)
	{
		using var document = await EvaluatePropertiesAsync(context, runtimeIdentifier: null, cancellationToken);
		var properties = document.RootElement.GetProperty("Properties");
		var evaluatedRid = properties.GetProperty("RuntimeIdentifier").GetString();
		var runtimeIdentifier = ResolveRuntimeIdentifier(
			evaluatedRid,
			properties.GetProperty("RuntimeIdentifiers").GetString(),
			context.Device);
		var roots = ReadOutputRoots(properties, context);
		var runtimeSpecificRoots = roots;
		var assemblyStage = ReadAssemblyStage(properties, context.ProfilePlatform);
		if (string.IsNullOrWhiteSpace(evaluatedRid))
		{
			using var ridDocument = await EvaluatePropertiesAsync(context, runtimeIdentifier, cancellationToken);
			var ridProperties = ridDocument.RootElement.GetProperty("Properties");
			runtimeSpecificRoots = ReadOutputRoots(ridProperties, context);
			assemblyStage = ReadAssemblyStage(ridProperties, context.ProfilePlatform);
		}

		ProfileCommandProcessHelpers.WriteVerbose(
			context.Formatter,
			context.UseJson,
			context.Verbose,
			$"MIBC references: framework={context.Framework}, configuration={context.Configuration}, runtimeIdentifier={runtimeIdentifier}, stage={assemblyStage}");
		return ResolveReferenceAssemblies(roots, runtimeIdentifier, assemblyStage, runtimeSpecificRoots);
	}

	static ProfileMibcAssemblyStage ReadAssemblyStage(JsonElement properties, string platform)
		=> ResolveAssemblyStage(
			platform,
			properties.GetProperty("PublishTrimmed").GetString(),
			properties.GetProperty("AndroidLinkMode").GetString(),
			properties.GetProperty("AndroidIncludeDebugSymbols").GetString());

	internal static ProfileMibcAssemblyStage ResolveAssemblyStage(
		string platform,
		string? publishTrimmed,
		string? androidLinkMode,
		string? androidIncludeDebugSymbols)
	{
		if (!string.Equals(publishTrimmed, "true", StringComparison.OrdinalIgnoreCase))
			return ProfileMibcAssemblyStage.Untrimmed;

		// Android's _RemoveRegisterAttribute target creates shrunk copies under these conditions.
		if (string.Equals(platform, Platforms.Android, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(androidLinkMode, "None", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(androidIncludeDebugSymbols, "true", StringComparison.OrdinalIgnoreCase))
			return ProfileMibcAssemblyStage.Shrunk;

		return ProfileMibcAssemblyStage.Linked;
	}

	static async Task<JsonDocument> EvaluatePropertiesAsync(
		ProfileSessionContext context,
		string? runtimeIdentifier,
		CancellationToken cancellationToken)
	{
		var result = await ProcessRunner.RunAsync(
			"dotnet",
			ProfileCommandArguments.BuildMibcPropertyArguments(context, runtimeIdentifier),
			context.Project.ProjectDirectory,
			timeout: TimeSpan.FromSeconds(30),
			environmentVariablesToRemove: ProfileCommand.s_msbuildSdkEnvVars,
			cancellationToken: cancellationToken);
		if (!result.Success)
			throw ProfileCommandProcessHelpers.CreateProcessFailureException("dotnet msbuild MIBC output evaluation", result);

		return JsonDocument.Parse(result.StandardOutput);
	}

	static string[] ReadOutputRoots(JsonElement properties, ProfileSessionContext context)
	{
		var roots = new[] { "IntermediateOutputPath", "OutputPath" }
			.Select(name => properties.GetProperty(name).GetString())
			.Where(static path => !string.IsNullOrWhiteSpace(path))
			.Select(path => Path.GetFullPath(path!, context.Project.ProjectDirectory))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		foreach (var root in roots)
		{
			var relativePath = Path.GetRelativePath(context.BuildWorkspace.Path, root);
			if (Path.IsPathRooted(relativePath) || relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
				throw new MauiToolException(ErrorCodes.InternalError, $"MIBC reference output '{root}' is outside the isolated profiling workspace.");
		}

		return roots;
	}

	internal static string ResolveRuntimeIdentifier(string? runtimeIdentifier, string? runtimeIdentifiers, Device device)
	{
		if (!string.IsNullOrWhiteSpace(runtimeIdentifier))
			return runtimeIdentifier.Trim();

		var projectRids = (runtimeIdentifiers ?? string.Empty)
			.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (projectRids.Length == 1)
			return projectRids[0];

		var deviceRids = device.RuntimeIdentifiers ?? [];
		if (deviceRids.Length == 0 && device.Platform == Platforms.Android)
		{
			deviceRids = AndroidEnvironment.GetRuntimeIdentifiers(device.Architecture)
				?? AndroidEnvironment.GetRuntimeIdentifiers(AndroidEnvironment.MapAbiToArchitecture(device.PlatformArchitecture)) ?? [];
		}
		if (deviceRids.Length == 0 && device.Platform == Platforms.iOS && device.IsEmulator)
			deviceRids = [PlatformDetector.IsArm64 ? "iossimulator-arm64" : "iossimulator-x64"];

		var match = deviceRids.FirstOrDefault(rid => projectRids.Contains(rid, StringComparer.OrdinalIgnoreCase));
		if (match is not null)
			return match;

		throw MauiToolException.UserActionRequired(
			ErrorCodes.InvalidArgument,
			$"MIBC conversion could not determine the app runtime identifier for device '{device.Name}'.",
			[
				"Set RuntimeIdentifier in the app project to the ABI used by the app, or ensure the device architecture is available.",
				"Then run 'maui profile startup --format mibc' again."
			]);
	}

	internal static IReadOnlyList<string> ResolveReferenceAssemblies(
		IReadOnlyList<string> candidateRoots,
		string runtimeIdentifier,
		ProfileMibcAssemblyStage assemblyStage,
		IReadOnlyList<string>? runtimeSpecificRoots = null)
	{
		var roots = (runtimeSpecificRoots ?? []).Select(static root => (Path: root, IsRuntimeSpecific: true))
			.Concat(candidateRoots.Select(static root => (Path: root, IsRuntimeSpecific: false)))
			.DistinctBy(static root => root.Path, StringComparer.OrdinalIgnoreCase);
		var assemblies = roots
			.Where(static root => Directory.Exists(root.Path))
			.SelectMany(root => Directory.EnumerateFiles(root.Path, "*.dll", SearchOption.AllDirectories)
				.Select(path =>
				{
					var directories = Path.GetRelativePath(root.Path, Path.GetDirectoryName(path)!)
						.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
					return new
					{
						Path = path,
						RuntimeIdentifier = directories.FirstOrDefault(IsRuntimeIdentifier) ?? (root.IsRuntimeSpecific ? runtimeIdentifier : null),
						IsReferenceOnly = directories.Any(static part => part.Equals("ref", StringComparison.OrdinalIgnoreCase)
							|| part.Equals("refint", StringComparison.OrdinalIgnoreCase)),
						Stage = directories.Any(static part => part.Equals("shrunk", StringComparison.OrdinalIgnoreCase))
							? ProfileMibcAssemblyStage.Shrunk
							: directories.Any(static part => part.Equals("linked", StringComparison.OrdinalIgnoreCase))
								? ProfileMibcAssemblyStage.Linked : ProfileMibcAssemblyStage.Untrimmed
					};
				}))
			.Where(static assembly => !assembly.IsReferenceOnly)
			.DistinctBy(static assembly => assembly.Path, StringComparer.OrdinalIgnoreCase)
			.OrderBy(static assembly => assembly.Path, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var matching = assemblies.Where(assembly => string.Equals(assembly.RuntimeIdentifier, runtimeIdentifier, StringComparison.OrdinalIgnoreCase)).ToArray();
		var candidates = matching.Length > 0
			? matching
			: assemblies.Where(static assembly => assembly.RuntimeIdentifier is null).ToArray();
		if (matching.Length == 0 && (runtimeSpecificRoots is not null
			|| assemblies.Any(static assembly => assembly.RuntimeIdentifier is not null)))
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DiagnosticsToolNotFound,
				$"MIBC conversion could not find reference assemblies for runtime identifier '{runtimeIdentifier}'.",
				[
					$"Ensure the app builds for '{runtimeIdentifier}' on the selected target framework.",
					"Then run 'maui profile startup --format mibc' again."
				]);
		}

		if (candidates.Length == 0)
			return [];

		var references = candidates.Where(assembly => assembly.Stage == assemblyStage).Select(static assembly => assembly.Path).ToArray();
		if (references.Length == 0)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DiagnosticsToolNotFound,
				$"MIBC conversion could not find '{assemblyStage.ToString().ToLowerInvariant()}' reference assemblies for runtime identifier '{runtimeIdentifier}'.",
				[
					"Check that the profiling build produced the configured assembly-processing stage's outputs.",
					"References from a different processing stage cannot be used for this trace."
				]);
		}

		return references;
	}

	static bool IsRuntimeIdentifier(string directory)
		=> directory.ToLowerInvariant() is "android-arm" or "android-arm64" or "android-x86" or "android-x64"
			or "ios-arm64" or "ios-arm" or "iossimulator-arm64" or "iossimulator-x64" or "iossimulator-x86";
}

internal enum ProfileMibcAssemblyStage
{
	Untrimmed,
	Linked,
	Shrunk
}
