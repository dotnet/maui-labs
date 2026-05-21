// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Services;

public sealed record MauiProjectPackageVersion
{
	public required string PackageId { get; init; }
	public string? Version { get; init; }
	public string? ResolvedVersion { get; init; }
	public required string Source { get; init; }
	public required string FilePath { get; init; }
}

public sealed record MauiProjectVersionInfo
{
	public required string ProjectPath { get; init; }
	public bool UsesMaui { get; init; }
	public bool IsMauiProject { get; init; }
	public string? MauiVersion { get; init; }
	public string? WorkloadVersion { get; init; }
	public string? EffectiveVersion { get; init; }
	public bool HasMixedPackageVersions { get; init; }
	public string? CentralPackageFilePath { get; init; }
	public List<MauiProjectPackageVersion> Packages { get; init; } = [];
}

public sealed record MauiProjectVersionChange
{
	public required string FilePath { get; init; }
	public required string Description { get; init; }
	public string? OldValue { get; init; }
	public string? NewValue { get; init; }
}

public sealed record MauiProjectVersionUpdateResult
{
	public required string ProjectPath { get; init; }
	public required string Version { get; init; }
	public bool DryRun { get; init; }
	public bool Changed => Changes.Count > 0;
	public List<MauiProjectVersionChange> Changes { get; init; } = [];
}

public sealed record MauiProjectRestoreResult
{
	public bool Success { get; init; }
	public string? Output { get; init; }
	public string? Error { get; init; }
}

public interface IMauiProjectVersionService
{
	string? DiscoverProjectFile(string? path = null);
	Task<MauiProjectVersionInfo> GetVersionInfoAsync(string projectPath, CancellationToken cancellationToken = default);
	Task<MauiProjectVersionUpdateResult> SetVersionAsync(string projectPath, string version, bool dryRun, CancellationToken cancellationToken = default);
	Task<MauiProjectVersionUpdateResult> UseWorkloadVersionAsync(string projectPath, bool dryRun, CancellationToken cancellationToken = default);
	Task<MauiProjectVersionChange?> EnsureNuGetSourceAsync(string projectPath, string sourceName, string sourceUrl, bool dryRun, CancellationToken cancellationToken = default);
	Task<MauiProjectRestoreResult> RestoreAsync(string projectPath, CancellationToken cancellationToken = default);
}

public sealed class MauiProjectVersionService : IMauiProjectVersionService
{
	internal const string MauiVersionProperty = "MauiVersion";
	internal const string WorkloadVersionExpression = "$(MauiVersion)";

	static readonly HashSet<string> s_mauiPackageIds = new(StringComparer.OrdinalIgnoreCase)
	{
		"Microsoft.Maui.Controls",
		"Microsoft.Maui.Controls.Compatibility",
		"Microsoft.Maui.Controls.Maps",
		"Microsoft.Maui.Core",
		"Microsoft.Maui.Essentials",
		"Microsoft.Maui.Graphics",
		"Microsoft.AspNetCore.Components.WebView.Maui",
	};

	readonly Func<CancellationToken, Task<string?>> _workloadVersionResolver;

	public MauiProjectVersionService(Func<CancellationToken, Task<string?>>? workloadVersionResolver = null)
	{
		_workloadVersionResolver = workloadVersionResolver ?? ResolveMauiVersionFromWorkloadAsync;
	}

	public string? DiscoverProjectFile(string? path = null)
	{
		path = string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : path;

		if (File.Exists(path))
			return Path.GetFullPath(path);

		if (!Directory.Exists(path))
			return null;

		var directProjects = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
		if (directProjects.Length == 1)
			return Path.GetFullPath(directProjects[0]);

		var recursiveProjects = EnumerateProjectFiles(path).Take(2).ToList();
		return recursiveProjects.Count == 1 ? Path.GetFullPath(recursiveProjects[0]) : null;
	}

