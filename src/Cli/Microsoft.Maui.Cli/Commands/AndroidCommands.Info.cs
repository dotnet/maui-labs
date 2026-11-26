// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Providers.Android;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AndroidCommands
{
	static Command CreateInfoCommand()
	{
		var command = new Command("info", "Display Android development environment summary");
		command.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
		{
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var formatter = Program.GetFormatter(parseResult);

			try
			{
				var androidProvider = GetAndroidProvider(parseResult);
				var jdkManager = Program.JdkManager;
				var warnings = new List<string>();
				var sdkPath = androidProvider.SdkPath;
				var sdkPathSource = !string.IsNullOrWhiteSpace(parseResult.GetValue(SdkOption))
					? "command-line"
					: androidProvider.GetSdkPathSource();
				var jdkPath = androidProvider.JdkPath;
				var jdkVersion = PathsEqual(jdkPath, jdkManager.DetectedJdkPath)
					? jdkManager.DetectedJdkVersion
					: JdkManager.GetJdkVersion(jdkPath);
				var requiresElevation = androidProvider.SdkPathRequiresElevation;

				// Gather license status
				bool? licensesAccepted = null;
				if (androidProvider.IsSdkInstalled)
				{
					try
					{
						licensesAccepted = await androidProvider.AreLicensesAcceptedAsync(cancellationToken);
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						System.Diagnostics.Trace.WriteLine($"Android license check failed: {ex.Message}");
						warnings.Add($"Could not determine Android SDK license status: {FirstLine(ex.Message)}");
					}
				}

				IReadOnlyList<ApiLevelInfo>? installedApiLevels = [];
				IReadOnlyList<string>? buildTools = [];
				if (androidProvider.IsSdkInstalled)
				{
					try
					{
						var packages = ReadInstalledPackagesFromDisk(sdkPath!, cancellationToken);
						(installedApiLevels, buildTools) = GroupInstalledPackages(packages);
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						System.Diagnostics.Trace.WriteLine($"Android SDK directory inventory failed: {ex.Message}");
						warnings.Add($"Could not inspect installed Android packages: {FirstLine(ex.Message)}");
						installedApiLevels = null;
						buildTools = null;
					}
				}

				// Gather tool paths
				var toolPaths = androidProvider.GetToolPaths();

				var info = new AndroidEnvironmentInfo
				{
					SdkPath = sdkPath,
					SdkPathSource = sdkPathSource,
					JdkPath = jdkPath,
					JdkVersion = jdkVersion?.ToString(),
					LicensesAccepted = licensesAccepted,
					RequiresElevation = requiresElevation,
					InstalledApiLevels = installedApiLevels,
					BuildTools = buildTools,
					Warnings = warnings,
					Tools = toolPaths
				};

				if (useJson)
				{
					formatter.Write(info);
				}
				else
				{
					WriteHumanReadableInfo(formatter, info);
				}
				return 0;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return command;
	}

	static string FirstLine(string value)
		=> value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;

	static bool PathsEqual(string? left, string? right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
			return false;

		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		return string.Equals(
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
			comparison);
	}

	internal static (IReadOnlyList<ApiLevelInfo> ApiLevels, IReadOnlyList<string> BuildTools) GroupInstalledPackages(
		IEnumerable<SdkPackage> packages)
	{
		var platformApis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var buildTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var systemImagesByApi = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

		foreach (var pkg in packages)
		{
			if (pkg.Path.StartsWith("platforms;android-", StringComparison.Ordinal))
			{
				var api = pkg.Path["platforms;android-".Length..];
				if (api.Length > 0)
					platformApis.Add(api);
			}
			else if (pkg.Path.StartsWith("build-tools;", StringComparison.Ordinal))
			{
				var version = pkg.Path["build-tools;".Length..];
				if (version.Length > 0)
					buildTools.Add(version);
			}
			else if (pkg.Path.StartsWith("system-images;android-", StringComparison.Ordinal))
			{
				var parts = pkg.Path.Split(';');
				if (parts.Length >= 4)
				{
					var api = parts[1]["android-".Length..];
					if (api.Length > 0)
					{
						if (!systemImagesByApi.TryGetValue(api, out var images))
						{
							images = new List<string>();
							systemImagesByApi[api] = images;
						}
						images.Add($"{parts[2]}/{parts[3]}");
					}
				}
			}
		}

		var allApis = new HashSet<string>(platformApis, StringComparer.OrdinalIgnoreCase);
		foreach (var api in systemImagesByApi.Keys) allApis.Add(api);

		var apiLevels = allApis
			.OrderByDescending(api => ParseVersion(api))
			.ThenBy(api => api.Contains('-', StringComparison.Ordinal))
			.ThenByDescending(api => api, StringComparer.OrdinalIgnoreCase)
			.Select(api => new ApiLevelInfo
			{
				Api = api,
				Platform = platformApis.Contains(api),
				SystemImages = systemImagesByApi.GetValueOrDefault(api, [])
					.OrderBy(image => image, StringComparer.OrdinalIgnoreCase)
					.ToArray()
			})
			.ToArray();

		var buildToolVersions = buildTools
			.OrderByDescending(version => ParseVersion(version))
			.ThenBy(version => version.Contains('-', StringComparison.Ordinal))
			.ThenByDescending(version => version, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return (apiLevels, buildToolVersions);
	}

	static Version ParseVersion(string value)
	{
		var numericPart = value.Split('-', 2)[0];
		if (Version.TryParse(numericPart, out var version))
			return version;
		return int.TryParse(numericPart, out var major) ? new Version(major, 0) : new Version(0, 0);
	}

	static List<SdkPackage> ReadInstalledPackagesFromDisk(string sdkPath, CancellationToken cancellationToken)
	{
		var packages = new List<SdkPackage>();
		AddDirectoryPackages("platforms", "platforms");
		AddDirectoryPackages("build-tools", "build-tools");

		var systemImagesRoot = Path.Combine(sdkPath, "system-images");
		if (Directory.Exists(systemImagesRoot))
		{
			foreach (var apiDirectory in Directory.EnumerateDirectories(systemImagesRoot))
			{
				foreach (var tagDirectory in Directory.EnumerateDirectories(apiDirectory))
				{
					foreach (var abiDirectory in Directory.EnumerateDirectories(tagDirectory))
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (!IsInstalledPackageDirectory(abiDirectory))
							continue;
						packages.Add(new SdkPackage
						{
							Path = $"system-images;{Path.GetFileName(apiDirectory)};{Path.GetFileName(tagDirectory)};{Path.GetFileName(abiDirectory)}",
							IsInstalled = true
						});
					}
				}
			}
		}

		return packages;

		void AddDirectoryPackages(string directoryName, string packagePrefix)
		{
			var root = Path.Combine(sdkPath, directoryName);
			if (!Directory.Exists(root))
				return;

			foreach (var directory in Directory.EnumerateDirectories(root))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!IsInstalledPackageDirectory(directory))
					continue;
				packages.Add(new SdkPackage
				{
					Path = $"{packagePrefix};{Path.GetFileName(directory)}",
					IsInstalled = true
				});
			}
		}

		static bool IsInstalledPackageDirectory(string directory)
			=> File.Exists(Path.Combine(directory, "package.xml")) ||
				File.Exists(Path.Combine(directory, "source.properties"));
	}

	static void WriteHumanReadableInfo(IOutputFormatter formatter, AndroidEnvironmentInfo info)
	{
		formatter.WriteInfo("Android Development Environment");

		// SDK Path
		var sdkDisplay = info.SdkPath ?? "(not found)";
		var sourceDisplay = info.SdkPathSource != null ? $" (source: {info.SdkPathSource})" : "";
		formatter.WriteProgress($"  SDK Path:        {sdkDisplay}{sourceDisplay}");

		// JDK Path
		var jdkDisplay = info.JdkPath ?? "(not found)";
		var jdkVersionDisplay = info.JdkVersion != null ? $" (JDK {info.JdkVersion})" : "";
		formatter.WriteProgress($"  JDK Path:        {jdkDisplay}{jdkVersionDisplay}");

		// License Status
		var licenseDisplay = info.LicensesAccepted switch
		{
			true => "\u2713 All accepted",
			false => "\u2717 Not accepted",
			null => "Unknown"
		};
		formatter.WriteProgress($"  License Status:  {licenseDisplay}");

		if (info.RequiresElevation)
			formatter.WriteWarning("  SDK location requires administrator access.");

		if (info.InstalledApiLevels is null)
		{
			formatter.WriteProgress("");
			formatter.WriteProgress("  Installed API Levels: unknown");
		}
		else if (info.InstalledApiLevels.Count > 0)
		{
			formatter.WriteProgress("");
			formatter.WriteProgress("  Installed API Levels:");
			foreach (var api in info.InstalledApiLevels)
			{
				var parts = new List<string>();
				if (api.Platform)
					parts.Add("platform");
				if (api.SystemImages.Count > 0)
					parts.Add($"system-image ({string.Join(", ", api.SystemImages)})");

				var detail = parts.Count > 0 ? $" \u2014 {string.Join(" + ", parts)}" : "";
				formatter.WriteProgress($"    API {api.Api}{detail}");
			}
		}

		if (info.BuildTools is null)
		{
			formatter.WriteProgress("");
			formatter.WriteProgress("  Build Tools:      unknown");
		}
		else if (info.BuildTools.Count > 0)
		{
			formatter.WriteProgress("");
			formatter.WriteProgress($"  Build Tools:      {string.Join(", ", info.BuildTools)}");
		}

		// Tools
		if (info.Tools != null && (info.Tools.Sdkmanager != null || info.Tools.Avdmanager != null ||
			info.Tools.Adb != null || info.Tools.Emulator != null))
		{
			formatter.WriteProgress("");
			formatter.WriteProgress("  Tools:");
			if (info.Tools.Sdkmanager != null)
				formatter.WriteProgress($"    sdkmanager:  {info.Tools.Sdkmanager}");
			if (info.Tools.Avdmanager != null)
				formatter.WriteProgress($"    avdmanager:  {info.Tools.Avdmanager}");
			if (info.Tools.Adb != null)
				formatter.WriteProgress($"    adb:         {info.Tools.Adb}");
			if (info.Tools.Emulator != null)
				formatter.WriteProgress($"    emulator:    {info.Tools.Emulator}");
		}

		foreach (var warning in info.Warnings)
			formatter.WriteWarning($"  Warning: {warning}");
	}
}
