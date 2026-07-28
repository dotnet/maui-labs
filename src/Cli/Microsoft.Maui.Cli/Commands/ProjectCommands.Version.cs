// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Services;

namespace Microsoft.Maui.Cli.Commands;

public static partial class ProjectCommands
{
	static Command CreateVersionCommand()
	{
		var command = new Command("version", "Show and manage the .NET MAUI version used by a project")
		{
			ProjectOption,
		};

		SetHandledAction(command, ShowVersionAsync);
		command.Add(CreateVersionShowCommand());
		command.Add(CreateVersionListCommand());
		command.Add(CreateVersionSetCommand());
		command.Add(CreateVersionUseWorkloadCommand());

		return command;
	}

	static Command CreateVersionShowCommand()
	{
		var command = new Command("show", "Show the .NET MAUI version used by a project");
		command.Aliases.Add("check");
		SetHandledAction(command, ShowVersionAsync);
		return command;
	}

	static Command CreateVersionListCommand()
	{
		var channelOption = new Option<string>("--channel", "-c")
		{
			Description = "Version feed to query: stable or nightly.",
			DefaultValueFactory = _ => "stable",
		};
		var prereleaseOption = new Option<bool>("--prerelease")
		{
			Description = "Include prerelease versions when querying the stable feed.",
		};
		var takeOption = new Option<int>("--take", "-t")
		{
			Description = "Number of versions to display.",
			DefaultValueFactory = _ => 10,
		};

		var command = new Command("list", "List available .NET MAUI package versions")
		{
			channelOption,
			prereleaseOption,
			takeOption,
		};

		SetHandledAction(command, async (parseResult, cancellationToken) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var channel = ParseChannel(parseResult.GetValue(channelOption));
			var take = parseResult.GetValue(takeOption);
			if (take <= 0)
				throw new InvalidOperationException("--take must be a positive number.");

			var includePrerelease = parseResult.GetValue(prereleaseOption) || channel == MauiVersionChannel.Nightly;
			var feedService = Program.Services.GetRequiredService<IMauiVersionFeedService>();
			var versions = await feedService.GetVersionsAsync(channel, includePrerelease, cancellationToken);
			var displayVersions = versions.TakeLast(take).Reverse().ToList();

			if (useJson)
			{
				formatter.Write(new MauiVersionListResult
				{
					Channel = channel.ToString().ToLowerInvariant(),
					Feed = feedService.GetFeedUrl(channel),
					TotalAvailable = versions.Count,
					Versions = displayVersions,
				});
			}
			else
			{
				formatter.WriteTable(displayVersions,
					("Version", version => version.Version),
					("Prerelease", version => version.IsPrerelease ? "Yes" : "No"));
				formatter.WriteInfo($"Showing {displayVersions.Count} of {versions.Count} versions from the {channel.ToString().ToLowerInvariant()} feed.");
			}

			return 0;
		});

