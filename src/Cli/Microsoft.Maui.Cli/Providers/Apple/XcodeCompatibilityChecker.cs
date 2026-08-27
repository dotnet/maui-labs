// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Xamarin.MacDev;

namespace Microsoft.Maui.Cli.Providers.Apple;

/// <summary>
/// Detects and fixes Xcode version mismatches with installed iOS/macOS SDK packs.
/// Reads MSBuild metadata to determine required Xcode versions and compares with installed versions.
/// </summary>
public class XcodeCompatibilityChecker
{
	readonly XcodeManager? _xcodeManager;

	public XcodeCompatibilityChecker(XcodeManager? xcodeManager = null)
	{
		_xcodeManager = xcodeManager;
	}

	/// <summary>
	/// Analyzes installed Apple SDK packs and checks Xcode compatibility.
	/// Returns a HealthCheck with any detected mismatches and auto-fix recommendations.
	/// </summary>
	public HealthCheck CheckXcodeCompatibility()
	{
		if (_xcodeManager is null)
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "Xcode Compatibility",
				Status = CheckStatus.Skipped,
				Message = "Xcode compatibility check not available on this platform"
			};
		}

		var sdkRequirements = DetectSdkRequirements();
		if (sdkRequirements.Count == 0)
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "Xcode Compatibility",
				Status = CheckStatus.Skipped,
				Message = "No Apple SDK packs detected"
			};
		}

		var selectedXcode = _xcodeManager.GetSelected();
		var selectedVersion = ExtractMajorMinor(selectedXcode?.Version.ToString());

		var incompatibleSdks = sdkRequirements
			.Where(r => r.RequiredVersion != selectedVersion)
			.ToList();

		if (incompatibleSdks.Count == 0)
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "Xcode Compatibility",
				Status = CheckStatus.Ok,
				Message = $"All SDK packs compatible with Xcode {selectedVersion}",
				Details = new JsonObject
				{
					["selected_xcode_version"] = selectedVersion ?? "unknown",
					["sdk_count"] = sdkRequirements.Count,
					["compatible"] = true
				}
			};
		}

		var incompatibleSdksList = string.Join(", ", incompatibleSdks.Select(s => $"{s.Platform} {s.Version} (requires {s.RequiredVersion})"));

		// Check if any installed Xcode matches the required version
		var availableXcodes = _xcodeManager.List();
		var recommendedVersion = incompatibleSdks.First().RequiredVersion;
		var matchingXcode = availableXcodes.FirstOrDefault(x =>
			ExtractMajorMinor(x.Version.ToString()) == recommendedVersion);

		if (matchingXcode != null)
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "Xcode Compatibility",
				Status = CheckStatus.Error,
				Message = $"SDK packs require Xcode {recommendedVersion}, but {selectedVersion} is selected. Incompatible: {incompatibleSdksList}",
				Details = new JsonObject
				{
					["selected_xcode_version"] = selectedVersion ?? "unknown",
					["required_xcode_version"] = recommendedVersion ?? "unknown",
					["incompatible_sdks"] = new JsonArray(incompatibleSdks.Select(s =>
						new JsonObject
						{
							["platform"] = s.Platform,
							["version"] = s.Version,
							["required_xcode"] = s.RequiredVersion
						}).Cast<JsonNode>().ToArray()),
					["compatible"] = false
				},
				Fix = new FixInfo
				{
					IssueId = ErrorCodes.AppleXcodeVersionMismatch,
					Description = $"Switch to Xcode {recommendedVersion}",
					AutoFixable = true,
					Command = $"xcode-select -s {matchingXcode.Path}"
				}
			};
		}

		return new HealthCheck
		{
			Category = "apple",
			Name = "Xcode Compatibility",
			Status = CheckStatus.Error,
			Message = $"SDK packs require Xcode {recommendedVersion}, but {selectedVersion} is selected. Incompatible: {incompatibleSdksList}",
			Details = new JsonObject
			{
				["selected_xcode_version"] = selectedVersion ?? "unknown",
				["required_xcode_version"] = recommendedVersion ?? "unknown",
				["incompatible_sdks"] = new JsonArray(incompatibleSdks.Select(s =>
					new JsonObject
					{
						["platform"] = s.Platform,
						["version"] = s.Version,
						["required_xcode"] = s.RequiredVersion
					}).Cast<JsonNode>().ToArray()),
				["compatible"] = false
			},
			Fix = new FixInfo
			{
				IssueId = ErrorCodes.AppleXcodeVersionMismatch,
				Description = $"Install or select Xcode {recommendedVersion}",
				AutoFixable = false,
				ManualSteps = new[]
				{
					$"Install Xcode {recommendedVersion} from the Mac App Store",
					$"Or select an installed version: xcode-select -s /Applications/Xcode-{recommendedVersion}.app"
				}
			}
		};
	}

	/// <summary>
	/// Detects all installed Apple SDK packs and their required Xcode versions.
	/// Scans /usr/local/share/dotnet/packs/ for Microsoft.*.Sdk.net* patterns.
	/// </summary>
	List<SdkRequirement> DetectSdkRequirements()
	{
		var requirements = new List<SdkRequirement>();
		var packsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".dotnet", "packs");

		if (!Directory.Exists(packsDir))
		{
			packsDir = "/usr/local/share/dotnet/packs";
		}

		if (!Directory.Exists(packsDir))
		{
			return requirements;
		}

		try
		{
			var sdkDirs = Directory.GetDirectories(packsDir)
				.Where(d => Path.GetFileName(d).Contains(".Sdk.net"))
				.ToList();

			foreach (var sdkDir in sdkDirs)
			{
				var sdkName = Path.GetFileName(sdkDir);

				// Extract platform name (e.g., "iOS" from "Microsoft.iOS.Sdk.net10.0_17.6")
				var platform = ExtractPlatformFromSdkName(sdkName);
				if (string.IsNullOrEmpty(platform))
					continue;

				// Find the versions directory (each SDK version is in a subdirectory)
				var versionDirs = Directory.GetDirectories(sdkDir);
				foreach (var versionDir in versionDirs)
				{
					var versionDirName = Path.GetFileName(versionDir);
					var requiredXcodeVersion = ExtractXcodeRequirementFromSdk(versionDir);

					if (requiredXcodeVersion != null)
					{
						requirements.Add(new SdkRequirement
						{
							Platform = platform,
							Version = versionDirName,
							RequiredVersion = requiredXcodeVersion
						});
					}
				}
			}
		}
		catch
		{
			// If SDK detection fails, return empty list (non-fatal)
		}

		return requirements;
	}

	/// <summary>
	/// Extracts the required Xcode version from an SDK's MSBuild properties.
	/// Looks for Microsoft.*.Sdk.Versions.props files with _RecommendedXcodeVersion property.
	/// </summary>
	string? ExtractXcodeRequirementFromSdk(string versionDir)
	{
		try
		{
			var targetsDir = Path.Combine(versionDir, "targets");
			if (!Directory.Exists(targetsDir))
				return null;

			var propsFile = Directory.GetFiles(targetsDir, "*.Versions.props")
				.FirstOrDefault();

			if (propsFile == null)
				return null;

			var doc = XDocument.Load(propsFile);
			var xcodeVersionElement = doc.Descendants()
				.FirstOrDefault(e => e.Name.LocalName == "_RecommendedXcodeVersion");

			if (xcodeVersionElement?.Value != null)
			{
				return ExtractMajorMinor(xcodeVersionElement.Value);
			}
		}
		catch
		{
			// If parsing fails, return null (non-fatal)
		}

		return null;
	}

	/// <summary>
	/// Extracts platform name from SDK directory name.
	/// Example: "Microsoft.iOS.Sdk.net10.0_17.6" → "iOS"
	/// </summary>
	static string? ExtractPlatformFromSdkName(string sdkName)
	{
		// Pattern: Microsoft.{Platform}.Sdk.net{TFM}_{Version}
		var parts = sdkName.Split('.');
		if (parts.Length >= 3 && parts[0] == "Microsoft" && parts[2] == "Sdk")
		{
			return parts[1]; // iOS, macOS, tvOS, watchOS
		}

		return null;
	}

	/// <summary>
	/// Extracts major.minor version from a version string.
	/// Example: "26.5.1" → "26.5"
	/// </summary>
	static string? ExtractMajorMinor(string? version)
	{
		if (string.IsNullOrWhiteSpace(version))
			return null;

		var parts = version.Split('.');
		if (parts.Length >= 2)
		{
			return $"{parts[0]}.{parts[1]}";
		}

		return version;
	}

	record SdkRequirement
	{
		public required string Platform { get; init; }
		public required string Version { get; init; }
		public required string RequiredVersion { get; init; }
	}
}
