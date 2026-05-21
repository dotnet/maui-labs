// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.DevFlow.Skills;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AiCommands
{
	internal sealed record AiAssetStatusRow(
		string Item,
		string Type,
		string Target,
		string Installed,
		string Status,
		string Path);

	static async Task<List<AiAssetStatusRow>> GetDevFlowStatusRowsAsync(
		IEnumerable<AiDevFlowBootstrapTarget> targets,
		CancellationToken ct)
	{
		var rows = new List<AiAssetStatusRow>();
		foreach (var target in targets)
		{
			var result = await DevFlowSkillManager.CheckAsync(
				target.Scope,
				target.Target,
				target.CustomPath,
				online: false,
				ct);

			rows.AddRange(GetDevFlowStatusRows(result, target));
		}

		return rows;
	}

	internal static IEnumerable<AiAssetStatusRow> GetDevFlowStatusRows(JsonObject result, AiDevFlowBootstrapTarget target)
	{
		if (result["skills"] is not JsonArray skills)
			yield break;

		foreach (var item in skills.OfType<JsonObject>())
		{
			var skillId = GetJsonString(item, "skillId") ?? "unknown";
			yield return new AiAssetStatusRow(
				skillId,
				"DevFlow",
				target.DisplayName,
				GetJsonString(item, "installedVersion") ?? "",
				GetJsonString(item, "status") ?? "unknown",
				GetJsonString(item, "path") ?? target.SkillsDirectory);
		}
	}

	static async Task<List<AiAssetStatusRow>> GetMarketplaceSkillStatusRowsAsync(
		IEnumerable<DetectedEnvironment> environments,
		bool checkUpdates,
		HttpClient? http,
		string repo,
		string branch,
		CancellationToken ct)
	{
		var rows = new List<AiAssetStatusRow>();
		foreach (var env in GetUniqueSkillInstallEnvironments(environments))
		{
			if (!Directory.Exists(env.SkillsDirectory))
				continue;

			foreach (var skillDir in Directory.GetDirectories(env.SkillsDirectory).OrderBy(path => path, StringComparer.Ordinal))
			{
				var skillName = Path.GetFileName(skillDir);
				if (IsDevFlowManagedSkillName(skillName))
					continue;

				var version = await SkillVersionStore.ReadAsync(skillDir, ct).ConfigureAwait(false);
				if (version is null)
				{
					rows.Add(new AiAssetStatusRow(skillName, "Skill", env.Kind.ToString(), "Unknown", "Unknown", skillDir));
					continue;
				}

				var installed = FormatInstalledTimestamp(version.UpdatedAt);
				var status = "Installed";

				if (checkUpdates && http is not null && version.PluginPath is not null)
				{
					var remoteSha = await MarketplaceClient.GetRemoteCommitShaAsync(
						http, repo, branch, version.PluginPath, ct).ConfigureAwait(false);

					status = remoteSha is not null && version.Commit is not null
						? string.Equals(remoteSha, version.Commit, StringComparison.OrdinalIgnoreCase)
							? "Up to date"
							: "Update available"
						: "Unknown";
				}

				rows.Add(new AiAssetStatusRow(skillName, "Skill", env.Kind.ToString(), installed, status, skillDir));
			}
		}

		return rows;
	}

	internal static List<AiAssetStatusRow> GetInstalledAgentStatusRows(string workingDir)
		=> RepositoryAssetInstaller.GetInstalledCopilotAgents(workingDir)
			.Select(asset => new AiAssetStatusRow(
				asset.Name,
				asset.Category,
				"GitHub Copilot",
				"Yes",
				"Installed",
				Path.Combine(workingDir, asset.DestinationRoot)))
			.ToList();

	internal static async Task<List<(RepositoryAssetInfo Asset, AiAssetStatusRow Row)>> GetRemoteAgentStatusRowsAsync(
		HttpClient http,
		IEnumerable<RepositoryAssetInfo> assets,
		string workingDir,
		string repo,
		string branch,
		CancellationToken ct)
	{
		var rows = new List<(RepositoryAssetInfo Asset, AiAssetStatusRow Row)>();
		foreach (var asset in assets)
		{
			var status = "Up to date";
			foreach (var filePath in asset.Files)
			{
				var localPath = RepositoryAssetInstaller.GetAssetFilePath(workingDir, asset, filePath);
				if (!File.Exists(localPath))
				{
					status = "Missing";
					break;
				}

				var remoteBytes = await MarketplaceClient.FetchRawBytesAsync(http, repo, branch, filePath, ct).ConfigureAwait(false);
				if (remoteBytes is null)
				{
					status = "Unknown";
					break;
				}

				var localBytes = await File.ReadAllBytesAsync(localPath, ct).ConfigureAwait(false);
				if (!remoteBytes.SequenceEqual(localBytes))
				{
					status = "Update available";
					break;
				}
			}

			rows.Add((asset, new AiAssetStatusRow(
				asset.Name,
				asset.Category,
				"GitHub Copilot",
				status == "Missing" ? "No" : "Yes",
				status,
				Path.Combine(workingDir, asset.DestinationRoot))));
		}

		return rows;
	}

	internal static bool NeedsUpdate(AiAssetStatusRow row, bool force)
		=> force || row.Status is "Missing" or "missing" or "Update available" or "update-available-from-current-cli" or "installed-from-different-cli-same-version" or "installed-from-newer-cli" or "dirty" or "unknown-or-unmanaged";

	static string FormatInstalledTimestamp(string? timestamp)
	{
		if (timestamp is null)
			return "Unknown";

		return DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
			? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
			: timestamp;
	}
}
