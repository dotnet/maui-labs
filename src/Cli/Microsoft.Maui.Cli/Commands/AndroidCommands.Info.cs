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
			var androidProvider = Program.AndroidProvider;
			var jdkManager = Program.JdkManager;

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var formatter = Program.GetFormatter(parseResult);

			try
			{
				var sdkPath = androidProvider.SdkPath;
				var sdkPathSource = androidProvider.GetSdkPathSource();
				var jdkPath = androidProvider.JdkPath;
				var jdkVersion = jdkManager.DetectedJdkVersion;
				var requiresElevation = androidProvider.SdkPathRequiresElevation;

				// Gather license status
				bool? licensesAccepted = null;
				if (androidProvider.IsSdkInstalled)
				{
					try
					{
						licensesAccepted = await androidProvider.AreLicensesAcceptedAsync(cancellationToken);
					}
					catch
					{
						// License check may fail if sdkmanager is not available
					}
				}

				// Gather installed packages and group by API level
				var installedApiLevels = new List<ApiLevelInfo>();
				if (androidProvider.IsSdkInstalled)
				{
					try
					{
						var packages = await androidProvider.GetInstalledPackagesAsync(cancellationToken);
						installedApiLevels = GroupPackagesByApiLevel(packages);
					}
					catch
					{
						// Package listing may fail if sdkmanager is not available
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

	static List<ApiLevelInfo> GroupPackagesByApiLevel(List<SdkPackage> packages)
	{
		// Collect raw data per API level using mutable containers
		var platformApis = new HashSet<int>();
		var buildToolsByApi = new Dictionary<int, string>();
		var systemImagesByApi = new Dictionary<int, List<string>>();

		foreach (var pkg in packages)
		{
			// Match platforms;android-XX
			if (pkg.Path.StartsWith("platforms;android-", StringComparison.Ordinal))
			{
				var apiStr = pkg.Path["platforms;android-".Length..];
				if (int.TryParse(apiStr, out var api))
					platformApis.Add(api);
			}
			// Match build-tools;XX.Y.Z
			else if (pkg.Path.StartsWith("build-tools;", StringComparison.Ordinal))
			{
				var versionStr = pkg.Path["build-tools;".Length..];
				// Extract major version as API level approximation
				var dotIndex = versionStr.IndexOf('.');
				var majorStr = dotIndex > 0 ? versionStr[..dotIndex] : versionStr;
				if (int.TryParse(majorStr, out var api))
					buildToolsByApi[api] = versionStr;
			}
			// Match system-images;android-XX;variant;arch
			else if (pkg.Path.StartsWith("system-images;android-", StringComparison.Ordinal))
			{
				var parts = pkg.Path.Split(';');
				if (parts.Length >= 4)
				{
					var apiStr = parts[1]["android-".Length..];
					if (int.TryParse(apiStr, out var api))
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

		// Merge all discovered API levels
		var allApis = new HashSet<int>(platformApis);
		foreach (var api in buildToolsByApi.Keys) allApis.Add(api);
		foreach (var api in systemImagesByApi.Keys) allApis.Add(api);

		return allApis
			.OrderByDescending(api => api)
			.Select(api => new ApiLevelInfo
			{
				Api = api,
				Platform = platformApis.Contains(api),
				BuildTools = buildToolsByApi.GetValueOrDefault(api),
				SystemImages = systemImagesByApi.GetValueOrDefault(api, new List<string>())
			})
			.ToList();
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
		var jdkVersionDisplay = info.JdkVersion != null ? $" (OpenJDK {info.JdkVersion})" : "";
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

		// Installed API Levels
		if (info.InstalledApiLevels.Count > 0)
		{
			formatter.WriteProgress("");
			formatter.WriteProgress("  Installed API Levels:");
			foreach (var api in info.InstalledApiLevels)
			{
				var parts = new List<string>();
				if (api.Platform)
					parts.Add("platform");
				if (!string.IsNullOrEmpty(api.BuildTools))
					parts.Add($"build-tools {api.BuildTools}");
				if (api.SystemImages.Count > 0)
					parts.Add($"system-image ({string.Join(", ", api.SystemImages)})");

				var detail = parts.Count > 0 ? $" \u2014 {string.Join(" + ", parts)}" : "";
				formatter.WriteProgress($"    Android {api.Api} (API {api.Api}){detail}");
			}
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
	}
}
