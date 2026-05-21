// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// HTTP client for marketplace operations. Fetches manifests, enumerates skills,
/// and downloads skill files from a GitHub-hosted marketplace repository.
/// </summary>
internal static class MarketplaceClient
{
	private const string GitHubApiBase = "https://api.github.com";
	private const string GitHubRawBase = "https://raw.githubusercontent.com";
	private const int MaxResponseBytes = 10 * 1024 * 1024;

	/// <summary>
	/// Fetches and deserializes the marketplace.json manifest from the repository.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/> (caller manages lifetime).</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <returns>The deserialized manifest, or <c>null</c> on failure.</returns>
	public static async Task<MarketplaceManifest?> GetMarketplaceAsync(HttpClient http, string repo, string branch, CancellationToken ct = default)
	{
		var url = BuildRawUrl(repo, branch, ".github/plugin/marketplace.json");
		var json = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (json is null)
			return null;

		return JsonSerializer.Deserialize(json, AiJsonContext.Default.MarketplaceManifest);
	}

	/// <summary>
	/// Fetches and deserializes the plugin.json manifest for a specific plugin.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="pluginSourcePath">Repository-relative path to the plugin directory.</param>
	/// <returns>The deserialized plugin manifest, or <c>null</c> on failure.</returns>
	public static async Task<PluginManifest?> GetPluginAsync(HttpClient http, string repo, string branch, string pluginSourcePath, CancellationToken ct = default)
	{
		var path = NormalizePath($"{pluginSourcePath}/plugin.json");
		var url = BuildRawUrl(repo, branch, path);
		var json = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (json is null)
			return null;

		return JsonSerializer.Deserialize(json, AiJsonContext.Default.PluginManifest);
	}

	/// <summary>
	/// Fetches the full recursive tree for the given branch and returns the entries
	/// as (path, type) pairs. Results can be cached and passed to
	/// <see cref="GetSkillsAsync"/> to avoid redundant API calls.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>List of tree entries, or <c>null</c> on failure.</returns>
	public static async Task<List<(string Path, string Type)>?> FetchTreeEntriesAsync(
		HttpClient http, string repo, string branch, CancellationToken ct = default)
	{
		var encodedRepo = EncodeRepoPath(repo);
		var treeSha = await ResolveTreeShaAsync(http, repo, branch, ct).ConfigureAwait(false);
		if (treeSha is null)
			return null;

		var treeUrl = $"{GitHubApiBase}/repos/{encodedRepo}/git/trees/{Uri.EscapeDataString(treeSha)}?recursive=1";
		var treeJson = await FetchStringAsync(http, treeUrl, ct).ConfigureAwait(false);
		if (treeJson is null)
			return null;

		var treeNode = JsonNode.Parse(treeJson);
		if (treeNode?["truncated"]?.GetValue<bool>() == true)
			throw new InvalidOperationException($"GitHub tree for '{repo}@{branch}' is truncated; cannot safely discover all MAUI AI assets.");

		var treeArray = treeNode?["tree"]?.AsArray();
		if (treeArray is null)
			return null;

		var entries = new List<(string Path, string Type)>();
		foreach (var entry in treeArray)
		{
			var entryPath = entry?["path"]?.GetValue<string>();
			var entryType = entry?["type"]?.GetValue<string>();
			if (entryPath is not null && entryType is not null)
				entries.Add((entryPath, entryType));
		}

		return entries;
	}

	/// <summary>
	/// Discovers all skills within a plugin by enumerating the repository tree
	/// and parsing SKILL.md frontmatter.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="plugin">The plugin manifest whose skills to discover.</param>
	/// <param name="pluginSourcePath">Repository-relative path to the plugin directory.</param>
	/// <param name="cachedTreeEntries">
	/// Optional pre-fetched tree entries from <see cref="FetchTreeEntriesAsync"/>.
	/// When <c>null</c>, the tree is fetched automatically (one API call per invocation).
	/// </param>
	/// <returns>List of discovered skills (empty on failure).</returns>
	public static async Task<List<SkillInfo>> GetSkillsAsync(
		HttpClient http, string repo, string branch, PluginManifest plugin, string pluginSourcePath,
		List<(string Path, string Type)>? cachedTreeEntries = null, CancellationToken ct = default)
	{
		var skills = new List<SkillInfo>();

		var entries = cachedTreeEntries ?? await FetchTreeEntriesAsync(http, repo, branch, ct).ConfigureAwait(false);
		if (entries is null)
			return skills;

		foreach (var skillGlob in plugin.Skills)
		{
			var basePath = NormalizePath($"{pluginSourcePath}/{skillGlob}");
			await AddSkillsFromDirectoryAsync(http, repo, branch, plugin.Name, basePath, entries, skills, ct).ConfigureAwait(false);
		}

		return skills;
	}

