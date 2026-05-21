// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// Discovers and installs non-skill AI development assets from this repository.
/// </summary>
internal static class RepositoryAssetInstaller
{
	internal const string CopilotAgentsRoot = ".github/agents";
	internal const string CopilotAgentsDestinationRoot = ".github/agents";

	/// <summary>
	/// Discovers MAUI-related Copilot agent definitions from <c>.github/agents</c>.
	/// </summary>
	public static async Task<List<RepositoryAssetInfo>> GetCopilotAgentsAsync(
		HttpClient http,
		string repo,
		string branch,
		List<(string Path, string Type)>? cachedTreeEntries = null,
		CancellationToken ct = default)
	{
		var assets = new List<RepositoryAssetInfo>();
		var entries = cachedTreeEntries ?? await MarketplaceClient.FetchTreeEntriesAsync(http, repo, branch, ct).ConfigureAwait(false);
		if (entries is null)
			return assets;

		var prefix = MarketplaceClient.NormalizePath(CopilotAgentsRoot) + "/";
		foreach (var (entryPath, entryType) in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
		{
			if (entryType != "blob" ||
				!entryPath.StartsWith(prefix, StringComparison.Ordinal) ||
				!entryPath.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var content = await MarketplaceClient.FetchRawStringAsync(http, repo, branch, entryPath, ct).ConfigureAwait(false);
			if (content is null)
				continue;

			var (name, description) = MarketplaceClient.ParseFrontmatter(content);
			var assetName = name ?? GetRemoteFileName(entryPath)[..^".agent.md".Length];
			if (!IsMauiRelatedAgent(assetName, description))
				continue;

			assets.Add(new RepositoryAssetInfo
			{
				Name = assetName,
				Category = "agent",
				Description = description,
				RemotePath = entryPath,
				DestinationRoot = CopilotAgentsDestinationRoot,
				Files = [entryPath]
			});
		}

		return assets;
	}

	/// <summary>
	/// Installs an asset into the target project.
	/// </summary>
	public static async Task<(int FilesInstalled, string InstallPath)> InstallAssetAsync(
		HttpClient http,
		RepositoryAssetInfo asset,
		string projectRoot,
		string repo,
		string branch,
		bool force,
		CancellationToken ct = default)
	{
		var destinationRoot = GetDestinationRoot(projectRoot, asset.DestinationRoot);
		if (!FileSystemPathGuard.IsPathWithinRoot(destinationRoot, projectRoot))
			return (-1, string.Empty);

		Directory.CreateDirectory(destinationRoot);
		if (!FileSystemPathGuard.IsPathWithinRoot(destinationRoot, projectRoot))
			return (-1, string.Empty);

		var count = 0;
		var downloadFailures = 0;
		foreach (var filePath in asset.Files)
		{
			var content = await MarketplaceClient.FetchRawBytesAsync(http, repo, branch, filePath, ct).ConfigureAwait(false);
			if (content is null)
			{
				downloadFailures++;
				continue;
			}

			var destinationPath = GetAssetFilePath(projectRoot, asset, filePath);
			var fullDestinationPath = Path.GetFullPath(destinationPath);
			if (FileSystemPathGuard.IsReparsePoint(fullDestinationPath))
				return (-1, string.Empty);

			if (!FileSystemPathGuard.IsPathWithinRoot(fullDestinationPath, destinationRoot))
				continue;

			if (File.Exists(fullDestinationPath))
			{
				if (!force)
					continue;
			}

			if (!await FileSystemPathGuard.WriteFileAtomicallyWithinRootAsync(
				fullDestinationPath,
				projectRoot,
				content,
				ct).ConfigureAwait(false))
			{
				return (-1, string.Empty);
			}

			count++;
		}

		return downloadFailures > 0 ? (-2, destinationRoot) : (count, destinationRoot);
	}

	/// <summary>
	/// Discovers MAUI-related Copilot agent definitions already present in the target project.
	/// </summary>
	public static List<RepositoryAssetInfo> GetInstalledCopilotAgents(string projectRoot)
	{
		var destinationRoot = GetDestinationRoot(projectRoot, CopilotAgentsDestinationRoot);
		var assets = new List<RepositoryAssetInfo>();
		if (!Directory.Exists(destinationRoot))
			return assets;

		foreach (var filePath in Directory.GetFiles(destinationRoot, "*.agent.md", SearchOption.AllDirectories)
			.OrderBy(path => path, StringComparer.Ordinal))
		{
			var content = File.ReadAllText(filePath);
			var (name, description) = MarketplaceClient.ParseFrontmatter(content);
			var fileName = Path.GetFileName(filePath);
			var assetName = name ?? fileName[..^".agent.md".Length];
			if (!IsMauiRelatedAgent(assetName, description))
				continue;

			assets.Add(new RepositoryAssetInfo
			{
				Name = assetName,
				Category = "agent",
				Description = description,
				RemotePath = string.Empty,
				DestinationRoot = CopilotAgentsDestinationRoot,
				Files = [filePath]
			});
		}

		return assets;
	}

	internal static string GetAssetFilePath(string projectRoot, RepositoryAssetInfo asset, string filePath)
	{
		var destinationRoot = GetDestinationRoot(projectRoot, asset.DestinationRoot);
		return Path.Combine(
			destinationRoot,
			GetAssetRelativePath(asset, filePath).Replace('/', Path.DirectorySeparatorChar));
	}

	internal static string GetAssetRelativePath(RepositoryAssetInfo asset, string filePath)
	{
		var normalizedFilePath = MarketplaceClient.NormalizePath(filePath);
		var destinationPrefix = MarketplaceClient.NormalizePath(asset.DestinationRoot) + "/";

		return normalizedFilePath.StartsWith(destinationPrefix, StringComparison.Ordinal)
			? normalizedFilePath[destinationPrefix.Length..]
			: GetRemoteFileName(normalizedFilePath);
	}

	static string GetDestinationRoot(string projectRoot, string destinationRoot)
		=> Path.Combine(
			projectRoot,
			MarketplaceClient.NormalizePath(destinationRoot).Replace('/', Path.DirectorySeparatorChar));

	static bool IsMauiRelatedAgent(string name, string? description)
	{
		var haystack = string.Join('\n', name, description);
		return haystack.Contains("maui", StringComparison.OrdinalIgnoreCase) ||
			haystack.Contains("comet", StringComparison.OrdinalIgnoreCase);
	}

	internal static string GetRemoteFileName(string path)
	{
		var normalized = MarketplaceClient.NormalizePath(path);
		var slashIndex = normalized.LastIndexOf('/');
		return slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
	}

}
