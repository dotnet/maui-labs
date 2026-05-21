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
			if (!IsMauiRelatedAgent(assetName, description, content))
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

		var destinationBase = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;

		var count = 0;
		foreach (var filePath in asset.Files)
		{
			var destinationPath = GetAssetFilePath(projectRoot, asset, filePath);
			var fullDestinationPath = Path.GetFullPath(destinationPath);
			if (!fullDestinationPath.StartsWith(destinationBase, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
				continue;

			if (File.Exists(fullDestinationPath) && !force)
				continue;

			var content = await MarketplaceClient.FetchRawBytesAsync(http, repo, branch, filePath, ct).ConfigureAwait(false);
			if (content is null)
				continue;

			if (!FileSystemPathGuard.IsPathWithinRoot(Path.GetDirectoryName(fullDestinationPath) ?? destinationRoot, projectRoot))
				return (-1, string.Empty);

			await File.WriteAllBytesAsync(fullDestinationPath, content, ct).ConfigureAwait(false);
			count++;
		}

		return (count, destinationRoot);
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

		foreach (var filePath in Directory.GetFiles(destinationRoot, "*.agent.md", SearchOption.TopDirectoryOnly)
			.OrderBy(path => path, StringComparer.Ordinal))
		{
			var content = File.ReadAllText(filePath);
			var (name, description) = MarketplaceClient.ParseFrontmatter(content);
			var fileName = Path.GetFileName(filePath);
			var assetName = name ?? fileName[..^".agent.md".Length];
			if (!IsMauiRelatedAgent(assetName, description, content))
				continue;

			assets.Add(new RepositoryAssetInfo
			{
				Name = assetName,
				Category = "agent",
				Description = description,
				RemotePath = string.Empty,
				DestinationRoot = CopilotAgentsDestinationRoot,
				Files = [Path.Combine(destinationRoot, fileName)]
			});
		}

		return assets;
	}

	internal static string GetAssetFilePath(string projectRoot, RepositoryAssetInfo asset, string filePath)
	{
		var destinationRoot = GetDestinationRoot(projectRoot, asset.DestinationRoot);
		return Path.Combine(destinationRoot, GetRemoteFileName(filePath));
	}

	static string GetDestinationRoot(string projectRoot, string destinationRoot)
		=> Path.Combine(
			projectRoot,
			MarketplaceClient.NormalizePath(destinationRoot).Replace('/', Path.DirectorySeparatorChar));

	static bool IsMauiRelatedAgent(string name, string? description, string content)
	{
		var haystack = string.Join('\n', name, description, content);
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
