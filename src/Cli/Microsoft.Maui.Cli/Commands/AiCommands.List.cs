// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AiCommands
{
	/// <summary>
	/// Creates the <c>maui ai list</c> command that shows available marketplace skills.
	/// </summary>
	static Command CreateListCommand()
	{
		var command = new Command("list", "List available AI agent skills from the marketplace")
		{
			CreateRepoOption(),
			CreateBranchOption()
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var repo = parseResult.GetOption<string>("repo") ?? DefaultRepo;
			var branch = parseResult.GetOption<string>("branch") ?? DefaultBranch;

			try
			{
				using var http = CreateGitHubHttpClient();

				List<SkillInfo> allSkills;
				if (!useJson && formatter is SpectreOutputFormatter spectre)
				{
					allSkills = await spectre.StatusAsync("Fetching marketplace...", async () =>
						await FetchAllSkillsAsync(http, repo, branch, ct));
				}
				else
				{
					allSkills = await FetchAllSkillsAsync(http, repo, branch, ct);
				}

				if (allSkills.Count == 0)
				{
					formatter.WriteWarning("No skills found in the marketplace.");
					return 1;
				}

				// Check installed status per skill
				var workingDir = Directory.GetCurrentDirectory();
				var environments = AgentEnvironmentDetector.Detect(workingDir);
				var installedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var env in environments)
				{
					if (!Directory.Exists(env.SkillsDirectory))
						continue;

					foreach (var skillDir in Directory.GetDirectories(env.SkillsDirectory))
					{
						var version = await SkillVersionStore.ReadAsync(skillDir, ct);
						if (version is not null)
							installedSkills.Add(Path.GetFileName(skillDir));
					}
				}

				if (useJson)
				{
					var jsonArray = new JsonArray(allSkills.Select(s => (JsonNode)new JsonObject
					{
						["skill"] = s.Name,
						["plugin"] = s.PluginName,
						["description"] = s.Description ?? "",
						["files"] = s.Files.Count,
						["installed"] = installedSkills.Contains(s.Name)
					}).ToArray());
					formatter.Write(jsonArray);
				}
				else
				{
					formatter.WriteTable(
						allSkills,
						("Skill", s => s.Name),
						("Plugin", s => s.PluginName),
						("Description", s => s.Description ?? ""),
						("Installed", s => installedSkills.Contains(s.Name) ? "Yes" : ""));
				}

				return 0;
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
