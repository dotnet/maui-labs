// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Xamarin.MacDev;

namespace Microsoft.Maui.Cli.Providers.Apple;

/// <summary>
/// Compares the selected Xcode with the Apple SDK packs installed for the current .NET runtime.
/// </summary>
public sealed class XcodeCompatibilityChecker
{
	const string CheckCategory = "apple";
	const string CheckName = "Xcode Compatibility";

	readonly IXcodeCompatibilityEnvironment? _environment;

	public XcodeCompatibilityChecker(XcodeManager? xcodeManager = null)
	{
		_environment = xcodeManager is null
			? null
			: new XcodeCompatibilityEnvironment(xcodeManager);
	}

	internal XcodeCompatibilityChecker(IXcodeCompatibilityEnvironment environment)
	{
		_environment = environment;
	}

	/// <summary>
	/// Checks whether the selected Xcode matches the requirements from installed Apple SDK packs.
	/// </summary>
	public HealthCheck CheckXcodeCompatibility()
	{
		if (_environment is null)
			return CreateCheck(CheckStatus.Skipped, "Xcode compatibility check not available on this platform");

		var sdkRequirements = DetectSdkRequirements();
		if (sdkRequirements.Count == 0)
			return CreateCheck(CheckStatus.Skipped, "No Apple SDK packs detected for the current .NET runtime");

		var selectedXcode = _environment.GetSelectedXcode();
		var selectedVersion = ExtractMajorMinor(selectedXcode?.Version);
		var incompatibleSdks = sdkRequirements
			.Where(requirement => requirement.RequiredVersion != selectedVersion)
			.OrderBy(requirement => requirement.Platform, StringComparer.Ordinal)
			.ToList();

		if (incompatibleSdks.Count == 0)
		{
			return CreateCheck(
				CheckStatus.Ok,
				$"All SDK packs compatible with Xcode {selectedVersion}",
				CreateDetails(selectedVersion, sdkRequirements, compatible: true));
		}

		var requiredVersions = sdkRequirements
			.Select(requirement => requirement.RequiredVersion)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(version => ParseVersion(version))
			.ThenBy(version => version, StringComparer.Ordinal)
			.ToList();

		if (requiredVersions.Count > 1)
			return CreateConflictingRequirementsCheck(selectedVersion, sdkRequirements, requiredVersions);

		var requiredVersion = requiredVersions[0];
		var matchingXcode = _environment.GetInstalledXcodes()
			.Where(xcode => ExtractMajorMinor(xcode.Version) == requiredVersion)
			.OrderBy(xcode => xcode.Path, StringComparer.Ordinal)
			.FirstOrDefault();

		return matchingXcode is null
			? CreateManualFixCheck(selectedVersion, sdkRequirements, incompatibleSdks, requiredVersion)
			: CreateAutoFixCheck(selectedVersion, sdkRequirements, incompatibleSdks, requiredVersion, matchingXcode);
	}

	HealthCheck CreateAutoFixCheck(
		string? selectedVersion,
		IReadOnlyList<SdkRequirement> sdkRequirements,
		IReadOnlyList<SdkRequirement> incompatibleSdks,
		string requiredVersion,
		XcodeInstallation matchingXcode)
	{
		var selectedDisplay = selectedVersion ?? "no Xcode";
		var command = $"sudo xcode-select --switch \"{matchingXcode.Path}\"";

		return CreateCheck(
			CheckStatus.Warning,
			$"SDK packs require Xcode {requiredVersion}, but {selectedDisplay} is selected. Incompatible: {FormatRequirements(incompatibleSdks)}",
			CreateDetails(selectedVersion, sdkRequirements, compatible: false, requiredVersion),
			new FixInfo
			{
				IssueId = ErrorCodes.AppleXcodeVersionMismatch,
				Description = $"Switch to Xcode {requiredVersion} (requires administrator permission)",
				AutoFixable = true,
				Command = command
			});
	}

	HealthCheck CreateManualFixCheck(
		string? selectedVersion,
		IReadOnlyList<SdkRequirement> sdkRequirements,
		IReadOnlyList<SdkRequirement> incompatibleSdks,
		string requiredVersion)
	{
		var selectedDisplay = selectedVersion ?? "no Xcode";

		return CreateCheck(
			CheckStatus.Warning,
			$"SDK packs require Xcode {requiredVersion}, but {selectedDisplay} is selected. Incompatible: {FormatRequirements(incompatibleSdks)}",
			CreateDetails(selectedVersion, sdkRequirements, compatible: false, requiredVersion),
			new FixInfo
			{
				IssueId = ErrorCodes.AppleXcodeVersionMismatch,
				Description = $"Install or select Xcode {requiredVersion}",
				AutoFixable = false,
				ManualSteps =
				[
					$"Install Xcode {requiredVersion} from the Mac App Store or Apple Developer downloads",
					$"Select it with: sudo xcode-select --switch \"/Applications/Xcode-{requiredVersion}.app\""
				]
			});
	}

