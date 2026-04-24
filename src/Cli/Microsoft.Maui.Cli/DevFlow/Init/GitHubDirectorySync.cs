using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow.Init;

internal sealed class GitHubSyncRequest
{
    public required string Repo { get; init; }
    public required string RepoUrl { get; init; }
    public required string SourcePath { get; init; }
    public required string Ref { get; init; }
    public required string DestinationRoot { get; init; }
    public required string MetadataFileName { get; init; }
    public string? ManifestVersion { get; init; }
    public bool DryRun { get; init; }
}

internal sealed class GitHubSyncResult
{
    public string CommitSha { get; init; } = "";
    public string MetadataPath { get; init; } = "";
    public IReadOnlyList<string> DownloadedFiles { get; init; } = [];
}

internal sealed class GitHubSyncMetadata
{
    public string Commit { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Repo { get; set; } = "";
    public string RepoUrl { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string ManifestVersion { get; set; } = "";
}

internal static class GitHubDirectorySync
{
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Microsoft.Maui.DevFlow-CLI", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public static async Task<IReadOnlyList<string>> ListFilesAsync(HttpClient http, string repo, string basePath, string gitRef, CancellationToken cancellationToken = default)
    {
        var files = new List<string>();
        await ListGitHubDirectoryAsync(http, repo, basePath, "", files, gitRef, cancellationToken);
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static async Task<string?> GetLatestCommitShaAsync(HttpClient http, string repo, string basePath, string gitRef, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{repo}/commits?path={basePath}&sha={gitRef}&per_page=1";
        var json = await http.GetStringAsync(url, cancellationToken);
        var commits = CliJson.ParseElement(json);
        foreach (var commit in commits.EnumerateArray())
            return commit.GetProperty("sha").GetString();

        return null;
    }

    public static async Task<GitHubSyncMetadata?> ReadMetadataAsync(string metadataPath)
    {
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            var doc = CliJson.ParseElement(json);

            return new GitHubSyncMetadata
            {
                Commit = doc.TryGetProperty("commit", out var commit) ? commit.GetString() ?? "" : "",
                UpdatedAt = doc.TryGetProperty("updatedAt", out var updatedAt) ? updatedAt.GetString() ?? "" : "",
                Branch = doc.TryGetProperty("branch", out var branch) ? branch.GetString() ?? "" : "",
                Repo = doc.TryGetProperty("repo", out var repo) ? repo.GetString() ?? "" : "",
                RepoUrl = doc.TryGetProperty("repoUrl", out var repoUrl) ? repoUrl.GetString() ?? "" : "",
                SourcePath = doc.TryGetProperty("sourcePath", out var sourcePath) ? sourcePath.GetString() ?? "" : "",
                ManifestVersion = doc.TryGetProperty("manifestVersion", out var manifestVersion) ? manifestVersion.GetString() ?? "" : ""
            };
        }
        catch
        {
            return null;
        }
    }

    public static async Task<GitHubSyncResult> SyncAsync(HttpClient http, GitHubSyncRequest request, CancellationToken cancellationToken = default)
    {
        var files = await ListFilesAsync(http, request.Repo, request.SourcePath, request.Ref, cancellationToken);
        if (files.Count == 0)
            throw new InvalidOperationException($"No files found at {request.Repo}/{request.SourcePath}@{request.Ref}.");

        var downloaded = new List<string>();
        foreach (var file in files)
        {
            var url = $"https://raw.githubusercontent.com/{request.Repo}/{request.Ref}/{request.SourcePath}/{file}";
            var destPath = Path.GetFullPath(Path.Combine(request.DestinationRoot, file));
            var rootFull = Path.GetFullPath(request.DestinationRoot + Path.DirectorySeparatorChar);
            if (!destPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to write outside destination root: {file}");
            downloaded.Add(destPath);

            if (request.DryRun)
                continue;

            var content = await http.GetStringAsync(url, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await File.WriteAllTextAsync(destPath, content, cancellationToken);
        }

        var commitSha = await GetLatestCommitShaAsync(http, request.Repo, request.SourcePath, request.Ref, cancellationToken) ?? request.Ref;
        var metadataPath = Path.Combine(request.DestinationRoot, request.MetadataFileName);
        if (!request.DryRun)
        {
            Directory.CreateDirectory(request.DestinationRoot);
            var metadata = new JsonObject
            {
                ["commit"] = commitSha,
                ["updatedAt"] = DateTime.UtcNow.ToString("o"),
                ["branch"] = request.Ref,
                ["repo"] = request.Repo,
                ["repoUrl"] = request.RepoUrl,
                ["sourcePath"] = request.SourcePath,
                ["manifestVersion"] = request.ManifestVersion ?? string.Empty
            };
            await File.WriteAllTextAsync(metadataPath, CliJson.SerializeUntyped(metadata, indented: true), cancellationToken);
        }

        return new GitHubSyncResult
        {
            CommitSha = commitSha,
            MetadataPath = metadataPath,
            DownloadedFiles = downloaded
        };
    }

    static async Task ListGitHubDirectoryAsync(HttpClient http, string repo, string basePath, string relativePath, List<string> files, string gitRef, CancellationToken cancellationToken = default)
    {
        var apiPath = string.IsNullOrEmpty(relativePath) ? basePath : $"{basePath}/{relativePath}";
        var url = $"https://api.github.com/repos/{repo}/contents/{apiPath}?ref={gitRef}";
        var json = await http.GetStringAsync(url, cancellationToken);
        var items = CliJson.ParseElement(json);

        foreach (var item in items.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var type = item.GetProperty("type").GetString()!;
            var itemRelative = string.IsNullOrEmpty(relativePath) ? name : $"{relativePath}/{name}";

            if (type == "file")
                files.Add(itemRelative);
            else if (type == "dir")
                await ListGitHubDirectoryAsync(http, repo, basePath, itemRelative, files, gitRef, cancellationToken);
        }
    }
}
