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
			Description = "Update only specific skills or agents (repeatable); a bundled DevFlow skill selects its recommended skill group",
			Arity = ArgumentArity.OneOrMore,
			AllowMultipleArgumentsPerToken = true
		};

		var repoOption = CreateRepoOption();
		var branchOption = CreateBranchOption();
		var command = new Command("update", "Update installed AI development assets; without --skill, also install missing recommended DevFlow skills and repository agents")
		{
			repoOption,
			branchOption,
			CreateForceOption(),
			skillOption
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var isCi = parseResult.GetValue(GlobalOptions.CiOption);
			var dryRun = parseResult.GetValue(GlobalOptions.DryRunOption);
			var repoOverride = parseResult.GetResult(repoOption) is { Implicit: false } ? parseResult.GetValue(repoOption) : null;
			var branchOverride = parseResult.GetResult(branchOption) is { Implicit: false } ? parseResult.GetValue(branchOption) : null;
			var repo = repoOverride ?? DefaultRepo;
			var branch = branchOverride ?? DefaultBranch;
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
				var skillCatalogs = new Dictionary<(string Repo, string Branch), List<SkillInfo>>
				{
					[(repo, branch)] = allSkills
				};

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
				var hasUnknownAssets = agentStatusRows.Any(row => row.Row.Status is "Unknown" or "Error");

				// Scan installed marketplace/repository skills and check for updates; de-duplicate by resolved path
				// so environments sharing the same skills directory are not updated twice.
				var updatable = new List<(DetectedEnvironment Env, string SkillDir, string SkillName, InstalledSkillVersion Version)>();
				var processedPaths = new HashSet<string>(FileSystemPathComparer);
				var installedSkillFilterMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var uncheckableCount = 0;

				foreach (var env in environments)
				{
					foreach (var skillDir in EnumerateSkillDirectories(env))
					{
						var resolvedPath = Path.GetFullPath(skillDir);
						if (!processedPaths.Add(resolvedPath))
							continue;

						var skillName = Path.GetFileName(skillDir);
						if (IsDevFlowManagedSkillName(skillName))
							continue;

						if (filterSpecified)
						{
							var matchesFilter = skillFilter!.Any(f => string.Equals(f, skillName, StringComparison.OrdinalIgnoreCase));
							if (!matchesFilter)
								continue;

							installedSkillFilterMatches.Add(skillName);
						}

						var version = await SkillVersionStore.ReadAsync(skillDir, ct);
						if (version is null || string.IsNullOrWhiteSpace(version.PluginPath))
						{
							uncheckableCount++;
							continue;
						}

						// Check if update is available
						{
							var origin = ResolveInstalledSkillOrigin(version, repoOverride, branchOverride);
							var (isCheckable, remoteSha) = await TryGetRemoteCommitShaAsync(
								http, origin.Repo, origin.Branch, version.PluginPath, ct);

							if (!isCheckable || remoteSha is null)
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

				if (!HasUpdateFilterMatches(
					filterSpecified,
					devFlowTargets.Count,
					selectedAgentAssets.Count,
					installedSkillFilterMatches.Count))
				{
					formatter.WriteWarning($"No skills or agents matched filter: {string.Join(", ", skillFilter!)}");
					return 1;
				}

				if (updatable.Count == 0 && devFlowTargetsToUpdate.Count == 0 && agentsToUpdate.Count == 0)
				{
					if (uncheckableCount > 0 || hasUnknownAssets)
					{
						formatter.WriteWarning("Could not check all selected AI development assets — metadata may be missing or GitHub may be unreachable.");
						return 1;
					}

					formatter.WriteSuccess(filterSpecified ? "All selected AI development assets are up to date." : "All AI development assets are up to date.");
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
					return uncheckableCount > 0 || hasUnknownAssets ? 1 : 0;
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
				var stopAfterFailure = false;

				foreach (var target in devFlowTargetsToUpdate)
				{
					var result = await DevFlowSkillManager.UpdateAsync(
						target.Scope,
						target.Target,
						target.CustomPath,
						force,
						allowDowngrade: false,
						confirm: _ => force,
						ct);

					foreach (var row in GetDevFlowResultRows(result, target))
					{
						devFlowResults.Add(row);
						formatter.WriteSuccess($"DevFlow {row.Action} {row.Skill} → {row.Target}");
					}
				}

				foreach (var (env, skillDir, skillName, version) in updatable)
				{
					var origin = ResolveInstalledSkillOrigin(version, repoOverride, branchOverride);
					if (!skillCatalogs.TryGetValue(origin, out var catalog))
					{
						(catalog, _) = await FetchBootstrapAssetsAsync(http, origin.Repo, origin.Branch, ct);
						skillCatalogs.Add(origin, catalog);
					}
					var skillInfo = catalog.FirstOrDefault(s =>
						string.Equals(s.Name, skillName, StringComparison.Ordinal) &&
						string.Equals(s.RemotePath, version.PluginPath, StringComparison.Ordinal));

					if (skillInfo is null)
					{
						results.Add((skillName, env.Kind.ToString(), -2));
						formatter.WriteWarning($"Skill '{skillName}' was not found at '{version.PluginPath}' in '{origin.Repo}' ({origin.Branch}); it was not updated.");
						if (isCi)
						{
							stopAfterFailure = true;
							break;
						}
						continue;
					}

					var (filesInstalled, _) = await SkillInstaller.InstallSkillAsync(
						http, skillInfo, env, workingDir, origin.Repo, origin.Branch, force: true, ct);

					results.Add((skillName, env.Kind.ToString(), filesInstalled));

					if (filesInstalled == -1)
						formatter.WriteWarning($"Skill '{skillName}' has an invalid name and cannot be updated.");
					else if (filesInstalled == -2)
						formatter.WriteWarning($"Failed to download skill files for '{skillName}'. Check your network connection.");
					else if (filesInstalled > 0)
						formatter.WriteSuccess($"Updated {skillName} → {env.Kind} ({filesInstalled} files)");
					else
						formatter.WriteInfo($"Skipped {skillName} → {env.Kind} (no files downloaded)");

					if (isCi && filesInstalled < 0)
					{
						stopAfterFailure = true;
						break;
					}
				}

				foreach (var (asset, _) in agentsToUpdate)
				{
					if (stopAfterFailure)
						break;

					var (filesInstalled, installPath) = await RepositoryAssetInstaller.InstallAssetAsync(
						http, asset, workingDir, repo, branch, force: true, ct);

					assetResults.Add((asset.Name, asset.Category, filesInstalled, installPath));
					if (filesInstalled > 0)
						formatter.WriteSuccess($"Updated {asset.Name} → {asset.Category} ({filesInstalled} files)");
					else
						formatter.WriteWarning($"Could not update {asset.Name} → {asset.Category}");
					if (isCi && filesInstalled < 0)
						break;
				}

				var hasUpdateFailures = uncheckableCount > 0 || hasUnknownAssets ||
					HasUpdateInstallFailures(results.Select(r => r.Files), assetResults.Select(r => r.Files));
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
					formatter.WriteWarning($"⚠ Could not check {uncheckableCount} {skillWord} — metadata may be missing or GitHub may be unreachable.");
				}

				return hasUpdateFailures ? 1 : 0;
			}
			catch (HttpRequestException ex)
			{
				formatter.WriteError(new Exception($"Network error: {ex.Message}. Check your connection or set GITHUB_TOKEN for higher rate limits."));
				return 1;
			}
			catch (GitHubTreeTruncatedException ex)
			{
				return HandleGitHubTreeTruncatedException(formatter, ex);
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
		=> skillFileCounts.Any(files => files < 0) || assetFileCounts.Any(files => files < 0);

	internal static bool HasUpdateFilterMatches(
		bool filterSpecified,
		int devFlowTargetCount,
		int selectedAgentAssetCount,
		int installedSkillMatchCount)
		=> !filterSpecified || devFlowTargetCount > 0 || selectedAgentAssetCount > 0 || installedSkillMatchCount > 0;

	internal static string GetUpdateStatus(bool hasUpdateFailures)
		=> hasUpdateFailures ? "partial_failure" : "success";
}