		return command;
	}

	static Command CreateVersionSetCommand()
	{
		var versionArgument = new Argument<string?>("version")
		{
			Description = "Specific .NET MAUI version to use. Omit when using --latest or --latest-nightly.",
			Arity = ArgumentArity.ZeroOrOne,
		};
		var latestOption = new Option<bool>("--latest")
		{
			Description = "Set the project to the latest stable Microsoft.Maui.Controls version.",
		};
		var latestNightlyOption = new Option<bool>("--latest-nightly")
		{
			Description = "Set the project to the latest version from the MAUI nightly feed.",
		};
		var nugetConfigOption = new Option<bool>("--nuget-config")
		{
			Description = "Add or update a NuGet.config source for the selected feed.",
		};
		var sourceOption = new Option<string>("--source")
		{
			Description = "NuGet v3 source URL to add to NuGet.config, useful for PR or custom builds.",
		};
		var sourceNameOption = new Option<string>("--source-name")
		{
			Description = "Name to use when adding --source to NuGet.config.",
			DefaultValueFactory = _ => ".NET MAUI Packages",
		};
		var noRestoreOption = new Option<bool>("--no-restore")
		{
			Description = "Do not run dotnet restore after updating files.",
		};

		var command = new Command("set", "Set the .NET MAUI version used by a project")
		{
			versionArgument,
			latestOption,
			latestNightlyOption,
			nugetConfigOption,
			sourceOption,
			sourceNameOption,
			noRestoreOption,
		};

		SetHandledAction(command, async (parseResult, cancellationToken) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var dryRun = Program.IsDryRun(parseResult);
			var projectService = Program.Services.GetRequiredService<IMauiProjectVersionService>();
			var feedService = Program.Services.GetRequiredService<IMauiVersionFeedService>();
			var projectPath = ResolveProjectPath(parseResult, projectService);
			var explicitVersion = parseResult.GetValue(versionArgument);
			var latest = parseResult.GetValue(latestOption);
			var latestNightly = parseResult.GetValue(latestNightlyOption);
			var noRestore = parseResult.GetValue(noRestoreOption);
			var nugetConfig = parseResult.GetValue(nugetConfigOption);
			var source = parseResult.GetValue(sourceOption);
			var sourceName = parseResult.GetValue(sourceNameOption) ?? ".NET MAUI Packages";
			var sourceSpecified = parseResult.GetResult(sourceOption)?.Tokens.Count > 0;
			var sourceNameSpecified = parseResult.GetResult(sourceNameOption)?.Tokens.Count > 0;

			var sourceCount = (string.IsNullOrWhiteSpace(explicitVersion) ? 0 : 1) + (latest ? 1 : 0) + (latestNightly ? 1 : 0);
			if (sourceCount == 0)
				throw new InvalidOperationException("Specify a version, --latest, or --latest-nightly.");
			if (sourceCount > 1)
				throw new InvalidOperationException("Specify only one of version, --latest, or --latest-nightly.");
			if (!nugetConfig && (sourceSpecified || sourceNameSpecified))
				throw new InvalidOperationException("--source and --source-name require --nuget-config.");

			string version;
			string? sourceForConfig = source;
			string sourceNameForConfig = sourceName;
			if (latest)
			{
				var latestStable = await feedService.GetLatestVersionAsync(MauiVersionChannel.Stable, includePrerelease: false, cancellationToken);
				version = latestStable?.Version ?? throw new InvalidOperationException("Could not determine the latest stable MAUI version.");
			}
			else if (latestNightly)
			{
				var latestNightlyVersion = await feedService.GetLatestVersionAsync(MauiVersionChannel.Nightly, includePrerelease: true, cancellationToken);
				version = latestNightlyVersion?.Version ?? throw new InvalidOperationException("Could not determine the latest nightly MAUI version.");
				sourceForConfig ??= feedService.GetFeedUrl(MauiVersionChannel.Nightly);
				sourceNameForConfig = sourceName == ".NET MAUI Packages" ? ".NET MAUI Nightly" : sourceName;
			}
			else
			{
				version = explicitVersion!;
			}

			var versionResult = await projectService.SetVersionAsync(projectPath, version, dryRun: true, cancellationToken);
			var changes = new List<MauiProjectVersionChange>();
			if (nugetConfig)
			{
				if (string.IsNullOrWhiteSpace(sourceForConfig))
					throw new InvalidOperationException("--nuget-config requires --source unless --latest-nightly is used.");
				var nugetConfigChange = await projectService.EnsureNuGetSourceAsync(
					projectPath, sourceNameForConfig, sourceForConfig, dryRun, cancellationToken);
				if (nugetConfigChange is not null)
					changes.Add(nugetConfigChange);
			}

			var result = dryRun
				? versionResult
				: await projectService.SetVersionAsync(projectPath, version, dryRun: false, cancellationToken);
			changes.AddRange(result.Changes);

			MauiProjectRestoreResult? restoreResult = null;
			if (!dryRun && !noRestore && changes.Count > 0)
				restoreResult = await projectService.RestoreAsync(projectPath, cancellationToken);

			WriteUpdateResult(formatter, parseResult, result with { Changes = changes }, restoreResult);
			return 0;
		});

		return command;
	}

	static Command CreateVersionUseWorkloadCommand()
	{
		var noRestoreOption = new Option<bool>("--no-restore")
		{
			Description = "Do not run dotnet restore after updating files.",
		};
		var command = new Command("use-workload", "Use the installed MAUI workload version instead of a pinned project version")
		{
			noRestoreOption,
		};

		SetHandledAction(command, async (parseResult, cancellationToken) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var dryRun = Program.IsDryRun(parseResult);
			var projectService = Program.Services.GetRequiredService<IMauiProjectVersionService>();
			var projectPath = ResolveProjectPath(parseResult, projectService);
			var result = await projectService.UseWorkloadVersionAsync(projectPath, dryRun, cancellationToken);

			MauiProjectRestoreResult? restoreResult = null;
			if (!dryRun && !parseResult.GetValue(noRestoreOption) && result.Changed)
				restoreResult = await projectService.RestoreAsync(projectPath, cancellationToken);

			WriteUpdateResult(formatter, parseResult, result, restoreResult);
			return 0;
		});

		return command;
	}

	static void SetHandledAction(Command command, Func<ParseResult, CancellationToken, Task<int>> action)
	{
		command.SetAction((parseResult, cancellationToken) =>
			ExecuteHandledActionAsync(parseResult, cancellationToken, action));
	}

	static async Task<int> ExecuteHandledActionAsync(
		ParseResult parseResult,
		CancellationToken cancellationToken,
		Func<ParseResult, CancellationToken, Task<int>> action)
	{
		try
		{
			return await action(parseResult, cancellationToken);
		}
		catch (Exception exception)
		{
			var formatter = Program.GetFormatter(parseResult);
			return Program.HandleCommandException(formatter, exception);
		}
	}

	static async Task<int> ShowVersionAsync(ParseResult parseResult, CancellationToken cancellationToken)
	{
		var formatter = Program.GetFormatter(parseResult);
		var projectService = Program.Services.GetRequiredService<IMauiProjectVersionService>();
		var projectPath = ResolveProjectPath(parseResult, projectService);
		var info = await projectService.GetVersionInfoAsync(projectPath, cancellationToken);

		if (!info.IsMauiProject)
			throw new InvalidOperationException($"No .NET MAUI project markers found in {Path.GetFileName(projectPath)}.");

		if (parseResult.GetValue(GlobalOptions.JsonOption))
		{
			formatter.Write(info);
		}
		else
		{
			WriteProjectVersionInfo(formatter, info);
		}

		return 0;
	}

	static string ResolveProjectPath(ParseResult parseResult, IMauiProjectVersionService projectService)
	{
		var projectPath = parseResult.GetValue(ProjectOption);
		var resolvedProjectPath = projectService.DiscoverProjectFile(projectPath);
		if (resolvedProjectPath is null)
		{
			throw new InvalidOperationException(
				string.IsNullOrWhiteSpace(projectPath)
					? "No single .csproj file found. Run from a project directory or pass --project."
					: $"Project file not found or ambiguous: {projectPath}");
		}

		if (!File.Exists(resolvedProjectPath))
			throw new FileNotFoundException($"Project file not found: {resolvedProjectPath}", resolvedProjectPath);

		return resolvedProjectPath;
	}

	static MauiVersionChannel ParseChannel(string? channel) =>
		channel?.ToLowerInvariant() switch
		{
			null or "" or "stable" => MauiVersionChannel.Stable,
			"nightly" => MauiVersionChannel.Nightly,
			_ => throw new InvalidOperationException($"Invalid channel '{channel}'. Valid values: stable, nightly."),
		};

	static void WriteProjectVersionInfo(IOutputFormatter formatter, MauiProjectVersionInfo info)
	{
		formatter.WriteInfo($"Project: {Path.GetFileName(info.ProjectPath)}");
		formatter.WriteInfo($"MAUI project: {(info.UsesMaui ? "yes (<UseMaui>true</UseMaui>)" : "yes")}");

		if (info.MauiVersion is not null)
			formatter.WriteInfo($"MauiVersion property: {info.MauiVersion}");
		if (info.WorkloadVersion is not null)
			formatter.WriteInfo($"Installed workload version: {info.WorkloadVersion}");
		if (info.EffectiveVersion is not null)
			formatter.WriteSuccess($"Effective MAUI version: {info.EffectiveVersion}");
		else
			formatter.WriteWarning("No explicit MAUI version was found; the project will use the installed workload defaults.");

		if (info.HasMixedPackageVersions)
			formatter.WriteWarning("Mixed MAUI package versions detected.");

		if (info.Packages.Count > 0)
		{
			formatter.WriteTable(info.Packages,
				("Package", package => package.PackageId),
				("Version", FormatPackageVersion),
				("Source", package => package.Source),
				("File", package => Path.GetFileName(package.FilePath)));
		}
	}

	static string FormatPackageVersion(MauiProjectPackageVersion package)
	{
		if (package.Version is null)
			return "(implicit)";
		if (package.ResolvedVersion is null ||
			string.Equals(package.Version, package.ResolvedVersion, StringComparison.OrdinalIgnoreCase))
		{
			return package.Version;
		}

		return $"{package.Version} => {package.ResolvedVersion}";
	}

	static void WriteUpdateResult(
		IOutputFormatter formatter,
		ParseResult parseResult,
		MauiProjectVersionUpdateResult result,
		MauiProjectRestoreResult? restoreResult)
	{
		if (parseResult.GetValue(GlobalOptions.JsonOption))
		{
			formatter.Write(new MauiProjectVersionCommandResult
			{
				ProjectPath = result.ProjectPath,
				Version = result.Version,
				DryRun = result.DryRun,
				Changed = result.Changed,
				Changes = result.Changes,
				Restored = restoreResult?.Success,
			});
			return;
		}

		if (result.DryRun)
			formatter.WriteInfo($"[dry-run] Would set MAUI version to {result.Version}");

		if (result.Changes.Count == 0)
		{
			formatter.WriteSuccess($"Project already uses MAUI version {result.Version}.");
			return;
		}

		foreach (var change in result.Changes)
		{
			var oldValue = change.OldValue is null ? "(none)" : change.OldValue;
			var newValue = change.NewValue is null ? "(removed)" : change.NewValue;
			formatter.WriteInfo($"{change.Description}: {oldValue} -> {newValue} ({Path.GetFileName(change.FilePath)})");
		}

		if (restoreResult is not null)
			formatter.WriteSuccess("dotnet restore completed.");
		else if (!result.DryRun)
			formatter.WriteSuccess($"Updated MAUI version to {result.Version}.");
	}
}

public sealed record MauiVersionListResult
{
	public required string Channel { get; init; }
	public required string Feed { get; init; }
	public int TotalAvailable { get; init; }
	public List<MauiPackageFeedVersion> Versions { get; init; } = [];
}

public sealed record MauiProjectVersionCommandResult
{
	public required string ProjectPath { get; init; }
	public required string Version { get; init; }
	public bool DryRun { get; init; }
	public bool Changed { get; init; }
	public bool? Restored { get; init; }
	public List<MauiProjectVersionChange> Changes { get; init; } = [];
}
