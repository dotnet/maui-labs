// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileCommandDiagnostics
{
	internal static void ValidateDnxAvailable()
	{
		var hasDnx = ProcessRunner.GetCommandPath("dnx") is not null;
		var hasDotnetTrace = CanResolveDiagnosticsTool(
			FindInstalledDotnetToolCommand("dotnet-trace"),
			FindCachedDotnetToolDll("dotnet-trace"));
		var hasDotnetDsrouter = CanResolveDiagnosticsTool(
			FindInstalledDotnetToolCommand("dotnet-dsrouter"),
			FindCachedDotnetToolDll("dotnet-dsrouter"));

		if (CanUseDiagnosticsTooling(hasDnx, hasDotnetTrace, hasDotnetDsrouter))
		{
			return;
		}

		throw MauiToolException.UserActionRequired(
			ErrorCodes.DiagnosticsToolNotFound,
			"Neither 'dnx' nor the required dotnet diagnostics tools were found.",
			[
				"Install the global tools: `dotnet tool install -g dotnet-trace` and `dotnet tool install -g dotnet-dsrouter`.",
				"Or use a .NET 10 SDK with `dnx` available on PATH: https://dot.net/download"
			]);
	}

	internal static void ConfigureDotnetToolStartInfo(ProcessStartInfo startInfo, string packageId, IReadOnlyList<string> toolArgs, out string commandLine)
	{
		var installedToolPath = FindInstalledDotnetToolCommand(packageId);
		if (installedToolPath is not null)
		{
			startInfo.FileName = installedToolPath;
			foreach (var arg in toolArgs)
				startInfo.ArgumentList.Add(arg);

			commandLine = ProfileCommandProcessHelpers.FormatCommandLine(installedToolPath, [.. toolArgs]);
			return;
		}

		var cachedToolDll = FindCachedDotnetToolDll(packageId);
		if (cachedToolDll is not null)
		{
			startInfo.FileName = "dotnet";
			startInfo.ArgumentList.Add(cachedToolDll);
			foreach (var arg in toolArgs)
				startInfo.ArgumentList.Add(arg);

			commandLine = ProfileCommandProcessHelpers.FormatCommandLine("dotnet", [cachedToolDll, .. toolArgs]);
			return;
		}

		startInfo.FileName = "dnx";
		startInfo.ArgumentList.Add("-y");
		startInfo.ArgumentList.Add(packageId);
		startInfo.ArgumentList.Add("--");
		foreach (var arg in toolArgs)
			startInfo.ArgumentList.Add(arg);

		commandLine = ProfileCommandProcessHelpers.FormatCommandLine("dnx", ["-y", packageId, "--", .. toolArgs]);
	}

	internal static bool CanResolveDiagnosticsTool(string? installedToolPath, string? cachedToolDll)
		=> !string.IsNullOrWhiteSpace(installedToolPath) || !string.IsNullOrWhiteSpace(cachedToolDll);

	internal static bool CanUseDiagnosticsTooling(bool hasDnx, bool hasDotnetTrace, bool hasDotnetDsrouter)
		=> hasDnx || (hasDotnetTrace && hasDotnetDsrouter);

	static string? FindInstalledDotnetToolCommand(string packageId)
	{
		var commandPath = ProcessRunner.GetCommandPath(packageId);
		if (!string.IsNullOrWhiteSpace(commandPath))
			return commandPath;

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			return null;

		var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
		var candidate = Path.Combine(userProfile, ".dotnet", "tools", packageId + extension);
		return File.Exists(candidate) ? candidate : null;
	}

	static string? FindCachedDotnetToolDll(string packageId)
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			return null;

		var packageRoot = Path.Combine(userProfile, ".nuget", "packages", packageId.ToLowerInvariant());
		if (!Directory.Exists(packageRoot))
			return null;

		var versionDirectories = Directory
			.GetDirectories(packageRoot)
			.OrderByDescending(path => TryParsePackageVersion(Path.GetFileName(path), out var version) ? version : new Version(0, 0))
			.ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

		foreach (var versionDirectory in versionDirectories)
		{
			var directPath = Path.Combine(versionDirectory, "tools", "net8.0", "any", $"{packageId}.dll");
			if (File.Exists(directPath))
				return directPath;

			var candidate = Directory
				.EnumerateFiles(versionDirectory, $"{packageId}.dll", SearchOption.AllDirectories)
				.FirstOrDefault(path =>
					path.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
					path.Contains($"{Path.AltDirectorySeparatorChar}tools{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
			if (candidate is not null)
				return candidate;
		}

		return null;
	}

	static bool TryParsePackageVersion(string? value, out Version version)
	{
		var success = Version.TryParse(value, out var parsedVersion);
		version = parsedVersion ?? new Version(0, 0);
		return success;
	}
}
