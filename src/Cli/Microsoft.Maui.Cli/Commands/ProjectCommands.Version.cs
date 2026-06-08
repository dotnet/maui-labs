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
			Description = "Specific .NET MAUI version to use. Omit when using --latest, --latest-nightly, or --pr.",
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
		var prOption = new Option<int?>("--pr", "--pull-request")
		{
			Description = "Set the project to the package version produced by a dotnet/maui pull request build.",
		};
		var nugetConfigOption = new Option<bool>("--nuget-config")
		{
			Description = "Add or update a NuGet.config source for the selected feed.",
		};
		var sourceOption = new Option<string>("--source")
		{
			Description = "NuGet v3 source URL to add to NuGet.config, useful for custom builds.",
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
		var hivePathOption = new Option<string?>("--hive-path", "--artifact-path")
		{
			Description = "MAUI hives root used for downloaded PR build packages. Defaults to ~/.maui/hives. --artifact-path is also accepted as an alias and has the same hive-root behavior.",
		};
		var targetFrameworkOption = new Option<string?>("--target-framework", "--framework", "-f")
		{
			Description = "Update project target frameworks to the specified netX.Y value, for example net10.0. Values like 10.0 are normalized to net10.0.",
		};

		var command = new Command("set", "Set the .NET MAUI version used by a project")
		{
			versionArgument,
			latestOption,
			latestNightlyOption,
			prOption,
			nugetConfigOption,
			sourceOption,
			sourceNameOption,
			noRestoreOption,
			hivePathOption,
			targetFrameworkOption,
		};

		SetHandledAction(command, async (parseResult, cancellationToken) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var dryRun = Program.IsDryRun(parseResult);
			var projectService = Program.Services.GetRequiredService<IMauiProjectVersionService>();
			var feedService = Program.Services.GetRequiredService<IMauiVersionFeedService>();
			var projectPath = ResolveProjectPath(parseResult, projectService);
			var explicitVersion = parseResult.GetValue(versionArgument);
			var latest = parseResult.GetValue(latestOption);
			var latestNightly = parseResult.GetValue(latestNightlyOption);
			var prNumber = parseResult.GetValue(prOption);
			var noRestore = parseResult.GetValue(noRestoreOption);
			var nugetConfig = parseResult.GetValue(nugetConfigOption);
			var source = parseResult.GetValue(sourceOption);
			var sourceName = parseResult.GetValue(sourceNameOption) ?? ".NET MAUI Packages";
			var hivePath = parseResult.GetValue(hivePathOption);
			var targetFrameworkInput = parseResult.GetValue(targetFrameworkOption);
			var targetFramework = string.IsNullOrWhiteSpace(targetFrameworkInput)
				? null
				: MauiProjectVersionService.NormalizeTargetFramework(targetFrameworkInput);
			var sourceSpecified = parseResult.GetResult(sourceOption)?.Tokens.Count > 0;
			var sourceNameSpecified = parseResult.GetResult(sourceNameOption)?.Tokens.Count > 0;

			var sourceCount =
				(string.IsNullOrWhiteSpace(explicitVersion) ? 0 : 1) +
				(latest ? 1 : 0) +
				(latestNightly ? 1 : 0) +
				(prNumber.HasValue ? 1 : 0);
			if (sourceCount == 0)
				throw new InvalidOperationException("Specify a version, --latest, --latest-nightly, or --pr.");
			if (sourceCount > 1)
				throw new InvalidOperationException("Specify only one of version, --latest, --latest-nightly, or --pr.");
			if (prNumber.HasValue && prNumber.Value <= 0)
				throw new InvalidOperationException("--pr must be a positive pull request number.");
			if (prNumber.HasValue && sourceSpecified)
				throw new InvalidOperationException("--pr downloads PackageArtifacts and cannot be combined with --source.");
			if (!nugetConfig && !prNumber.HasValue && (sourceSpecified || sourceNameSpecified))
				throw new InvalidOperationException("--source and --source-name require --nuget-config.");
			if (!prNumber.HasValue && !string.IsNullOrWhiteSpace(hivePath))
				throw new InvalidOperationException("--hive-path can only be used with --pr.");

			var projectInfo = await projectService.GetVersionInfoAsync(projectPath, cancellationToken);
			var selectionTargetFramework = targetFramework ?? projectInfo.TargetDotNetFramework;

			string version;
			string? sourceForConfig = source;
			string sourceNameForConfig = sourceName;
			MauiPrArtifactDownload? prDownload = null;
			MauiPrBuildArtifact? prBuild = null;
			MauiPrBuildProgress? inProgressBuild = null;
			IMauiPrArtifactService? prArtifactService = null;
			string? selectionMessage = null;
			if (latest)
			{
				var latestStable = await feedService.GetLatestVersionAsync(MauiVersionChannel.Stable, includePrerelease: false, selectionTargetFramework, cancellationToken);
				version = latestStable?.Version ?? throw new InvalidOperationException(
					selectionTargetFramework is null
						? "Could not determine the latest stable MAUI version."
						: $"Could not determine a stable MAUI version compatible with {selectionTargetFramework}.");
				if (selectionTargetFramework is not null)
					selectionMessage = $"Selected MAUI version {version} compatible with {selectionTargetFramework}.";
			}
			else if (latestNightly)
			{
				var latestNightlyVersion = await feedService.GetLatestVersionAsync(MauiVersionChannel.Nightly, includePrerelease: true, selectionTargetFramework, cancellationToken);
				version = latestNightlyVersion?.Version ?? throw new InvalidOperationException(
					selectionTargetFramework is null
						? "Could not determine the latest nightly MAUI version."
						: $"Could not determine a nightly MAUI version compatible with {selectionTargetFramework}.");
				sourceForConfig ??= feedService.GetFeedUrl(MauiVersionChannel.Nightly);
				sourceNameForConfig = sourceName == ".NET MAUI Packages" ? ".NET MAUI Nightly" : sourceName;
				if (selectionTargetFramework is not null)
					selectionMessage = $"Selected MAUI version {version} compatible with {selectionTargetFramework}.";
			}
			else if (prNumber.HasValue)
			{
				prArtifactService = Program.Services.GetRequiredService<IMauiPrArtifactService>();
				inProgressBuild = await prArtifactService.FindInProgressBuildAsync(prNumber.Value, cancellationToken);
				try
				{
					prBuild = await prArtifactService.FindPackageArtifactAsync(prNumber.Value, cancellationToken);
				}
				catch (InvalidOperationException exception) when (inProgressBuild is not null)
				{
					throw new InvalidOperationException(
						$"dotnet/maui PR #{prNumber.Value} has a build in progress ({inProgressBuild.Status}, build {inProgressBuild.BuildId}). " +
						"Try again after it completes; no completed PackageArtifacts build is available yet.",
						exception);
				}
				sourceNameForConfig = sourceNameSpecified ? sourceName : ".NET MAUI PR Build";

				if (dryRun)
				{
					WritePrDryRunResult(formatter, parseResult, projectPath, prArtifactService, prBuild, hivePath, sourceNameForConfig, inProgressBuild);
					return 0;
				}

				if (!useJson)
				{
					if (inProgressBuild is not null)
						formatter.WriteWarning($"dotnet/maui PR #{prNumber.Value} also has an in-progress build {inProgressBuild.BuildId} ({inProgressBuild.Status}); using the latest completed PackageArtifacts build.");
					formatter.WriteInfo($"Found dotnet/maui PR #{prNumber.Value} build {prBuild.BuildId} ({prBuild.BuildNumber}).");
				}

				prDownload = await prArtifactService.StagePackageArtifactAsync(prBuild, hivePath, cancellationToken);
				version = prDownload.Version;
				sourceForConfig = prDownload.PackageSourcePath;
			}
			else
			{
				version = explicitVersion!;
			}

			try
			{
				EnsureTargetFrameworkCompatibility(version, projectInfo, targetFramework);
				MauiProjectVersionUpdateResult? targetFrameworkResult = null;
				if (targetFramework is not null)
					targetFrameworkResult = await projectService.SetTargetFrameworkAsync(projectPath, targetFramework, dryRun: true, cancellationToken);

				if (!useJson && selectionMessage is not null)
					formatter.WriteInfo(selectionMessage);

				var versionResult = await projectService.SetVersionAsync(projectPath, version, dryRun: true, cancellationToken);
				MauiProjectVersionChange? nugetConfigChange = null;
				var shouldUpdateNuGetConfig = nugetConfig || prDownload is not null;
				if (shouldUpdateNuGetConfig)
				{
					if (string.IsNullOrWhiteSpace(sourceForConfig))
						throw new InvalidOperationException("--nuget-config requires --source unless --latest-nightly is used.");
					nugetConfigChange = await projectService.EnsureNuGetSourceAsync(
						projectPath, sourceNameForConfig, sourceForConfig, dryRun: true, cancellationToken);
				}

				if (prDownload?.IsStaged == true && prArtifactService is not null)
				{
					if (dryRun)
					{
						prArtifactService.DiscardStagedPackageArtifact(prDownload);
					}
					else
					{
						await prArtifactService.PromoteStagedPackageArtifactAsync(prDownload, cancellationToken);
						prDownload = prDownload with { IsStaged = false, StagingArtifactPath = null };
						if (!useJson)
							formatter.WriteInfo($"Downloaded PR packages to {prDownload.PackageSourcePath}.");
					}
				}

				var changes = new List<MauiProjectVersionChange>();
				if (targetFrameworkResult is not null)
				{
					var appliedTargetFrameworkResult = dryRun
						? targetFrameworkResult
						: await projectService.SetTargetFrameworkAsync(projectPath, targetFramework!, dryRun: false, cancellationToken);
					changes.AddRange(appliedTargetFrameworkResult.Changes);
				}
				if (shouldUpdateNuGetConfig)
				{
					var appliedNuGetConfigChange = dryRun
						? nugetConfigChange
						: await projectService.EnsureNuGetSourceAsync(projectPath, sourceNameForConfig, sourceForConfig!, dryRun: false, cancellationToken);
					if (appliedNuGetConfigChange is not null)
						changes.Add(appliedNuGetConfigChange);
				}

				var result = dryRun
					? versionResult
					: await projectService.SetVersionAsync(projectPath, version, dryRun: false, cancellationToken);
				changes.AddRange(result.Changes);

				MauiProjectRestoreResult? restoreResult = null;
				if (!dryRun && !noRestore && changes.Count > 0)
					restoreResult = await projectService.RestoreAsync(projectPath, cancellationToken);

				WriteUpdateResult(formatter, parseResult, result with { Changes = changes }, restoreResult, prDownload, prDownload is null ? null : sourceNameForConfig, targetFramework, inProgressBuild);
				return 0;
			}
			catch
			{
				if (prDownload?.IsStaged == true && prArtifactService is not null)
					prArtifactService.DiscardStagedPackageArtifact(prDownload);
				throw;
			}
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

	static void EnsureTargetFrameworkCompatibility(
		string version,
		MauiProjectVersionInfo projectInfo,
		string? requestedTargetFramework)
	{
		var requiredTargetFramework = MauiProjectVersionService.TryGetRequiredTargetFramework(version);
		if (requiredTargetFramework is null)
			return;

		if (requestedTargetFramework is not null)
		{
			if (!string.Equals(requestedTargetFramework, requiredTargetFramework, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					$"MAUI version {version} requires {requiredTargetFramework}, but --framework was {requestedTargetFramework}.");
			}

			return;
		}

		if (projectInfo.TargetDotNetFramework is null)
		{
			if (projectInfo.TargetFrameworks.Count > 0)
			{
				throw new InvalidOperationException(
					$"MAUI version {version} requires {requiredTargetFramework}, but {Path.GetFileName(projectInfo.ProjectPath)} targets multiple or non-literal frameworks. " +
					$"Rerun with --framework {requiredTargetFramework} to update project target frameworks.");
			}

			return;
		}

		if (!string.Equals(projectInfo.TargetDotNetFramework, requiredTargetFramework, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"MAUI version {version} requires {requiredTargetFramework}, but {Path.GetFileName(projectInfo.ProjectPath)} targets {projectInfo.TargetDotNetFramework}. " +
				$"Rerun with --framework {requiredTargetFramework} to update project target frameworks.");
		}
	}

	static void WriteProjectVersionInfo(IOutputFormatter formatter, MauiProjectVersionInfo info)
	{
		formatter.WriteInfo($"Project: {Path.GetFileName(info.ProjectPath)}");
		formatter.WriteInfo($"MAUI project: {(info.UsesMaui ? "yes (<UseMaui>true</UseMaui>)" : "yes")}");

		if (info.MauiVersion is not null)
			formatter.WriteInfo($"MauiVersion property: {info.MauiVersion}");
		if (info.WorkloadVersion is not null)
			formatter.WriteInfo($"Installed workload version: {info.WorkloadVersion}");
		if (info.TargetFrameworks.Count > 0)
			formatter.WriteInfo($"Target frameworks: {string.Join("; ", info.TargetFrameworks)}");
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
		MauiProjectRestoreResult? restoreResult,
		MauiPrArtifactDownload? prDownload = null,
		string? prSourceName = null,
		string? targetFramework = null,
		MauiPrBuildProgress? inProgressBuild = null)
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
				PullRequest = prDownload?.Build.PullRequest,
				BuildId = prDownload?.Build.BuildId,
				BuildNumber = prDownload?.Build.BuildNumber,
				BuildUrl = prDownload?.Build.BuildUrl,
				HiveRoot = prDownload?.HiveRoot,
				ArtifactPath = prDownload?.ArtifactPath,
				PackageSourcePath = prDownload?.PackageSourcePath,
				MetadataPath = prDownload?.MetadataPath,
				SourceName = prSourceName,
				TargetFramework = targetFramework,
				BuildInProgress = inProgressBuild is not null,
				InProgressBuildId = inProgressBuild?.BuildId,
				InProgressBuildNumber = inProgressBuild?.BuildNumber,
				InProgressBuildUrl = inProgressBuild?.BuildUrl,
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

	static void WritePrDryRunResult(
		IOutputFormatter formatter,
		ParseResult parseResult,
		string projectPath,
		IMauiPrArtifactService prArtifactService,
		MauiPrBuildArtifact build,
		string? hiveRoot,
		string sourceName,
		MauiPrBuildProgress? inProgressBuild)
	{
		var paths = prArtifactService.GetHivePaths(build, hiveRoot);

		if (parseResult.GetValue(GlobalOptions.JsonOption))
		{
			formatter.Write(new MauiProjectVersionCommandResult
			{
				ProjectPath = projectPath,
				DryRun = true,
				Changed = false,
				PullRequest = build.PullRequest,
				BuildId = build.BuildId,
				BuildNumber = build.BuildNumber,
				BuildUrl = build.BuildUrl,
				HiveRoot = paths.HiveRoot,
				ArtifactPath = paths.HivePath,
				PackageSourcePath = paths.PackageSourcePath,
				MetadataPath = paths.MetadataPath,
				SourceName = sourceName,
				BuildInProgress = inProgressBuild is not null,
				InProgressBuildId = inProgressBuild?.BuildId,
				InProgressBuildNumber = inProgressBuild?.BuildNumber,
				InProgressBuildUrl = inProgressBuild?.BuildUrl,
			});
			return;
		}

		if (inProgressBuild is not null)
			formatter.WriteWarning($"dotnet/maui PR #{build.PullRequest} also has an in-progress build {inProgressBuild.BuildId} ({inProgressBuild.Status}); this dry run uses the latest completed PackageArtifacts build.");
		formatter.WriteInfo($"[dry-run] Found dotnet/maui PR #{build.PullRequest} build {build.BuildId} ({build.BuildNumber}).");
		formatter.WriteInfo($"[dry-run] Would download {build.ArtifactName} into {paths.HivePath} and add NuGet source '{sourceName}'.");
		formatter.WriteInfo("[dry-run] Would set MAUI version to the Microsoft.Maui.Controls version discovered in the downloaded artifact.");
		formatter.WriteInfo("[dry-run] PR artifact target framework compatibility is validated after downloading the package version during a real run.");
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
	public string? Version { get; init; }
	public bool DryRun { get; init; }
	public bool Changed { get; init; }
	public bool? Restored { get; init; }
	public List<MauiProjectVersionChange> Changes { get; init; } = [];
	public int? PullRequest { get; init; }
	public int? BuildId { get; init; }
	public string? BuildNumber { get; init; }
	public string? BuildUrl { get; init; }
	public string? HiveRoot { get; init; }
	public string? ArtifactPath { get; init; }
	public string? PackageSourcePath { get; init; }
	public string? MetadataPath { get; init; }
	public string? SourceName { get; init; }
	public string? TargetFramework { get; init; }
	public bool BuildInProgress { get; init; }
	public int? InProgressBuildId { get; init; }
	public string? InProgressBuildNumber { get; init; }
	public string? InProgressBuildUrl { get; init; }
}
