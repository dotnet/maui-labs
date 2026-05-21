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
	/// Creates the <c>maui ai init</c> command that bootstraps agent skill installation.
	/// </summary>
	static Command CreateInitCommand()
	{
		var skillOption = new Option<string[]>("--skill")
		{
			Description = "Install only specific skills (repeatable)",
			AllowMultipleArgumentsPerToken = true
		};

		var envOption = new Option<string[]>("--env")
		{
			Description = "Target only specific environments (repeatable, e.g. Claude, VsCode)",
			AllowMultipleArgumentsPerToken = true
		};

		var command = new Command("init", "Bootstrap AI-powered MAUI development assets")
		{
			CreateRepoOption(),
			CreateBranchOption(),
			CreateForceOption(),
			new Option<bool>("--no-mcp") { Description = "Skip MCP server configuration" },
			skillOption,
			envOption
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
			var noMcp = parseResult.GetOption<bool>("no-mcp");
			var skillFilter = parseResult.GetOption<string[]>("skill");
			var envFilter = parseResult.GetOption<string[]>("env");

			try
			{
				using var http = CreateGitHubHttpClient();

				// Step 1: Fetch marketplace and discover repository AI assets.
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

				// Step 2: Detect agent environments
				var currentDir = Directory.GetCurrentDirectory();
				var workingDir = GetAiCommandWorkingDirectory(currentDir);
				var environments = AgentEnvironmentDetector.Detect(currentDir);

				environments = FilterEnvironments(environments, envFilter);

				if (environments.Count == 0)
				{
					var canCreateDefaultClaudeEnvironment = ShouldCreateDefaultClaudeEnvironment(envFilter);
					if ((isCi || force) && canCreateDefaultClaudeEnvironment)
					{
						// In CI or force mode, create .claude/ by default
						var claudeDir = Path.Combine(workingDir, ".claude");
						Directory.CreateDirectory(claudeDir);
						environments = FilterEnvironments(AgentEnvironmentDetector.Detect(currentDir), envFilter);
					}
					else if (!canCreateDefaultClaudeEnvironment || useJson || isCi || force)
					{
						formatter.WriteWarning("No agent environments detected.");
						return 1;
					}
					else
					{
						// Prompt user to create a default environment
						formatter.WriteWarning("No agent environments detected.");
						var create = AnsiConsole.Confirm(
							"Create [cyan].claude/[/] directory for Claude Code?", defaultValue: true);
						if (!create)
						{
							formatter.WriteInfo("No environments to configure. Exiting.");
							return 0;
						}

						var claudeDir = Path.Combine(workingDir, ".claude");
						Directory.CreateDirectory(claudeDir);
						environments = FilterEnvironments(AgentEnvironmentDetector.Detect(currentDir), envFilter);
					}
				}

				var envWord = environments.Count == 1 ? "environment" : "environments";
				formatter.WriteInfo($"Detected {environments.Count} {envWord}: {string.Join(", ", environments.Select(e => e.Kind))}");

				// Step 3: Select skills
				var filterSpecified = skillFilter is { Length: > 0 };
				var selectedSkills = SelectSkills(allSkills, skillFilter, useJson, isCi, formatter);
				var includeDevFlowSkills = filterSpecified
					? skillFilter!.Any(IsDevFlowManagedSkillName)
					: selectedSkills.Any(s => IsDevFlowManagedSkillName(s.Name));

				if (!filterSpecified && !useJson && !isCi && allSkills.Count > 0 && selectedSkills.Count == 0)
				{
					formatter.WriteInfo("No skills selected.");
					return 0;
				}

				if (selectedSkills.Count == 0 && !includeDevFlowSkills)
				{
					if (filterSpecified)
					{
						formatter.WriteError(new Exception($"No skills matched filter: {string.Join(", ", skillFilter!)}"));
						return 1;
					}

					if (allAgentAssets.Count == 0)
					{
						formatter.WriteInfo("No skills selected.");
						return 0;
					}
				}

				var selectedMarketplaceSkills = selectedSkills
					.Where(s => !IsDevFlowManagedSkillName(s.Name))
					.ToList();
				List<RepositoryAssetInfo> selectedAgentAssets = filterSpecified ? [] : allAgentAssets;
				List<AiDevFlowBootstrapTarget> devFlowTargets = includeDevFlowSkills
					? GetDevFlowBootstrapTargets(environments)
					: [];
				var skillInstallEnvironments = GetUniqueSkillInstallEnvironments(environments);

				// Step 4: Dry run check (before confirmation prompt)
				if (dryRun)
				{
					var dryRunRows = new List<(string Item, string Type, string Target, string Path)>();

					foreach (var target in devFlowTargets)
						dryRunRows.Add(("recommended DevFlow skills", "DevFlow", target.DisplayName, target.SkillsDirectory));

					foreach (var skill in selectedMarketplaceSkills)
					{
						foreach (var env in skillInstallEnvironments)
							dryRunRows.Add((skill.Name, "Skill", env.Kind.ToString(), Path.Combine(env.SkillsDirectory, skill.Name)));
					}

					foreach (var asset in selectedAgentAssets)
						dryRunRows.Add((asset.Name, asset.Category, "GitHub Copilot", Path.Combine(workingDir, asset.DestinationRoot)));

					formatter.WriteInfo("[Dry run] Would install the following AI assets:");
					formatter.WriteTable(
						dryRunRows,
						("Item", r => r.Item),
						("Type", r => r.Type),
						("Target", r => r.Target),
						("Path", r => r.Path));
					return 0;
				}

				// Step 5: Confirmation
				if (!force && !isCi && !useJson)
				{
					var totalItems = selectedMarketplaceSkills.Count + selectedAgentAssets.Count + (includeDevFlowSkills ? 1 : 0);
					var itemWord = totalItems == 1 ? "AI asset group" : "AI asset groups";
					formatter.WriteInfo($"Will install {totalItems} {itemWord} to {environments.Count} {envWord}:");

					var rows = new List<(string Item, string Type, string Source)>();
					if (includeDevFlowSkills)
						rows.Add(("recommended DevFlow skills", "DevFlow", "Microsoft.Maui.Cli bundle"));
					rows.AddRange(selectedMarketplaceSkills.Select(s => (s.Name, "Skill", s.PluginName)));
					rows.AddRange(selectedAgentAssets.Select(a => (a.Name, a.Category, a.RemotePath)));

					formatter.WriteTable(
						rows,
						("Item", r => r.Item),
						("Type", r => r.Type),
						("Source", r => r.Source));

					if (!AnsiConsole.Confirm("Proceed with installation?", defaultValue: true))
					{
						formatter.WriteInfo("Installation cancelled.");
						return 0;
					}
				}

				// Step 6: Install DevFlow skills through the DevFlow-owned bundled installer.
				var devFlowResults = new List<(string Skill, string Target, string Action, string Path)>();
				foreach (var target in devFlowTargets)
				{
					var result = await DevFlowSkillManager.InstallRecommendedAsync(
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

				// Step 7: Install marketplace/repository skills not owned by DevFlow.
				var skillResults = new List<(string Skill, string Env, int Files, string Path)>();
				foreach (var env in skillInstallEnvironments)
				{
					foreach (var skill in selectedMarketplaceSkills)
					{
						var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
							http, skill, env, workingDir, repo, branch, force, ct);

						skillResults.Add((skill.Name, env.Kind.ToString(), filesInstalled, installPath));

						if (filesInstalled == -1)
						{
							formatter.WriteWarning($"Skill '{skill.Name}' has an invalid name or unsafe install path and cannot be installed.");
						}
						else if (filesInstalled == -2)
						{
							formatter.WriteWarning($"Failed to download skill files for '{skill.Name}'. Check your network connection.");
						}
						else if (filesInstalled > 0)
							formatter.WriteSuccess($"Installed {skill.Name} → {env.Kind} ({filesInstalled} files)");
						else
							formatter.WriteInfo($"Skipped {skill.Name} → {env.Kind} (already installed)");
					}
				}

				// Step 8: Install Copilot agent definitions.
				var assetResults = new List<(string Asset, string Type, int Files, string Path)>();
				foreach (var asset in selectedAgentAssets)
				{
					var (filesInstalled, installPath) = await RepositoryAssetInstaller.InstallAssetAsync(
						http, asset, workingDir, repo, branch, force, ct);

					assetResults.Add((asset.Name, asset.Category, filesInstalled, installPath));
					if (filesInstalled > 0)
						formatter.WriteSuccess($"Installed {asset.Name} → {asset.Category} ({filesInstalled} files)");
					else if (filesInstalled == -1)
						formatter.WriteWarning($"Skipped {asset.Name} → {asset.Category} (unsafe install path)");
					else if (filesInstalled == -2)
						formatter.WriteWarning($"Failed to download asset files for '{asset.Name}'. Check your network connection.");
					else
						formatter.WriteInfo($"Skipped {asset.Name} → {asset.Category} (already installed)");
				}

				// Step 9: Configure MCP
				if (!noMcp)
				{
					foreach (var env in environments)
					{
						var ok = await McpConfigurator.ConfigureAsync(env, workingDir, ct);
						if (ok)
							formatter.WriteSuccess($"MCP configured for {env.Kind}");
						else
							formatter.WriteWarning($"Could not configure MCP for {env.Kind}");
					}

					if (!useJson)
						formatter.WriteInfo("Restart your editor to load the MCP server configuration.");
				}

				// Step 10: Summary
				var summaryRows = new List<(string Item, string Type, string Target, string Result, string Path)>();
				summaryRows.AddRange(devFlowResults.Select(r => (r.Skill, "DevFlow", r.Target, r.Action, r.Path)));
				summaryRows.AddRange(skillResults.Select(r => (r.Skill, "Skill", r.Env, FormatFileResult(r.Files), r.Path)));
				summaryRows.AddRange(assetResults.Select(r => (r.Asset, r.Type, "GitHub Copilot", FormatFileResult(r.Files), r.Path)));
				var hasInstallFailures = HasInitInstallFailures(skillResults.Select(r => r.Files), assetResults.Select(r => r.Files));

				if (useJson)
				{
					var jsonResult = new JsonObject
					{
						["status"] = GetInitStatus(hasInstallFailures),
						["devFlowSkills"] = new JsonArray(devFlowResults.Select(r => (JsonNode)new JsonObject
						{
							["skill"] = r.Skill,
							["target"] = r.Target,
							["action"] = r.Action,
							["path"] = r.Path
						}).ToArray()),
						["skills"] = new JsonArray(skillResults.Select(r => (JsonNode)new JsonObject
						{
							["skill"] = r.Skill,
							["environment"] = r.Env,
							["files"] = r.Files,
							["path"] = r.Path
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
				else
				{
					formatter.WriteTable(
						summaryRows,
						("Item", r => r.Item),
						("Type", r => r.Type),
						("Target", r => r.Target),
						("Result", r => r.Result),
						("Path", r => r.Path));

					AnsiConsole.WriteLine();
					AnsiConsole.MarkupLine("[dim]Next steps:[/]");
					AnsiConsole.MarkupLine("[dim]  Open your AI agent and ask it to use the installed .NET MAUI skills.[/]");
					AnsiConsole.MarkupLine("[dim]  For DevFlow, start with [green]maui-devflow-onboard[/] to add runtime automation to this project.[/]");
					AnsiConsole.MarkupLine("[dim]  Run [green]maui ai status[/] to check installed marketplace skills.[/]");
					AnsiConsole.MarkupLine("[dim]  Run [green]maui devflow skills check[/] to check bundled DevFlow skills.[/]");
				}

				return hasInstallFailures ? 1 : 0;
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

	static async Task<(List<SkillInfo> Skills, List<RepositoryAssetInfo> AgentAssets)> FetchBootstrapAssetsAsync(
		HttpClient http, string repo, string branch, CancellationToken ct = default)
	{
		var treeEntries = await MarketplaceClient.FetchTreeEntriesAsync(http, repo, branch, ct);
		var skills = await FetchAllSkillsAsync(http, repo, branch, treeEntries, ct);
		var agentAssets = await RepositoryAssetInstaller.GetCopilotAgentsAsync(http, repo, branch, treeEntries, ct);
		return (skills, agentAssets);
	}

	static List<SkillInfo> SelectSkills(
		List<SkillInfo> allSkills,
		string[]? skillFilter,
		bool useJson,
		bool isCi,
		IOutputFormatter formatter)
	{
		if (skillFilter is { Length: > 0 })
		{
			return allSkills
				.Where(s => skillFilter.Any(f =>
					string.Equals(f, s.Name, StringComparison.OrdinalIgnoreCase)))
				.ToList();
		}

		if (useJson || isCi || allSkills.Count == 0)
			return allSkills;

		var prompt = new MultiSelectionPrompt<string>()
			.Title("Select skills to install:")
			.PageSize(15)
			.InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]");

		foreach (var skill in allSkills)
		{
			var label = GetSkillPromptLabel(skill);
			prompt.AddChoice(label);
			prompt.Select(label);
		}

		var selected = AnsiConsole.Prompt(prompt);
		return allSkills
			.Where(s => selected.Contains(GetSkillPromptLabel(s)))
			.ToList();
	}

	static string GetSkillPromptLabel(SkillInfo skill) =>
		string.IsNullOrEmpty(skill.Description)
			? skill.Name
			: $"{skill.Name} - {skill.Description}";

	internal static List<DetectedEnvironment> FilterEnvironments(
		IEnumerable<DetectedEnvironment> environments,
		string[]? envFilter)
	{
		if (envFilter is not { Length: > 0 })
			return environments.ToList();

		return environments
			.Where(e => envFilter.Any(f =>
				string.Equals(f, e.Kind.ToString(), StringComparison.OrdinalIgnoreCase)))
			.ToList();
	}

	internal static bool ShouldCreateDefaultClaudeEnvironment(string[]? envFilter)
		=> envFilter is not { Length: > 0 } ||
			envFilter.Any(f => string.Equals(f, AgentEnvironmentKind.Claude.ToString(), StringComparison.OrdinalIgnoreCase));

	internal static List<AiDevFlowBootstrapTarget> GetDevFlowBootstrapTargets(IEnumerable<DetectedEnvironment> environments)
	{
		var targets = new List<AiDevFlowBootstrapTarget>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var env in environments)
		{
			var target = env.Kind switch
			{
				AgentEnvironmentKind.Claude => new AiDevFlowBootstrapTarget("project", "claude", null, env.Kind.ToString(), env.SkillsDirectory),
				AgentEnvironmentKind.VsCode => new AiDevFlowBootstrapTarget("project", "github", null, env.Kind.ToString(), env.SkillsDirectory),
				AgentEnvironmentKind.CopilotCli => new AiDevFlowBootstrapTarget("project", "github", null, env.Kind.ToString(), env.SkillsDirectory),
				AgentEnvironmentKind.OpenCode => new AiDevFlowBootstrapTarget("project", "auto", Path.Combine(".opencode", "skills"), env.Kind.ToString(), env.SkillsDirectory),
				_ => null
			};

			if (target is null)
				continue;

			var key = $"{target.Scope}|{target.Target}|{target.CustomPath}";
			if (seen.Add(key))
				targets.Add(target);
		}

		return targets;
	}

	static List<DetectedEnvironment> GetUniqueSkillInstallEnvironments(IEnumerable<DetectedEnvironment> environments)
	{
		var unique = new List<DetectedEnvironment>();
		var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		foreach (var env in environments)
		{
			var path = Path.GetFullPath(env.SkillsDirectory);
			if (seen.Add(path))
				unique.Add(env);
		}

		return unique;
	}

	static IEnumerable<(string Skill, string Target, string Action, string Path)> GetDevFlowResultRows(
		JsonObject result,
		AiDevFlowBootstrapTarget target)
	{
		if (result["results"] is not JsonArray results)
			yield break;

		foreach (var item in results.OfType<JsonObject>())
		{
			yield return (
				GetJsonString(item, "skillId") ?? "unknown",
				target.DisplayName,
				GetJsonString(item, "action") ?? "checked",
				GetJsonString(item, "path") ?? target.SkillsDirectory);
		}
	}

	static string? GetJsonString(JsonObject item, string propertyName) =>
		item[propertyName]?.GetValue<string>();

	static string FormatFileResult(int files) =>
		files switch
		{
			-1 => "invalid",
			-2 => "failed",
			0 => "skipped",
			_ => files.ToString()
		};

	internal static bool HasInitInstallFailures(IEnumerable<int> skillFileCounts, IEnumerable<int> assetFileCounts)
		=> HasSkillInstallFailures(skillFileCounts) || assetFileCounts.Any(files => files < 0);

	internal static string GetInitStatus(bool hasInstallFailures)
		=> hasInstallFailures ? "partial" : "success";

	internal static bool HasSkillInstallFailures(IEnumerable<int> skillFileCounts)
		=> skillFileCounts.Any(files => files < 0);

	internal static string GetAiCommandWorkingDirectory(string currentDir)
		=> AgentEnvironmentDetector.ResolveProjectRoot(currentDir);

	/// <summary>
	/// Fetches all skills from every plugin listed in the marketplace manifest,
	/// plus project-scoped GitHub Copilot skills that live in this repository.
	/// </summary>
	static async Task<List<SkillInfo>> FetchAllSkillsAsync(
		HttpClient http,
		string repo,
		string branch,
		CancellationToken ct = default)
		=> await FetchAllSkillsAsync(http, repo, branch, treeEntries: null, ct);

	/// <summary>
	/// Fetches all skills from every plugin listed in the marketplace manifest,
	/// plus project-scoped GitHub Copilot skills that live in this repository.
	/// </summary>
	static async Task<List<SkillInfo>> FetchAllSkillsAsync(
		HttpClient http,
		string repo,
		string branch,
		List<(string Path, string Type)>? treeEntries = null,
		CancellationToken ct = default)
	{
		var marketplace = await MarketplaceClient.GetMarketplaceAsync(http, repo, branch, ct);
		var allSkills = new List<SkillInfo>();
		treeEntries ??= await MarketplaceClient.FetchTreeEntriesAsync(http, repo, branch, ct);

		if (marketplace is not null && treeEntries is not null)
		{
			foreach (var pluginEntry in marketplace.Plugins)
			{
				var plugin = await MarketplaceClient.GetPluginAsync(http, repo, branch, pluginEntry.Source, ct);
				if (plugin is null)
					continue;

				var skills = await MarketplaceClient.GetSkillsAsync(http, repo, branch, plugin, pluginEntry.Source, treeEntries, ct);
				allSkills.AddRange(skills);
			}
		}

		if (treeEntries is not null)
		{
			var repositorySkills = await MarketplaceClient.GetSkillsFromDirectoryAsync(
				http, repo, branch, RepositorySkillsRoot, RepositorySkillsPluginName, treeEntries, ct);
			allSkills.AddRange(repositorySkills);
		}

		return allSkills
			.GroupBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.ToList();
	}
}

internal sealed record AiDevFlowBootstrapTarget(
	string Scope,
	string Target,
	string? CustomPath,
	string DisplayName,
	string SkillsDirectory);