	HealthCheck CreateConflictingRequirementsCheck(
		string? selectedVersion,
		IReadOnlyList<SdkRequirement> incompatibleSdks,
		IReadOnlyList<string> requiredVersions)
	{
		var details = CreateDetails(selectedVersion, incompatibleSdks, compatible: false);
		details["required_xcode_versions"] = new JsonArray(
			requiredVersions.Select(version => (JsonNode)JsonValue.Create(version)!).ToArray());

		return CreateCheck(
			CheckStatus.Warning,
			$"Installed Apple SDK packs require different Xcode versions ({string.Join(", ", requiredVersions)}). Incompatible: {FormatRequirements(incompatibleSdks)}",
			details,
			new FixInfo
			{
				IssueId = ErrorCodes.AppleXcodeVersionMismatch,
				Description = "Align the installed Apple workloads before selecting Xcode",
				AutoFixable = false,
				ManualSteps =
				[
					"Update or repair the installed Apple workloads so their SDK packs require the same Xcode version",
					"Run 'dotnet workload list' to inspect the active workloads"
				]
			});
	}

	List<SdkRequirement> DetectSdkRequirements()
	{
		var requirements = new List<SdkRequirement>();

		foreach (var packsRoot in _environment!.GetPackRoots()
			.Where(Directory.Exists)
			.Distinct(StringComparer.Ordinal))
		{
			foreach (var sdkDirectory in EnumerateDirectories(packsRoot))
			{
				var sdkName = Path.GetFileName(sdkDirectory);
				if (!TryGetSdkPackInfo(sdkName, out var platform, out var targetFramework))
					continue;

				if (targetFramework is not null &&
					!string.Equals(targetFramework, _environment.TargetFramework, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				foreach (var versionDirectory in EnumerateDirectories(sdkDirectory))
				{
					var requiredXcodeVersion = ExtractXcodeRequirementFromSdk(versionDirectory);
					if (requiredXcodeVersion is null)
						continue;

					requirements.Add(new SdkRequirement(
						platform,
						Path.GetFileName(versionDirectory),
						requiredXcodeVersion));
				}
			}
		}

		return requirements
			.Distinct()
			.GroupBy(requirement => requirement.Platform, StringComparer.OrdinalIgnoreCase)
			.Select(group => group
				.OrderByDescending(requirement => ParseVersion(requirement.Version))
				.ThenByDescending(requirement => requirement.Version, StringComparer.Ordinal)
				.First())
			.OrderBy(requirement => requirement.Platform, StringComparer.Ordinal)
			.ToList();
	}

	static IEnumerable<string> EnumerateDirectories(string path)
	{
		try
		{
			return Directory.EnumerateDirectories(path).ToArray();
		}
		catch (IOException)
		{
			return [];
		}
		catch (UnauthorizedAccessException)
		{
			return [];
		}
	}

	static string? ExtractXcodeRequirementFromSdk(string versionDirectory)
	{
		var targetsDirectory = Path.Combine(versionDirectory, "targets");
		if (!Directory.Exists(targetsDirectory))
			return null;

		try
		{
			var propsFile = Directory.EnumerateFiles(targetsDirectory, "*.Versions.props")
				.OrderBy(path => path, StringComparer.Ordinal)
				.FirstOrDefault();
			if (propsFile is null)
				return null;

			var document = XDocument.Load(propsFile);
			var recommendedVersion = document.Descendants()
				.FirstOrDefault(element => element.Name.LocalName == "_RecommendedXcodeVersion")
				?.Value;

			return ExtractMajorMinor(recommendedVersion);
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
		catch (XmlException)
		{
			return null;
		}
	}

	static bool TryGetSdkPackInfo(string sdkName, out string platform, out string? targetFramework)
	{
		platform = string.Empty;
		targetFramework = null;

		const string prefix = "Microsoft.";
		const string sdkMarker = ".Sdk";
		if (!sdkName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return false;

		var sdkMarkerIndex = sdkName.IndexOf(sdkMarker, prefix.Length, StringComparison.OrdinalIgnoreCase);
		if (sdkMarkerIndex < 0)
			return false;

		var platformName = sdkName[prefix.Length..sdkMarkerIndex];
		platform = platformName.ToLowerInvariant() switch
		{
			"ios" => "iOS",
			"macos" => "macOS",
			"maccatalyst" => "MacCatalyst",
			"tvos" => "tvOS",
			"watchos" => "watchOS",
			_ => string.Empty
		};
		if (platform.Length == 0)
			return false;

		var suffix = sdkName[(sdkMarkerIndex + sdkMarker.Length)..];
		if (!suffix.StartsWith(".net", StringComparison.OrdinalIgnoreCase))
			return suffix.Length == 0;

		var separatorIndex = suffix.IndexOf('_');
		targetFramework = separatorIndex < 0 ? suffix[1..] : suffix[1..separatorIndex];
		return true;
	}

	internal static string? ExtractMajorMinor(string? version)
	{
		if (string.IsNullOrWhiteSpace(version))
			return null;

		var parts = version.Split('.');
		return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
	}

	static Version ParseVersion(string version)
	{
		var stableVersion = version.Split('-', 2)[0];
		return Version.TryParse(stableVersion, out var parsedVersion)
			? parsedVersion
			: new Version();
	}

	static string FormatRequirements(IEnumerable<SdkRequirement> requirements) =>
		string.Join(", ", requirements.Select(requirement =>
			$"{requirement.Platform} {requirement.Version} (requires {requirement.RequiredVersion})"));

	static JsonObject CreateDetails(
		string? selectedVersion,
		IReadOnlyList<SdkRequirement> requirements,
		bool compatible,
		string? requiredVersion = null)
	{
		var details = new JsonObject
		{
			["selected_xcode_version"] = selectedVersion ?? "unknown",
			["sdk_count"] = requirements.Count,
			["compatible"] = compatible,
			["sdk_requirements"] = new JsonArray(requirements.Select(requirement =>
				new JsonObject
				{
					["platform"] = requirement.Platform,
					["version"] = requirement.Version,
					["required_xcode"] = requirement.RequiredVersion
				}).Cast<JsonNode>().ToArray())
		};

		if (requiredVersion is not null)
			details["required_xcode_version"] = requiredVersion;

		return details;
	}

	static HealthCheck CreateCheck(
		CheckStatus status,
		string message,
		JsonObject? details = null,
		FixInfo? fix = null) =>
		new()
		{
			Category = CheckCategory,
			Name = CheckName,
			Status = status,
			Message = message,
			Details = details,
			Fix = fix
		};

	readonly record struct SdkRequirement(string Platform, string Version, string RequiredVersion);
}

internal interface IXcodeCompatibilityEnvironment
{
	string TargetFramework { get; }
	XcodeInstallation? GetSelectedXcode();
	IReadOnlyList<XcodeInstallation> GetInstalledXcodes();
	IReadOnlyList<string> GetPackRoots();
}

sealed class XcodeCompatibilityEnvironment(XcodeManager xcodeManager) : IXcodeCompatibilityEnvironment
{
	public string TargetFramework => $"net{Environment.Version.Major}.0";

	public XcodeInstallation? GetSelectedXcode()
	{
		var selected = xcodeManager.GetSelected();
		return selected is null
			? null
			: new XcodeInstallation
			{
				Path = selected.Path,
				Version = selected.Version.ToString(),
				Build = selected.Build,
				IsSelected = true
			};
	}

	public IReadOnlyList<XcodeInstallation> GetInstalledXcodes() =>
		xcodeManager.List()
			.Select(xcode => new XcodeInstallation
			{
				Path = xcode.Path,
				Version = xcode.Version.ToString(),
				Build = xcode.Build,
				IsSelected = xcode.IsSelected
			})
			.ToList();

	public IReadOnlyList<string> GetPackRoots()
	{
		var roots = new List<string>();
		var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!string.IsNullOrWhiteSpace(homeDirectory))
			roots.Add(Path.Combine(homeDirectory, ".dotnet", "packs"));

		var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		if (!string.IsNullOrWhiteSpace(dotnetRoot))
			roots.Add(Path.Combine(dotnetRoot, "packs"));

		roots.Add("/usr/local/share/dotnet/packs");
		roots.Add("/usr/share/dotnet/packs");
		return roots.Distinct(StringComparer.Ordinal).ToList();
	}
}
