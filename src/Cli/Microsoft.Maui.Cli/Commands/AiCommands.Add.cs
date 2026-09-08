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
	/// Creates the <c>maui ai add &lt;skill&gt;</c> command that installs a specific skill by name.
	/// </summary>
	static Command CreateAddCommand()
	{
		var skillArg = new Argument<string>("skill") { Description = "Name of the skill to add" };

		var envOption = new Option<string[]>("--env")
		{
			Description = "Target detected environments: Claude, VsCode, CopilotCli, OpenCode (repeatable)",
			Arity = ArgumentArity.OneOrMore,
			AllowMultipleArgumentsPerToken = true
		};

		var command = new Command("add", "Install one named skill in detected project environments and merge MCP config (Copilot CLI MCP is user-wide). DevFlow skills come from the CLI bundle. Use list to discover names; --force replaces local edits.")
		{
			skillArg,
			CreateRepoOption(),
			CreateBranchOption(),
			CreateForceOption(),
			new Option<bool>("--no-mcp") { Description = "Skip MCP server configuration" },
			envOption
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var isCi = parseResult.GetValue(GlobalOptions.CiOption);
			var dryRun = parseResult.GetValue(GlobalOptions.DryRunOption);
			var skillName = parseResult.GetValue(skillArg) ?? string.Empty;
			var repo = parseResult.GetOption<string>("repo") ?? DefaultRepo;
			var branch = parseResult.GetOption<string>("branch") ?? DefaultBranch;
			var force = parseResult.GetOption<bool>("force");
			var noMcp = parseResult.GetOption<bool>("no-mcp");
			var envFilter = parseResult.GetOption<string[]>("env");

			if (string.IsNullOrWhiteSpace(skillName))
			{
				formatter.WriteError(new Exception("Skill name is required. Usage: maui ai add <skill>"));
				return 1;
			}

			try
			{
				using var http = CreateGitHubHttpClient();

				var bundledSkill = IsDevFlowManagedSkillName(skillName);
				if (bundledSkill)
					skillName = GetBundledSkillId(skillName);

				// Bundled skills do not need a marketplace lookup.
				List<SkillInfo> allSkills;
				if (bundledSkill)
				{
					allSkills = [new SkillInfo { Name = skillName }];
				}
				else if (!useJson && formatter is SpectreOutputFormatter spectre)
				{
					allSkills = await spectre.StatusAsync("Fetching marketplace...", async () =>
						await FetchAllSkillsAsync(http, repo, branch, ct));
				}
				else
				{
					allSkills = await FetchAllSkillsAsync(http, repo, branch, ct);
				}

				var skill = allSkills.FirstOrDefault(s =>
					string.Equals(s.Name, skillName, StringComparison.OrdinalIgnoreCase));

				if (skill is null)
				{
					formatter.WriteError(new Exception(
						$"Skill '{skillName}' not found in the marketplace. Run 'maui ai list' to see available skills."));
					return 1;
				}

				// Detect environments
				var currentDir = Directory.GetCurrentDirectory();
				var workingDir = GetAiCommandWorkingDirectory(currentDir);
				var environments = AgentEnvironmentDetector.Detect(currentDir);

				environments = FilterEnvironments(environments, envFilter);

				if (environments.Count == 0)
				{
					formatter.WriteWarning("No agent environments detected. Run 'maui ai init' to set up environments first.");
					return 1;
				}

				if (dryRun)
				{
					formatter.WriteInfo($"[Dry run] Would install skill '{skill.Name}' to:");
					formatter.WriteTable(
						GetUniqueSkillInstallEnvironments(environments),
						("Environment", e => e.Kind.ToString()),
						("Path", e => Path.Combine(e.SkillsDirectory, skill.Name)));
					if (!noMcp)
						formatter.WriteTable(environments, ("MCP environment", e => e.Kind.ToString()), ("Config path", e => e.McpConfigPath));
					return 0;
				}

				// Confirm unless --force, --ci, or --json
				if (!force && !isCi && !useJson)
				{
					var sourceDescription = bundledSkill ? "CLI bundle" : $"{skill.Files.Count} files";
					formatter.WriteInfo($"Will install '{skill.Name}' ({sourceDescription}) to {environments.Count} {(environments.Count == 1 ? "environment" : "environments")}.");
					if (!AnsiConsole.Confirm("Proceed?", defaultValue: true))
					{
						formatter.WriteInfo("Installation cancelled.");
						return 0;
					}
				}

				// Install
				var results = new List<(string Env, int Files, string Path)>();
				var devFlowResults = new List<(string Skill, string Target, string Action, string Path)>();
				var devFlowFailed = false;

				if (bundledSkill)
				{
					foreach (var target in GetDevFlowBootstrapTargets(environments))
					{
						var result = await DevFlowSkillManager.InstallSkillAsync(
							skill.Name, target.Scope, target.Target, target.CustomPath, force, ct);
						var rows = GetDevFlowResultRows(result, target).ToList();
						devFlowFailed |= rows.Count == 0;
						devFlowResults.AddRange(rows);
						foreach (var row in rows)
							formatter.WriteInfo($"DevFlow {row.Action} {row.Skill} → {row.Target}");
						if (isCi && devFlowFailed)
							break;
					}
				}

				foreach (var env in bundledSkill ? [] : GetUniqueSkillInstallEnvironments(environments))
				{
					var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
						http, skill, env, workingDir, repo, branch, force, ct);

					results.Add((env.Kind.ToString(), filesInstalled, installPath));

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
						formatter.WriteInfo($"Skipped {skill.Name} → {env.Kind} (already installed, use --force to overwrite)");
					if (isCi && filesInstalled < 0)
						break;
				}

				// Configure MCP
				var mcpResults = new List<(string Environment, bool Configured, string? BackupPath)>();
				if (!noMcp && (!isCi || !HasInitInstallFailures(results.Select(r => r.Files), [], devFlowFailed)))
				{
					foreach (var env in environments)
					{
						var result = await McpConfigurator.ConfigureWithResultAsync(env, workingDir, ct);
						mcpResults.Add((env.Kind.ToString(), result.Success, result.BackupPath));
						WriteMcpConfigurationMessage(formatter, env.Kind, result, useJson);
						if (isCi && !result.Success)
							break;
					}
				}

				var hasFailures = HasInitInstallFailures(
					results.Select(r => r.Files), [], devFlowFailed, mcpResults.Any(r => !r.Configured));
				if (useJson)
				{
					var jsonResult = new JsonObject
					{
						["status"] = GetInitStatus(hasFailures),
						["skill"] = skill.Name,
						["devFlowSkills"] = new JsonArray(devFlowResults.Select(r => (JsonNode)new JsonObject
						{
							["skill"] = r.Skill,
							["target"] = r.Target,
							["action"] = r.Action,
							["path"] = r.Path
						}).ToArray()),
						["installations"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject
						{
							["environment"] = r.Env,
							["files"] = r.Files,
							["path"] = r.Path
						}).ToArray()),
						["mcp"] = new JsonArray(mcpResults.Select(r => (JsonNode)new JsonObject
						{
							["environment"] = r.Environment,
							["configured"] = r.Configured,
							["backupPath"] = r.BackupPath
						}).ToArray())
					};
					formatter.Write(jsonResult);
				}

				return hasFailures ? 1 : 0;
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
}
