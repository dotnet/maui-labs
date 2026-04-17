// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class MauiProjectResolver
{
	public static ResolvedMauiProject Resolve(string? projectOrDirectory)
	{
		var projectPath = ResolveProjectPath(projectOrDirectory);
		var realProjectPath = ResolvePath(projectPath);
		return new ResolvedMauiProject
		{
			ProjectPath = realProjectPath,
			ProjectDirectory = Path.GetDirectoryName(realProjectPath) ?? Environment.CurrentDirectory,
			ProjectName = Path.GetFileNameWithoutExtension(realProjectPath),
			TargetFrameworks = GetTargetFrameworks(realProjectPath)
		};
	}

	static string ResolvePath(string path)
	{
		if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
			return path;

		try
		{
			path = Path.GetFullPath(path);
			var root = Path.GetPathRoot(path) ?? "/";
			var parts = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
			var current = root;
			foreach (var part in parts)
			{
				current = Path.Combine(current, part);
				var resolved = Directory.Exists(current)
					? Directory.ResolveLinkTarget(current, returnFinalTarget: false)?.FullName
					: File.Exists(current)
						? File.ResolveLinkTarget(current, returnFinalTarget: false)?.FullName
						: null;
				if (resolved != null)
					current = resolved;
			}
			return current;
		}
		catch
		{
			return path;
		}
	}

	static string ResolveProjectPath(string? projectOrDirectory)
	{
		var candidate = string.IsNullOrWhiteSpace(projectOrDirectory)
			? Environment.CurrentDirectory
			: Path.GetFullPath(projectOrDirectory);

		if (File.Exists(candidate))
		{
			if (!string.Equals(Path.GetExtension(candidate), ".csproj", StringComparison.OrdinalIgnoreCase))
			{
				throw new MauiToolException(
					ErrorCodes.InvalidArgument,
					$"'{candidate}' is not a .csproj file.");
			}

			return candidate;
		}

		if (!Directory.Exists(candidate))
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Could not find project path '{candidate}'.");
		}

		var projects = Directory.GetFiles(candidate, "*.csproj", SearchOption.TopDirectoryOnly);
		if (projects.Length == 0)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.InvalidArgument,
				$"No .csproj file was found in '{candidate}'.",
				[
					"Run the command from your app directory.",
					"Or pass --project <path-to-your-app.csproj>."
				]);
		}

		if (projects.Length > 1)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Multiple .csproj files were found in '{candidate}'. Please specify one explicitly with --project.");
		}

		return Path.GetFullPath(projects[0]);
	}

	internal static IReadOnlyList<string> GetTargetFrameworks(string projectPath)
	{
		var frameworks = GetTargetFrameworksFromEvaluatedMsbuild(projectPath);
		if (frameworks.Count > 0)
			return frameworks;

		frameworks = GetTargetFrameworksFromProjectFile(projectPath);
		if (frameworks.Count > 0)
			return frameworks;

		throw new MauiToolException(
			ErrorCodes.PlatformNotSupported,
			$"Could not determine any target frameworks for '{projectPath}'.");
	}

	internal static string? GetAndroidApplicationId(string projectPath, string framework, string configuration)
	{
		var manifestPath = Path.Combine(
			Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory,
			"obj",
			configuration,
			framework,
			"AndroidManifest.xml");

		if (File.Exists(manifestPath))
		{
			try
			{
				var manifest = XDocument.Load(manifestPath);
				var packageName = manifest.Root?.Attribute("package")?.Value;
				if (!string.IsNullOrWhiteSpace(packageName))
					return packageName.Trim();
			}
			catch
			{
				// Fall back to project-file parsing below.
			}
		}

		try
		{
			var document = XDocument.Load(projectPath);
			var applicationId = document
				.Descendants()
				.FirstOrDefault(element => element.Name.LocalName.Equals("ApplicationId", StringComparison.OrdinalIgnoreCase))
				?.Value;

			return string.IsNullOrWhiteSpace(applicationId)
				? null
				: applicationId.Trim();
		}
		catch
		{
			return null;
		}
	}

	internal static bool HasPackageReference(string projectPath, string packageId)
	{
		try
		{
			var document = XDocument.Load(projectPath);
			return document
				.Descendants()
				.Where(element => element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase))
				.Any(element =>
				{
					var include = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;
					return string.Equals(include, packageId, StringComparison.OrdinalIgnoreCase);
				});
		}
		catch
		{
			return false;
		}
	}

	static IReadOnlyList<string> GetTargetFrameworksFromEvaluatedMsbuild(string projectPath)
	{
		var result = ProcessRunner.RunSync(
			"dotnet",
			[
				"msbuild",
				projectPath,
				"-nologo",
				"-getProperty:TargetFramework",
				"-getProperty:TargetFrameworks"
			],
			timeout: TimeSpan.FromSeconds(30));

		if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
			return [];

		try
		{
			using var document = JsonDocument.Parse(result.StandardOutput);
			if (!document.RootElement.TryGetProperty("Properties", out var properties))
				return [];

			var values = new List<string>();
			if (properties.TryGetProperty("TargetFramework", out var targetFramework))
				values.AddRange(SplitTargetFrameworks(targetFramework.GetString()));
			if (properties.TryGetProperty("TargetFrameworks", out var targetFrameworks))
				values.AddRange(SplitTargetFrameworks(targetFrameworks.GetString()));

			return values
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (JsonException)
		{
			return [];
		}
	}

	static IReadOnlyList<string> GetTargetFrameworksFromProjectFile(string projectPath)
	{
		var document = XDocument.Load(projectPath);
		return document
			.Descendants()
			.Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
			.SelectMany(element => SplitTargetFrameworks(element.Value))
			.Where(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("$(", StringComparison.Ordinal))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	static IEnumerable<string> SplitTargetFrameworks(string? rawValue) =>
		(rawValue ?? string.Empty)
			.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
