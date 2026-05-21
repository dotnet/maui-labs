// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AiCommands
{
	/// <summary>
	/// Creates the <c>maui ai status</c> command that shows installed skill status and checks for updates.
	/// </summary>
	static Command CreateStatusCommand()
	{
		var command = new Command("status", "Show status of installed AI development assets")
		{
			CreateRepoOption(),
			CreateBranchOption(),
			new Option<bool>("--check-updates") { Description = "Check remote repository for available updates" }
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var repo = parseResult.GetOption<string>("repo") ?? DefaultRepo;
			var branch = parseResult.GetOption<string>("branch") ?? DefaultBranch;
			var checkUpdates = parseResult.GetOption<bool>("check-updates");

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

				using var http = checkUpdates ? CreateGitHubHttpClient() : null;

				var rows = new List<AiAssetStatusRow>();
				rows.AddRange(await GetDevFlowStatusRowsAsync(GetDevFlowBootstrapTargets(environments), ct));
				rows.AddRange(await GetMarketplaceSkillStatusRowsAsync(environments, checkUpdates, http, repo, branch, ct));

				if (checkUpdates && http is not null)
				{
					var treeEntries = await MarketplaceClient.FetchTreeEntriesAsync(http, repo, branch, ct);
					var agentAssets = await RepositoryAssetInstaller.GetCopilotAgentsAsync(http, repo, branch, treeEntries, ct);
					var agentRows = await GetRemoteAgentStatusRowsAsync(http, agentAssets, workingDir, repo, branch, ct);
					rows.AddRange(agentRows.Select(row => row.Row));
					rows.AddRange(GetLocalOnlyAgentStatusRows(agentAssets, GetInstalledAgentStatusRows(workingDir)));
				}
				else
				{
					rows.AddRange(GetInstalledAgentStatusRows(workingDir));
				}

				if (rows.Count == 0)
				{
					formatter.WriteInfo("No AI development assets found. Run 'maui ai init' to get started.");
					return 0;
				}

				if (useJson)
				{
					var jsonArray = new JsonArray(rows.Select(r => (JsonNode)new JsonObject
					{
						["item"] = r.Item,
						["type"] = r.Type,
						["target"] = r.Target,
						["installed"] = r.Installed,
						["status"] = r.Status,
						["path"] = r.Path
					}).ToArray());
					formatter.Write(jsonArray);
				}
				else
				{
					formatter.WriteTable(
						rows,
						("Item", r => r.Item),
						("Type", r => r.Type),
						("Target", r => r.Target),
						("Installed", r => r.Installed),
						("Status", r => r.Status),
						("Path", r => r.Path));
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