	public async Task<MauiProjectVersionInfo> GetVersionInfoAsync(string projectPath, CancellationToken cancellationToken = default)
	{
		projectPath = Path.GetFullPath(projectPath);
		var document = LoadXml(projectPath);
		var usesMaui = HasTrueProperty(document, "UseMaui");
		var centralPackageFile = FindCentralPackageFile(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory);
		var properties = LoadKnownProperties(projectPath, centralPackageFile);
		var mauiVersion = ResolvePropertyExpression(GetPropertyValue(document, MauiVersionProperty), properties);

		var packages = new List<MauiProjectPackageVersion>();
		var projectPackages = GetProjectPackageVersions(document, projectPath, properties).ToList();
		packages.AddRange(projectPackages);
		var projectPackageIds = projectPackages
			.Select(package => package.PackageId)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var hasProjectMauiMarker = usesMaui || mauiVersion is not null || projectPackages.Count > 0;
		var centralPackageManagementEnabled = IsCentralPackageManagementEnabled(properties);

		if (centralPackageFile is not null && hasProjectMauiMarker && centralPackageManagementEnabled)
		{
			var centralDocument = LoadXml(centralPackageFile);
			packages.AddRange(GetCentralPackageVersions(centralDocument, centralPackageFile, properties)
				.Where(package => projectPackageIds.Contains(package.PackageId)));
		}

		var isMauiProject = hasProjectMauiMarker;
		var rawVersions = packages
			.Select(package => package.ResolvedVersion ?? package.Version)
			.Where(version => !string.IsNullOrWhiteSpace(version))
			.Select(version => version!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		var needsWorkloadVersion = isMauiProject && (rawVersions.Count == 0 || rawVersions.Contains(WorkloadVersionExpression, StringComparer.OrdinalIgnoreCase));
		var workloadVersion = needsWorkloadVersion ? await _workloadVersionResolver(cancellationToken) : null;
		var effectiveVersion = GetEffectiveVersion(mauiVersion, workloadVersion, rawVersions);

		return new MauiProjectVersionInfo
		{
			ProjectPath = projectPath,
			UsesMaui = usesMaui,
			IsMauiProject = isMauiProject,
			MauiVersion = mauiVersion,
			WorkloadVersion = workloadVersion,
			EffectiveVersion = effectiveVersion,
			HasMixedPackageVersions = rawVersions.Count > 1,
			CentralPackageFilePath = centralPackageFile,
			Packages = packages,
		};
	}

	public Task<MauiProjectVersionUpdateResult> SetVersionAsync(
		string projectPath,
		string version,
		bool dryRun,
		CancellationToken cancellationToken = default)
	{
		ValidateVersionValue(version, allowWorkloadExpression: false);
		return UpdateVersionAsync(projectPath, version, removeMauiVersionProperty: false, dryRun, cancellationToken);
	}

	public Task<MauiProjectVersionUpdateResult> UseWorkloadVersionAsync(
		string projectPath,
		bool dryRun,
		CancellationToken cancellationToken = default)
	{
		return UseWorkloadVersionCoreAsync(projectPath, dryRun, cancellationToken);
	}

	async Task<MauiProjectVersionUpdateResult> UseWorkloadVersionCoreAsync(
		string projectPath,
		bool dryRun,
		CancellationToken cancellationToken)
	{
		var info = await GetVersionInfoAsync(projectPath, cancellationToken);
		if (!info.UsesMaui)
			throw new InvalidOperationException("use-workload requires <UseMaui>true</UseMaui> so $(MauiVersion) is supplied by the installed MAUI workload.");

		return await UpdateVersionAsync(projectPath, WorkloadVersionExpression, removeMauiVersionProperty: true, dryRun, cancellationToken);
	}

	public async Task<MauiProjectVersionChange?> EnsureNuGetSourceAsync(
		string projectPath,
		string sourceName,
		string sourceUrl,
		bool dryRun,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(sourceName))
			throw new ArgumentException("NuGet source name cannot be empty.", nameof(sourceName));
		if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out _))
			throw new ArgumentException($"NuGet source must be an absolute URL: {sourceUrl}", nameof(sourceUrl));

		projectPath = Path.GetFullPath(projectPath);
		var projectDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory;
		var configPath = Directory.GetFiles(projectDirectory, "NuGet.config", SearchOption.TopDirectoryOnly).FirstOrDefault()
			?? Directory.GetFiles(projectDirectory, "nuget.config", SearchOption.TopDirectoryOnly).FirstOrDefault()
			?? Path.Combine(projectDirectory, "NuGet.config");

		XDocument document;
		if (File.Exists(configPath))
		{
			document = LoadXml(configPath);
		}
		else
		{
			document = new XDocument(
				new XElement("configuration",
					new XElement("packageSources")));
		}

		var packageSources = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "packageSources");
		if (packageSources is null)
		{
			packageSources = new XElement("packageSources");
			document.Root?.Add(packageSources);
		}

		var source = packageSources.Elements()
			.FirstOrDefault(e =>
				e.Name.LocalName == "add" &&
				string.Equals(e.Attribute("key")?.Value, sourceName, StringComparison.OrdinalIgnoreCase));

		if (source is null)
		{
			var change = new MauiProjectVersionChange
			{
				FilePath = configPath,
				Description = $"Add NuGet source '{sourceName}'",
				NewValue = sourceUrl,
			};

			if (!dryRun)
			{
				packageSources.Add(new XElement("add",
					new XAttribute("key", sourceName),
					new XAttribute("value", sourceUrl)));
				await SaveXmlAsync(document, configPath, cancellationToken);
			}

			return change;
		}

		var valueAttribute = source.Attribute("value");
		var oldValue = valueAttribute?.Value;
		if (string.Equals(oldValue, sourceUrl, StringComparison.OrdinalIgnoreCase))
			return null;

		var updateChange = new MauiProjectVersionChange
		{
			FilePath = configPath,
			Description = $"Update NuGet source '{sourceName}'",
			OldValue = oldValue,
			NewValue = sourceUrl,
		};

		if (!dryRun)
		{
			if (valueAttribute is null)
				source.Add(new XAttribute("value", sourceUrl));
			else
				valueAttribute.Value = sourceUrl;
			await SaveXmlAsync(document, configPath, cancellationToken);
		}

		return updateChange;
	}

	public async Task<MauiProjectRestoreResult> RestoreAsync(string projectPath, CancellationToken cancellationToken = default)
	{
		projectPath = Path.GetFullPath(projectPath);
		var result = await ProcessRunner.RunAsync(
			"dotnet",
			["restore", projectPath],
			workingDirectory: Path.GetDirectoryName(projectPath),
			cancellationToken: cancellationToken);

		if (!result.Success)
		{
			throw new InvalidOperationException(
				$"'dotnet restore {projectPath}' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
		}

		return new MauiProjectRestoreResult
		{
			Success = true,
			Output = result.StandardOutput.Trim(),
			Error = string.IsNullOrWhiteSpace(result.StandardError) ? null : result.StandardError.Trim(),
		};
	}

	async Task<MauiProjectVersionUpdateResult> UpdateVersionAsync(
		string projectPath,
		string version,
		bool removeMauiVersionProperty,
		bool dryRun,
		CancellationToken cancellationToken)
	{
		ValidateVersionValue(version, allowWorkloadExpression: true);
		projectPath = Path.GetFullPath(projectPath);

		if (!File.Exists(projectPath))
			throw new FileNotFoundException($"Project file not found: {projectPath}", projectPath);

		var info = await GetVersionInfoAsync(projectPath, cancellationToken);
		if (!info.IsMauiProject)
			throw new InvalidOperationException($"No .NET MAUI project markers found in {Path.GetFileName(projectPath)}.");

		var changes = new List<MauiProjectVersionChange>();
		var projectDocument = LoadXml(projectPath);
		var projectChanged = false;
		var projectReferencePackageIds = GetProjectPackageReferences(projectDocument)
			.Where(element => IsMauiPackage(GetPackageId(element)))
			.Select(element => GetPackageId(element)!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var projectPackageReferencesWithoutVersion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var packageReference in GetProjectPackageReferences(projectDocument).Where(element => IsMauiPackage(GetPackageId(element))))
		{
			var packageId = GetPackageId(packageReference)!;
			var packageVersion = GetVersionValue(packageReference);
			if (TryUpdateVersionOnElement(packageReference, projectPath, packageId, version, changes, addIfMissing: false))
			{
				projectChanged = true;
			}
			else if (packageVersion is null)
			{
				projectPackageReferencesWithoutVersion.Add(packageId);
			}
		}

		var centralChanged = false;
		XDocument? centralDocument = null;
		var centralFilePath = info.CentralPackageFilePath;
		var centralPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var centralPackageManagementEnabled = false;
		if (centralFilePath is not null)
		{
			centralDocument = LoadXml(centralFilePath);
			centralPackageManagementEnabled = IsCentralPackageManagementEnabled(projectPath, centralFilePath);
			if (centralPackageManagementEnabled)
			{
				foreach (var packageVersion in GetCentralPackageVersionElements(centralDocument).Where(element => IsMauiPackage(GetPackageId(element))))
				{
					var packageId = GetPackageId(packageVersion)!;
					centralPackageIds.Add(packageId);
					if (projectPackageReferencesWithoutVersion.Contains(packageId) &&
						TryUpdateVersionOnElement(packageVersion, centralFilePath, packageId, version, changes, addIfMissing: true))
					{
						centralChanged = true;
					}
				}
			}
		}

		foreach (var packageId in projectPackageReferencesWithoutVersion.Where(packageId => !centralPackageIds.Contains(packageId)))
		{
			if (centralPackageManagementEnabled && centralDocument is not null && centralFilePath is not null)
			{
				if (AddCentralPackageVersion(centralDocument, centralFilePath, packageId, version, changes))
					centralChanged = true;
			}
			else
			{
				var packageReference = GetProjectPackageReferences(projectDocument)
					.First(element => string.Equals(GetPackageId(element), packageId, StringComparison.OrdinalIgnoreCase));
				if (TryUpdateVersionOnElement(packageReference, projectPath, packageId, version, changes, addIfMissing: true))
				{
					projectChanged = true;
				}
			}
		}

		if (removeMauiVersionProperty)
		{
			if (RemoveMauiVersionProperty(projectDocument, projectPath, changes))
				projectChanged = true;
		}
		else if (info.UsesMaui || info.MauiVersion is not null || projectReferencePackageIds.Count == 0)
		{
			if (SetMauiVersionProperty(projectDocument, projectPath, version, changes))
				projectChanged = true;
		}

		if (!dryRun)
		{
			if (projectChanged)
				await SaveXmlAsync(projectDocument, projectPath, cancellationToken);
			if (centralChanged && centralDocument is not null && centralFilePath is not null)
				await SaveXmlAsync(centralDocument, centralFilePath, cancellationToken);
		}

		return new MauiProjectVersionUpdateResult
		{
			ProjectPath = projectPath,
			Version = version,
			DryRun = dryRun,
			Changes = changes,
		};
	}

	static IEnumerable<string> EnumerateProjectFiles(string directory)
	{
		var pending = new Queue<string>();
		pending.Enqueue(directory);

		while (pending.Count > 0)
		{
			var current = pending.Dequeue();
			foreach (var project in Directory.GetFiles(current, "*.csproj", SearchOption.TopDirectoryOnly))
				yield return project;

			foreach (var child in Directory.GetDirectories(current))
			{
				var name = Path.GetFileName(child);
				if (name is "bin" or "obj" or ".git" or ".vs")
					continue;
				pending.Enqueue(child);
			}
		}
	}

	static string? GetEffectiveVersion(string? mauiVersion, string? workloadVersion, List<string> rawPackageVersions)
	{
		var expandedVersions = rawPackageVersions
			.Select(version => string.Equals(version, WorkloadVersionExpression, StringComparison.OrdinalIgnoreCase)
				? mauiVersion ?? workloadVersion ?? version
				: version)
			.Where(version => !string.IsNullOrWhiteSpace(version))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (expandedVersions.Count == 1)
			return expandedVersions[0];
		if (mauiVersion is not null)
			return mauiVersion;
		return workloadVersion;
	}

	static bool HasTrueProperty(XDocument document, string propertyName) =>
		document.Descendants()
			.Any(element =>
				element.Name.LocalName == propertyName &&
				string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

	static string? GetPropertyValue(XDocument document, string propertyName) =>
		document.Descendants()
			.FirstOrDefault(element =>
				element.Name.LocalName == propertyName &&
				IsUnconditionalProperty(element))
			?.Value
			.Trim();

	static IEnumerable<MauiProjectPackageVersion> GetProjectPackageVersions(
		XDocument document,
		string filePath,
		IReadOnlyDictionary<string, string> properties) =>
		GetProjectPackageReferences(document)
			.Where(element => IsMauiPackage(GetPackageId(element)))
			.Select(element => new MauiProjectPackageVersion
			{
				PackageId = GetPackageId(element)!,
				Version = GetVersionValue(element),
				ResolvedVersion = ResolvePropertyExpression(GetVersionValue(element), properties),
				Source = "project",
				FilePath = filePath,
			});

	static IEnumerable<MauiProjectPackageVersion> GetCentralPackageVersions(
		XDocument document,
		string filePath,
		IReadOnlyDictionary<string, string> properties) =>
		GetCentralPackageVersionElements(document)
			.Where(element => IsMauiPackage(GetPackageId(element)))
			.Select(element => new MauiProjectPackageVersion
			{
				PackageId = GetPackageId(element)!,
				Version = GetVersionValue(element),
				ResolvedVersion = ResolvePropertyExpression(GetVersionValue(element), properties),
				Source = "central",
				FilePath = filePath,
			});

	static IEnumerable<XElement> GetProjectPackageReferences(XDocument document) =>
		document.Descendants().Where(element => element.Name.LocalName == "PackageReference");

	static IEnumerable<XElement> GetCentralPackageVersionElements(XDocument document) =>
		document.Descendants().Where(element => element.Name.LocalName == "PackageVersion");

	static string? GetPackageId(XElement element) =>
		element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;

	static string? GetVersionValue(XElement element) =>
		element.Attribute("Version")?.Value ??
		element.Attribute("VersionOverride")?.Value ??
		element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value.Trim();

	static bool TryUpdateVersionOnElement(
		XElement element,
		string filePath,
		string packageId,
		string version,
		List<MauiProjectVersionChange> changes,
		bool addIfMissing)
	{
		var versionAttribute = element.Attribute("Version");
		var versionOverrideAttribute = element.Attribute("VersionOverride");
		var versionElement = element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version");
		var oldValue = versionAttribute?.Value ?? versionOverrideAttribute?.Value ?? versionElement?.Value.Trim();

		if (oldValue is null && !addIfMissing)
			return false;
		if (string.Equals(oldValue, version, StringComparison.OrdinalIgnoreCase))
			return false;

		changes.Add(new MauiProjectVersionChange
		{
			FilePath = filePath,
			Description = oldValue is null
				? $"Set {packageId} version"
				: $"Update {packageId} version",
			OldValue = oldValue,
			NewValue = version,
		});

		if (versionAttribute is not null)
			versionAttribute.Value = version;
		else if (versionOverrideAttribute is not null)
			versionOverrideAttribute.Value = version;
		else if (versionElement is not null)
			versionElement.Value = version;
		else
			element.Add(new XAttribute("Version", version));

		return true;
	}

	static bool SetMauiVersionProperty(
		XDocument document,
		string projectPath,
		string version,
		List<MauiProjectVersionChange> changes)
	{
		var changed = false;
		var properties = document.Descendants()
			.Where(element => element.Name.LocalName == MauiVersionProperty)
			.ToList();
		var hasUnconditionalProperty = properties.Any(IsUnconditionalProperty);

		foreach (var property in properties)
		{
			var oldValue = property.Value.Trim();
			if (string.Equals(oldValue, version, StringComparison.OrdinalIgnoreCase))
				continue;

			changes.Add(new MauiProjectVersionChange
			{
				FilePath = projectPath,
				Description = "Update MauiVersion property",
				OldValue = oldValue,
				NewValue = version,
			});
			property.Value = version;
			changed = true;
		}

		if (hasUnconditionalProperty)
			return changed;

		changes.Add(new MauiProjectVersionChange
		{
			FilePath = projectPath,
			Description = "Set MauiVersion property",
			NewValue = version,
		});

		var root = document.Root ?? throw new InvalidOperationException("Project file does not have a root element.");
		var ns = root.Name.Namespace;
		var propertyGroup = root.Elements().FirstOrDefault(element =>
			element.Name.LocalName == "PropertyGroup" &&
			element.Attribute("Condition") is null);
		if (propertyGroup is null)
		{
			propertyGroup = new XElement(ns + "PropertyGroup");
			root.AddFirst(propertyGroup);
		}

		propertyGroup.Add(new XElement(ns + MauiVersionProperty, version));
		return true;
	}

	static bool AddCentralPackageVersion(
		XDocument document,
		string filePath,
		string packageId,
		string version,
		List<MauiProjectVersionChange> changes)
	{
		changes.Add(new MauiProjectVersionChange
		{
			FilePath = filePath,
			Description = $"Set {packageId} version",
			NewValue = version,
		});

		var root = document.Root ?? throw new InvalidOperationException("Central package file does not have a root element.");
		var ns = root.Name.Namespace;
		var itemGroup = root.Elements().FirstOrDefault(element =>
			element.Name.LocalName == "ItemGroup" &&
			element.Attribute("Condition") is null);
		if (itemGroup is null)
		{
			itemGroup = new XElement(ns + "ItemGroup");
			root.Add(itemGroup);
		}

		itemGroup.Add(new XElement(ns + "PackageVersion",
			new XAttribute("Include", packageId),
			new XAttribute("Version", version)));
		return true;
	}

	static bool RemoveMauiVersionProperty(
		XDocument document,
		string projectPath,
		List<MauiProjectVersionChange> changes)
	{
		var properties = document.Descendants().Where(element => element.Name.LocalName == MauiVersionProperty).ToList();
		if (properties.Count == 0)
			return false;

		foreach (var property in properties)
		{
			changes.Add(new MauiProjectVersionChange
			{
				FilePath = projectPath,
				Description = "Remove MauiVersion property",
				OldValue = property.Value.Trim(),
			});
			property.Remove();
		}

		return true;
	}

	static string? FindCentralPackageFile(string startDirectory)
	{
		var directory = new DirectoryInfo(startDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine(directory.FullName, "Directory.Packages.props");
			if (File.Exists(candidate))
				return candidate;
			directory = directory.Parent;
		}

		return null;
	}

	static bool IsMauiPackage(string? packageId) =>
		packageId is not null && s_mauiPackageIds.Contains(packageId);

	static IReadOnlyDictionary<string, string> LoadKnownProperties(string projectPath, string? centralPackageFile)
	{
		var files = new List<string>();
		var directory = new DirectoryInfo(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory);
		var ancestors = new Stack<DirectoryInfo>();
		for (var current = directory; current is not null; current = current.Parent)
			ancestors.Push(current);

		foreach (var ancestor in ancestors)
		{
			var versionsProps = Path.Combine(ancestor.FullName, "eng", "Versions.props");
			if (File.Exists(versionsProps))
				files.Add(versionsProps);

			var directoryBuildProps = Path.Combine(ancestor.FullName, "Directory.Build.props");
			if (File.Exists(directoryBuildProps))
				files.Add(directoryBuildProps);
		}

		if (centralPackageFile is not null)
			files.Add(centralPackageFile);
		files.Add(projectPath);

		var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			foreach (var property in LoadProperties(file))
				properties[property.Key] = ResolvePropertyExpression(property.Value, properties) ?? property.Value;
		}

		return properties;
	}

	static IEnumerable<KeyValuePair<string, string>> LoadProperties(string filePath)
	{
		var document = LoadXml(filePath);

		foreach (var propertyGroup in document.Descendants().Where(element =>
			element.Name.LocalName == "PropertyGroup" &&
			element.Attribute("Condition") is null))
		{
			foreach (var property in propertyGroup.Elements())
			{
				if (property.HasElements || property.Attribute("Condition") is not null)
					continue;
				yield return new KeyValuePair<string, string>(property.Name.LocalName, property.Value.Trim());
			}
		}
	}

	static bool IsCentralPackageManagementEnabled(string projectPath, string centralPackageFile)
	{
		var properties = LoadKnownProperties(projectPath, centralPackageFile);
		return IsCentralPackageManagementEnabled(properties);
	}

	static bool IsCentralPackageManagementEnabled(IReadOnlyDictionary<string, string> properties)
	{
		return properties.TryGetValue("ManagePackageVersionsCentrally", out var value) &&
			string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
	}

	static bool IsUnconditionalProperty(XElement element) =>
		element.Attribute("Condition") is null &&
		element.Parent?.Name.LocalName == "PropertyGroup" &&
		element.Parent.Attribute("Condition") is null;

	static string? ResolvePropertyExpression(string? value, IReadOnlyDictionary<string, string> properties)
	{
		if (string.IsNullOrWhiteSpace(value))
			return value;

		var resolved = value;
		for (var pass = 0; pass < 10 && resolved.Contains("$(", StringComparison.Ordinal); pass++)
		{
			var before = resolved;
			foreach (var property in properties)
				resolved = resolved.Replace($"$({property.Key})", property.Value, StringComparison.OrdinalIgnoreCase);

			if (string.Equals(before, resolved, StringComparison.Ordinal))
				break;
		}

		return resolved;
	}

	static void ValidateVersionValue(string version, bool allowWorkloadExpression)
	{
		if (string.IsNullOrWhiteSpace(version))
			throw new ArgumentException("Version cannot be empty.", nameof(version));

		if (allowWorkloadExpression && string.Equals(version, WorkloadVersionExpression, StringComparison.OrdinalIgnoreCase))
			return;

		if (!char.IsDigit(version[0]) || version.Any(character =>
			!(char.IsLetterOrDigit(character) || character is '.' or '-' or '+')))
		{
			throw new ArgumentException($"'{version}' is not a valid NuGet-style MAUI version.", nameof(version));
		}
	}

	static XDocument LoadXml(string path) =>
		XDocument.Load(path, LoadOptions.PreserveWhitespace);

	static async Task SaveXmlAsync(XDocument document, string path, CancellationToken cancellationToken)
	{
		var directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
		var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
		var moved = false;

		try
		{
			await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				await document.SaveAsync(stream, SaveOptions.DisableFormatting, cancellationToken);
				await stream.FlushAsync(cancellationToken);
			}

			File.Move(tempPath, path, overwrite: true);
			moved = true;
		}
		finally
		{
			if (!moved && File.Exists(tempPath))
				File.Delete(tempPath);
		}
	}

	static async Task<string?> ResolveMauiVersionFromWorkloadAsync(CancellationToken cancellationToken)
	{
		var result = await ProcessRunner.RunAsync("dotnet", ["workload", "--info"], cancellationToken: cancellationToken);
		if (!result.Success || !result.StandardOutput.Contains("[maui]", StringComparison.OrdinalIgnoreCase))
			return null;

		var mauiSection = result.StandardOutput[result.StandardOutput.IndexOf("[maui]", StringComparison.OrdinalIgnoreCase)..];
		var manifestIndex = mauiSection.IndexOf("Manifest Version:", StringComparison.OrdinalIgnoreCase);
		if (manifestIndex < 0)
			return null;

		var afterLabel = mauiSection[(manifestIndex + "Manifest Version:".Length)..];
		var slashIndex = afterLabel.IndexOf('/');
		var newlineIndex = afterLabel.IndexOf('\n');
		var endIndex = (slashIndex >= 0 && newlineIndex >= 0)
			? Math.Min(slashIndex, newlineIndex)
			: slashIndex >= 0
				? slashIndex
				: newlineIndex >= 0
					? newlineIndex
					: afterLabel.Length;

		return afterLabel[..endIndex].Trim();
	}
}
