// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.DevFlow.Skills;
using Microsoft.Maui.Cli.Output;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AiCommands
{
	/// <summary>
	/// Creates the <c>maui ai update</c> command that updates installed skills to the latest version.
	/// </summary>
	static Command CreateUpdateCommand()
	{
		var skillOption = new Option<string[]>("--skill")
		{
			Description = "Update only specific skills or agents (repeatable)",
			AllowMultipleArgumentsPerToken = true
		};

		var command = new Command("update", "Update installed AI development assets to the latest version")
		{
			CreateRepoOption(),
			CreateBranchOption(),
			CreateForceOption(),
			skillOption
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var isCi = parseResult.GetValue(GlobalOptions.CiOption);
			var dryRun = parseResult.GetValue(GlobalOptions.DryRunOption);
			var repo = parseResult.GetOption<string>("repo") ?? DefaultRepo;
			var branch = parseResult.GetOption<string>("branch") ?? DefaultBranch;
			var force = parseResult.GetOption<bool>("force");
			var skillFilter = parseResult.GetOption<string[]>("skill");

			try
			{
				var currentDir = Directory.GetCurrentDirectory();
				var workingDir = AgentEnvironmentDetector.ResolveProjectRoot(currentDir);
				var environments = AgentEnvironmentDetector.Detect(currentDir);

				if (environments.Count == 0)
				{
					formatter.WriteWarning("No agent environments detected. Run 'maui ai init' first.");
					return 1;
				}

				using var http = CreateGitHubHttpClient();

				List<SkillInfo> allSkills;
				List<RepositoryAssetInfo> allAgentAssets;
				if (!useJson && formatter is SpectreOutputFormatter spectre)
				{
					(allSkills, allAgentAssets) = await spectre.StatusAsync("Fetching AI assets...", async () =>
						await FetchBootstrapAssetsAsync(http, repo, branch, ct));
				}
				else
				{
					(allSkills, allAgentAssets) = await FetchBootstrapAssetsAsync(http, repo, branch, ct);
				}

				var filterSpecified = skillFilter is { Length: > 0 };
				var includeDevFlowSkills = filterSpecified
					? skillFilter!.Any(IsDevFlowManagedSkillName)
					: true;
				var devFlowTargets = includeDevFlowSkills
					? GetDevFlowBootstrapTargets(environments)
					: [];
				var selectedAgentAssets = filterSpecified
					? allAgentAssets
						.Where(asset => skillFilter!.Any(filter => string.Equals(filter, asset.Name, StringComparison.OrdinalIgnoreCase)))
						.ToList()
					: allAgentAssets;

				var devFlowStatusRows = await GetDevFlowStatusRowsAsync(devFlowTargets, ct);
				var devFlowTargetsToUpdate = devFlowTargets
					.Where(target => devFlowStatusRows
						.Where(row => row.Type == "DevFlow" && row.Target == target.DisplayName)
						.Any(row => NeedsUpdate(row, force)))
					.ToList();
				var agentStatusRows = await GetRemoteAgentStatusRowsAsync(http, selectedAgentAssets, workingDir, repo, branch, ct);
				var agentsToUpdate = agentStatusRows
					.Where(row => NeedsUpdate(row.Row, force))
					.ToList();

				// Scan installed marketplace/repository skills and check for updates; de-duplicate by resolved path
				// so environments sharing the same skills directory are not updated twice.
				var updatable = new List<(DetectedEnvironment Env, string SkillDir, string SkillName, InstalledSkillVersion Version)>();
				var processedPaths = new HashSet<string>(FileSystemPathComparer);
				var uncheckableCount = 0;

				foreach (var env in environments)
				{
					if (!Directory.Exists(env.SkillsDirectory))
						continue;

					foreach (var skillDir in Directory.GetDirectories(env.SkillsDirectory))
					{
						var resolvedPath = Path.GetFullPath(skillDir);
						if (!processedPaths.Add(resolvedPath))
							continue;

						var skillName = Path.GetFileName(skillDir);
						if (IsDevFlowManagedSkillName(skillName))
							continue;

						if (filterSpecified &&
							!skillFilter!.Any(f => string.Equals(f, skillName, StringComparison.OrdinalIgnoreCase)))
							continue;

						var version = await SkillVersionStore.ReadAsync(skillDir, ct);
						if (version is null)
							continue;

						// Check if update is available
						if (version.PluginPath is not null)
						{
							var remoteSha = await MarketplaceClient.GetRemoteCommitShaAsync(
								http, repo, branch, version.PluginPath, ct);

							if (remoteSha is null)
							{
								uncheckableCount++;
								if (force)
									updatable.Add((env, skillDir, skillName, version));
								continue;
							}

							// Only update when the remote SHA differs from local, unless --force.
							var needsUpdate = force ||
								version.Commit is null ||
								!string.Equals(remoteSha, version.Commit, StringComparison.OrdinalIgnoreCase);

							if (needsUpdate)
								updatable.Add((env, skillDir, skillName, version));
						}
					}
				}

				if (updatable.Count == 0 && devFlowTargetsToUpdate.Count == 0 && agentsToUpdate.Count == 0)
				{
					formatter.WriteSuccess(filterSpecified ? "All selected AI development assets are up to date." : "All AI development assets are up to date.");
					if (uncheckableCount > 0)
					{
						var skillWord = uncheckableCount == 1 ? "skill" : "skill(s)";
						formatter.WriteWarning($"Could not check {uncheckableCount} {skillWord} — GitHub may be unreachable.");
					}

					return 0;
				}

				var totalUpdates = updatable.Count + devFlowTargetsToUpdate.Count + agentsToUpdate.Count;
				var updateWord = totalUpdates == 1 ? "AI asset group" : "AI asset groups";
				formatter.WriteInfo($"Found {totalUpdates} {updateWord} with updates available.");

				var updateRows = new List<AiAssetStatusRow>();
				updateRows.AddRange(devFlowTargetsToUpdate.Select(target => new AiAssetStatusRow(
					"recommended DevFlow skills",
					"DevFlow",
					target.DisplayName,
					"",
					"Update",
					target.SkillsDirectory)));
				updateRows.AddRange(updatable.Select(u => new AiAssetStatusRow(
					u.SkillName,
					"Skill",
					u.Env.Kind.ToString(),
					ShortCommit(u.Version.Commit),
					"Update available",
					u.SkillDir)));
				updateRows.AddRange(agentsToUpdate.Select(row => row.Row));

				if (dryRun)
				{
					formatter.WriteInfo("[Dry run] Would update the following AI development assets:");
					formatter.WriteTable(
						updateRows,
						("Item", r => r.Item),
						("Type", r => r.Type),
						("Target", r => r.Target),
						("Status", r => r.Status),
						("Path", r => r.Path));
					return 0;
				}

				// Confirm unless --force, --ci, or --json
				if (!force && !isCi && !useJson)
				{
					formatter.WriteTable(
						updateRows,
						("Item", r => r.Item),
						("Type", r => r.Type),
						("Target", r => r.Target),
						("Status", r => r.Status),
						("Path", r => r.Path));

					if (!AnsiConsole.Confirm("Proceed with update?", defaultValue: true))
					{
						formatter.WriteInfo("Update cancelled.");
						return 0;
					}
				}

				var devFlowResults = new List<(string Skill, string Target, string Action, string Path)>();
				var results = new List<(string Skill, string Env, int Files)>();
				var assetResults = new List<(string Asset, string Type, int Files, string Path)>();

				foreach (var target in devFlowTargetsToUpdate)
				{
					var result = await DevFlowSkillManager.UpdateAsync(
						target.Scope,
						target.Target,
						target.CustomPath,
						force,
						allowDowngrade: false,
						confirm: null,
						ct);

					foreach (var row in GetDevFlowResultRows(result, target))
					{
						devFlowResults.Add(row);
						formatter.WriteSuccess($"DevFlow {row.Action} {row.Skill} → {row.Target}");
					}
				}

				foreach (var (env, skillDir, skillName, _) in updatable)
				{
					var skillInfo = allSkills.FirstOrDefault(s =>
						string.Equals(s.Name, skillName, StringComparison.OrdinalIgnoreCase));

					if (skillInfo is null)
					{
						formatter.WriteWarning($"Skill '{skillName}' not found in marketplace, skipping.");
						continue;
					}

					var (filesInstalled, _) = await SkillInstaller.InstallSkillAsync(
						http, skillInfo, env, workingDir, repo, branch, force: true, ct);

					results.Add((skillName, env.Kind.ToString(), filesInstalled));

					if (filesInstalled == -1)
						formatter.WriteWarning($"Skill '{skillName}' has an invalid name and cannot be updated.");
					else if (filesInstalled == -2)
						formatter.WriteWarning($"Failed to download skill files for '{skillName}'. Check your network connection.");
					else if (filesInstalled > 0)
						formatter.WriteSuccess($"Updated {skillName} → {env.Kind} ({filesInstalled} files)");
					else
						formatter.WriteInfo($"Skipped {skillName} → {env.Kind} (no files downloaded)");
				}

				foreach (var (asset, _) in agentsToUpdate)
				{
					var (filesInstalled, installPath) = await RepositoryAssetInstaller.InstallAssetAsync(
						http, asset, workingDir, repo, branch, force: true, ct);

					assetResults.Add((asset.Name, asset.Category, filesInstalled, installPath));
					if (filesInstalled > 0)
						formatter.WriteSuccess($"Updated {asset.Name} → {asset.Category} ({filesInstalled} files)");
					else
						formatter.WriteWarning($"Could not update {asset.Name} → {asset.Category}");
				}

				var hasUpdateFailures = HasUpdateInstallFailures(results.Select(r => r.Files), assetResults.Select(r => r.Files));
				if (useJson)
				{
					var jsonResult = new JsonObject
					{
						["status"] = GetUpdateStatus(hasUpdateFailures),
						["devFlowSkills"] = new JsonArray(devFlowResults.Select(r => (JsonNode)new JsonObject
						{
							["skill"] = r.Skill,
							["target"] = r.Target,
							["action"] = r.Action,
							["path"] = r.Path
						}).ToArray()),
						["updated"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject
						{
							["skill"] = r.Skill,
							["environment"] = r.Env,
							["files"] = r.Files
						}).ToArray()),
						["assets"] = new JsonArray(assetResults.Select(r => (JsonNode)new JsonObject
						{
							["asset"] = r.Asset,
							["type"] = r.Type,
							["files"] = r.Files,
							["path"] = r.Path
						}).ToArray())
					};
					formatter.Write(jsonResult);
				}

				if (uncheckableCount > 0)
				{
					var skillWord = uncheckableCount == 1 ? "skill" : "skill(s)";
					formatter.WriteWarning($"⚠ Could not check {uncheckableCount} {skillWord} — GitHub may be unreachable.");
				}

				return hasUpdateFailures ? 1 : 0;
			}
			catch (HttpRequestException ex)
			{
				formatter.WriteError(new Exception($"Network error: {ex.Message}. Check your connection or set GITHUB_TOKEN for higher rate limits."));
				return 1;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return command;
	}

	static string ShortCommit(string? commit)
		=> string.IsNullOrEmpty(commit)
			? "unknown"
			: commit[..Math.Min(commit.Length, 7)];

	internal static StringComparer FileSystemPathComparer =>
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	internal static bool HasUpdateInstallFailures(IEnumerable<int> skillFileCounts, IEnumerable<int> assetFileCounts)
		=> skillFileCounts.Any(files => files <= 0) || assetFileCounts.Any(files => files <= 0);

	internal static string GetUpdateStatus(bool hasUpdateFailures)
		=> hasUpdateFailures ? "partial_failure" : "success";
}