	/// <summary>
	/// Discovers skills below a repository directory that contains one subdirectory
	/// per skill, each with a <c>SKILL.md</c> file.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="skillsRootPath">Repository-relative directory containing skill folders.</param>
	/// <param name="pluginName">Logical source name used in output.</param>
	/// <param name="cachedTreeEntries">
	/// Optional pre-fetched tree entries from <see cref="FetchTreeEntriesAsync"/>.
	/// </param>
	/// <returns>List of discovered skills (empty on failure).</returns>
	public static async Task<List<SkillInfo>> GetSkillsFromDirectoryAsync(
		HttpClient http, string repo, string branch, string skillsRootPath, string pluginName,
		List<(string Path, string Type)>? cachedTreeEntries = null, CancellationToken ct = default)
	{
		var skills = new List<SkillInfo>();
		var entries = cachedTreeEntries ?? await FetchTreeEntriesAsync(http, repo, branch, ct).ConfigureAwait(false);
		if (entries is null)
			return skills;

		await AddSkillsFromDirectoryAsync(
			http, repo, branch, pluginName, NormalizePath(skillsRootPath), entries, skills, ct).ConfigureAwait(false);

		return skills;
	}

	/// <summary>
	/// Downloads all files for a skill to the specified destination directory.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="skill">Skill whose files to download.</param>
	/// <param name="destDir">Local directory to write files into.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Count of files successfully downloaded.</returns>
	public static async Task<int> DownloadSkillFilesAsync(
		HttpClient http, SkillInfo skill, string destDir, string repo, string branch, CancellationToken ct = default)
	{
		var count = 0;

		foreach (var filePath in skill.Files)
		{
			string normalizedFilePath;
			string remotePrefix;
			try
			{
				normalizedFilePath = NormalizePath(filePath);
				remotePrefix = NormalizePath(skill.RemotePath) + "/";
			}
			catch (InvalidOperationException)
			{
				continue;
			}

			if (!normalizedFilePath.StartsWith(remotePrefix, StringComparison.Ordinal))
				continue;

			var relativePath = normalizedFilePath[remotePrefix.Length..];

			var destPath = Path.Combine(destDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

			if (!FileSystemPathGuard.IsPathWithinRoot(destPath, destDir))
				continue;

			var content = await FetchRawBytesAsync(http, repo, branch, normalizedFilePath, ct).ConfigureAwait(false);
			if (content is null)
				continue;

			if (!await FileSystemPathGuard.WriteFileAtomicallyWithinRootAsync(
				destPath,
				destDir,
				content,
				ct).ConfigureAwait(false))
			{
				continue;
			}

			count++;
		}

		return count;
	}

	/// <summary>
	/// Resolves the latest commit SHA that touched a specific path on the given branch.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name.</param>
	/// <param name="path">Repository-relative path to query.</param>
	/// <returns>The commit SHA, or <c>null</c> on failure.</returns>
	public static async Task<string?> GetRemoteCommitShaAsync(HttpClient http, string repo, string branch, string path, CancellationToken ct = default)
	{
		var encodedRepo = EncodeRepoPath(repo);
		var normalizedPath = NormalizePath(path);
		var url = $"{GitHubApiBase}/repos/{encodedRepo}/commits?sha={Uri.EscapeDataString(branch)}&path={Uri.EscapeDataString(normalizedPath)}&per_page=1";
		var json = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (json is null)
			return null;

		var array = JsonNode.Parse(json)?.AsArray();
		return array is { Count: > 0 } ? array[0]?["sha"]?.GetValue<string>() : null;
	}

	/// <summary>
	/// Fetches a raw repository text file.
	/// </summary>
	internal static async Task<string?> FetchRawStringAsync(
		HttpClient http, string repo, string branch, string path, CancellationToken ct = default)
	{
		var url = BuildRawUrl(repo, branch, path);
		return await FetchStringAsync(http, url, ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Fetches a raw repository binary file.
	/// </summary>
	internal static async Task<byte[]?> FetchRawBytesAsync(
		HttpClient http, string repo, string branch, string path, CancellationToken ct = default)
	{
		var url = BuildRawUrl(repo, branch, path);
		return await FetchBytesAsync(http, url, ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Resolves the tree SHA for the given branch by fetching the latest commit.
	/// </summary>
	private static async Task<string?> ResolveTreeShaAsync(HttpClient http, string repo, string branch, CancellationToken ct = default)
	{
		var encodedRepo = EncodeRepoPath(repo);
		var url = $"{GitHubApiBase}/repos/{encodedRepo}/commits/{Uri.EscapeDataString(branch)}";
		var json = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (json is null)
			return null;

		var node = JsonNode.Parse(json);
		return node?["commit"]?["tree"]?["sha"]?.GetValue<string>();
	}

	/// <summary>
	/// Downloads and parses the YAML frontmatter from a SKILL.md file.
	/// </summary>
	private static async Task<(string? Name, string? Description)> ParseSkillFrontmatterAsync(
		HttpClient http, string repo, string branch, string skillMdPath, CancellationToken ct = default)
	{
		var content = await FetchRawStringAsync(http, repo, branch, skillMdPath, ct).ConfigureAwait(false);
		if (content is null)
			return (null, null);

		return ParseFrontmatter(content);
	}

	static async Task AddSkillsFromDirectoryAsync(
		HttpClient http,
		string repo,
		string branch,
		string pluginName,
		string basePath,
		List<(string Path, string Type)> entries,
		List<SkillInfo> skills,
		CancellationToken ct)
	{
		var prefix = NormalizePath(basePath) + "/";

		// Find SKILL.md files exactly one level below the base path.
		var skillMdPaths = new List<string>();
		foreach (var (entryPath, entryType) in entries)
		{
			if (entryType != "blob" || !entryPath.StartsWith(prefix, StringComparison.Ordinal))
				continue;

			var relative = entryPath[prefix.Length..];
			var slashIndex = relative.IndexOf('/');
			if (slashIndex > 0 && relative[(slashIndex + 1)..].Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
				skillMdPaths.Add(entryPath);
		}

		foreach (var skillMdPath in skillMdPaths)
		{
			var skillDir = skillMdPath[..skillMdPath.LastIndexOf('/')];
			var skillDirPrefix = skillDir + "/";

			var skillFiles = entries
				.Where(e => e.Type == "blob" && e.Path.StartsWith(skillDirPrefix, StringComparison.Ordinal))
				.Select(e => e.Path)
				.ToList();

			var (name, description) = await ParseSkillFrontmatterAsync(http, repo, branch, skillMdPath, ct).ConfigureAwait(false);
			var dirName = skillDir.Contains('/')
				? skillDir[(skillDir.LastIndexOf('/') + 1)..]
				: skillDir;

			skills.Add(new SkillInfo
			{
				Name = name ?? dirName,
				Description = description,
				PluginName = pluginName,
				RemotePath = skillDir,
				Files = skillFiles
			});
		}
	}

	/// <summary>
	/// Extracts name and description from YAML frontmatter delimited by <c>---</c>.
	/// Uses simple string operations — no YAML library required.
	/// </summary>
	internal static (string? Name, string? Description) ParseFrontmatter(string content)
	{
		string? name = null;
		string? description = null;

		var trimmed = content.TrimStart();
		if (!trimmed.StartsWith("---", StringComparison.Ordinal))
			return (name, description);

		var endIndex = trimmed.IndexOf("---", 3, StringComparison.Ordinal);
		if (endIndex < 0)
			return (name, description);

		var frontmatter = trimmed[3..endIndex];
		var lines = frontmatter.Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			var trimmedLine = lines[i].Trim();
			if (trimmedLine.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
				name = StripYamlValue(trimmedLine["name:".Length..]);
			else if (trimmedLine.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
			{
				var rawValue = StripYamlValue(trimmedLine["description:".Length..]);
				if (rawValue is ">-" or ">" or "|" or "|-" or "|+" or ">+")
				{
					// YAML block scalar — read indented continuation lines
					var sb = new System.Text.StringBuilder();
					while (i + 1 < lines.Length)
					{
						var nextLine = lines[i + 1];
						if (nextLine.Length > 0 && (nextLine[0] == ' ' || nextLine[0] == '\t'))
						{
							if (sb.Length > 0)
								sb.Append(' ');
							sb.Append(nextLine.Trim());
							i++;
						}
						else
						{
							break;
						}
					}
					description = sb.ToString();
				}
				else
				{
					description = rawValue;
				}
			}
		}

		return (name, description);
	}

	/// <summary>
	/// Strips surrounding whitespace and optional quotes from a YAML value.
	/// </summary>
	private static string StripYamlValue(string raw)
	{
		var value = raw.Trim();
		if (value.Length >= 2 &&
			((value[0] == '"' && value[^1] == '"') ||
			 (value[0] == '\'' && value[^1] == '\'')))
		{
			value = value[1..^1];
		}

		return value;
	}

	/// <summary>
	/// Normalizes a repository-relative path by removing <c>./</c> prefixes,
	/// collapsing double slashes, and trimming trailing slashes.
	/// </summary>
	internal static string NormalizePath(string path)
	{
		var normalized = path.Replace('\\', '/');
		while (normalized.Contains("/./"))
			normalized = normalized.Replace("/./", "/");
		while (normalized.StartsWith("./", StringComparison.Ordinal))
			normalized = normalized[2..];
		while (normalized.Contains("//"))
			normalized = normalized.Replace("//", "/");
		normalized = normalized.TrimEnd('/');

		if (normalized.StartsWith("/", StringComparison.Ordinal) ||
			normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
		{
			throw new InvalidOperationException($"Repository path '{path}' must be relative and cannot contain '..' segments.");
		}

		return normalized;
	}

	static string BuildRawUrl(string repo, string branch, string path)
	{
		var encodedRepo = EncodeRepoPath(repo);
		var encodedBranch = string.Join("/", branch.Split('/').Select(Uri.EscapeDataString));
		var normalizedPath = NormalizePath(path);
		var encodedPath = string.Join("/", normalizedPath.Split('/').Select(Uri.EscapeDataString));
		return $"{GitHubRawBase}/{encodedRepo}/{encodedBranch}/{encodedPath}";
	}

	internal static string EncodeRepoPath(string repo)
	{
		var segments = repo.Split('/');
		if (segments.Length != 2 ||
			!IsValidRepoSegment(segments[0]) ||
			!IsValidRepoSegment(segments[1]))
		{
			throw new InvalidOperationException($"GitHub repository '{repo}' must use the format 'owner/repo' and contain only letters, numbers, '.', '_', or '-'.");
		}

		return $"{Uri.EscapeDataString(segments[0])}/{Uri.EscapeDataString(segments[1])}";
	}

	static bool IsValidRepoSegment(string segment)
	{
		if (segment.Length == 0)
			return false;

		foreach (var ch in segment)
		{
			if (!char.IsAsciiLetterOrDigit(ch) && ch is not '.' and not '_' and not '-')
				return false;
		}

		return true;
	}

	private static async Task<string?> FetchStringAsync(HttpClient http, string url, CancellationToken ct = default)
	{
		try
		{
			using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

			// Return null only for 404; other HTTP errors propagate to callers.
			if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
				return null;

			response.EnsureSuccessStatusCode();

			var bytes = await ReadBytesWithLimitAsync(response.Content, ct).ConfigureAwait(false);
			return bytes is null ? null : Encoding.UTF8.GetString(bytes);
		}
		catch (TaskCanceledException) when (!ct.IsCancellationRequested)
		{
			throw new HttpRequestException("GitHub request timed out.", null, System.Net.HttpStatusCode.RequestTimeout);
		}
	}

	private static async Task<byte[]?> FetchBytesAsync(HttpClient http, string url, CancellationToken ct = default)
	{
		try
		{
			using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

			// Return null only for 404; other HTTP errors propagate to callers.
			if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
				return null;

			response.EnsureSuccessStatusCode();

			return await ReadBytesWithLimitAsync(response.Content, ct).ConfigureAwait(false);
		}
		catch (TaskCanceledException) when (!ct.IsCancellationRequested)
		{
			throw new HttpRequestException("GitHub request timed out.", null, System.Net.HttpStatusCode.RequestTimeout);
		}
	}

	static async Task<byte[]?> ReadBytesWithLimitAsync(HttpContent content, CancellationToken ct)
	{
		if (content.Headers.ContentLength > MaxResponseBytes)
			return null;

		await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
		using var buffer = new MemoryStream();
		var readBuffer = new byte[81920];
		while (true)
		{
			var read = await stream.ReadAsync(readBuffer, ct).ConfigureAwait(false);
			if (read == 0)
				break;

			if (buffer.Length + read > MaxResponseBytes)
				return null;

			buffer.Write(readBuffer, 0, read);
		}

		return buffer.ToArray();
	}
}
