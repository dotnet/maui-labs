// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Microsoft.Maui.Cli.Services;

public sealed record MauiPrBuildArtifact
{
	public int PullRequest { get; init; }
	public int BuildId { get; init; }
	public required string BuildNumber { get; init; }
	public required string Status { get; init; }
	public string? Result { get; init; }
	public required string SourceVersion { get; init; }
	public required string Organization { get; init; }
	public required string Project { get; init; }
	public required string ArtifactName { get; init; }
	public required string DownloadUrl { get; init; }
	public required string BuildUrl { get; init; }
}

public sealed record MauiPrArtifactDownload
{
	public required MauiPrBuildArtifact Build { get; init; }
	public required string HiveRoot { get; init; }
	public required string ArtifactPath { get; init; }
	public required string PackageSourcePath { get; init; }
	public required string MetadataPath { get; init; }
	public required string Version { get; init; }
}

public sealed record MauiPrArtifactHivePaths
{
	public required string HiveRoot { get; init; }
	public required string HivePath { get; init; }
	public required string ExtractPath { get; init; }
	public required string PackageSourcePath { get; init; }
	public required string ZipPath { get; init; }
	public required string MetadataPath { get; init; }
}

public sealed record MauiPrArtifactMetadata
{
	public int SchemaVersion { get; init; } = 1;
	public DateTimeOffset CreatedAtUtc { get; init; }
	public required string Repository { get; init; }
	public int PullRequest { get; init; }
	public int BuildId { get; init; }
	public required string BuildNumber { get; init; }
	public required string BuildUrl { get; init; }
	public required string Status { get; init; }
	public string? Result { get; init; }
	public required string SourceVersion { get; init; }
	public required string Organization { get; init; }
	public required string Project { get; init; }
	public required string ArtifactName { get; init; }
	public required string PackageVersion { get; init; }
	public required string HiveRoot { get; init; }
	public required string HivePath { get; init; }
	public required string PackageSourcePath { get; init; }
	public required string ZipPath { get; init; }
}

public interface IMauiPrArtifactService
{
	Task<MauiPrBuildArtifact> FindPackageArtifactAsync(int prNumber, CancellationToken cancellationToken = default);
	MauiPrArtifactHivePaths GetHivePaths(MauiPrBuildArtifact artifact, string? hiveRoot = null);
	Task<MauiPrArtifactDownload> DownloadPackageArtifactAsync(
		MauiPrBuildArtifact artifact,
		string? hiveRoot = null,
		CancellationToken cancellationToken = default);
}

public sealed class MauiPrArtifactService : IMauiPrArtifactService
{
	internal const string DotNetMauiOwner = "dotnet";
	internal const string DotNetMauiRepo = "maui";
	internal const string DefaultAzureDevOpsOrganization = "dnceng-public";
	internal const string DefaultAzureDevOpsProject = "public";
	internal const string MauiPrDefinitionName = "maui-pr";
	internal const string PackageArtifactName = "PackageArtifacts";
	internal const string VersionPackageId = "Microsoft.Maui.Controls";
	internal const long MaxArtifactZipBytes = 2_500_000_000;
	internal const long MaxExtractedArtifactBytes = 10_000_000_000;
	internal const long MaxPackageBytes = 1_500_000_000;
	internal const int MaxExtractedArtifactEntries = 100_000;

	static readonly Regex s_buildUrlRegex = new(
		@"dev\.azure\.com/([^/]+)/([^/]+)/_build/results\?buildId=(\d+)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	readonly HttpClient _httpClient;

	public MauiPrArtifactService(HttpClient httpClient)
	{
		_httpClient = httpClient;
	}

	public async Task<MauiPrBuildArtifact> FindPackageArtifactAsync(int prNumber, CancellationToken cancellationToken = default)
	{
		if (prNumber <= 0)
			throw new ArgumentOutOfRangeException(nameof(prNumber), "PR number must be positive.");

		var directBuild = await FindBuildFromAzureDevOpsAsync(prNumber, cancellationToken);
		if (directBuild is not null)
			return directBuild;

		var checkBuild = await FindBuildFromGitHubChecksAsync(prNumber, cancellationToken);
		if (checkBuild is not null)
			return checkBuild;

		throw new InvalidOperationException(
			$"No completed dotnet/maui PR build with {PackageArtifactName} was found for PR #{prNumber}. " +
			$"Check https://github.com/dotnet/maui/pull/{prNumber} to confirm the PR build has completed.");
	}

	public async Task<MauiPrArtifactDownload> DownloadPackageArtifactAsync(
		MauiPrBuildArtifact artifact,
		string? hiveRoot = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(artifact);

		var finalPaths = GetHivePaths(artifact, hiveRoot);
		var stagingPaths = CreateStagingPaths(finalPaths);
		await using var hiveLock = await AcquireHiveLockAsync(finalPaths.HivePath, cancellationToken);

		if (Directory.Exists(stagingPaths.HivePath))
			Directory.Delete(stagingPaths.HivePath, recursive: true);
		Directory.CreateDirectory(stagingPaths.HivePath);

		try
		{
			using (var response = await SendAsync(artifact.DownloadUrl, cancellationToken, HttpCompletionOption.ResponseHeadersRead))
			{
				if (!response.IsSuccessStatusCode)
					throw new InvalidOperationException($"Failed to download {artifact.ArtifactName} from build {artifact.BuildId}: {response.StatusCode}");

				await DownloadContentAsync(response, stagingPaths.ZipPath, cancellationToken);
			}

			ExtractArtifact(stagingPaths.ZipPath, stagingPaths.ExtractPath);
			Directory.CreateDirectory(stagingPaths.PackageSourcePath);

			var version = CopyPackagesAndFindVersion(stagingPaths.ExtractPath, stagingPaths.PackageSourcePath);
			WriteMetadata(artifact, finalPaths, stagingPaths.MetadataPath, version);
			ReplaceHive(stagingPaths.HivePath, finalPaths.HivePath);

			return new MauiPrArtifactDownload
			{
				Build = artifact,
				HiveRoot = finalPaths.HiveRoot,
				ArtifactPath = finalPaths.HivePath,
				PackageSourcePath = finalPaths.PackageSourcePath,
				MetadataPath = finalPaths.MetadataPath,
				Version = version,
			};
		}

		catch
		{
			if (Directory.Exists(stagingPaths.HivePath))
				Directory.Delete(stagingPaths.HivePath, recursive: true);

			throw;
		}
	}

	static async Task<FileStream> AcquireHiveLockAsync(string hivePath, CancellationToken cancellationToken)
	{
		var lockDirectory = Path.GetDirectoryName(hivePath);
		if (!string.IsNullOrWhiteSpace(lockDirectory))
			Directory.CreateDirectory(lockDirectory);

		var lockPath = hivePath + ".lock";
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
			catch (IOException)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
			}
		}
	}

	public MauiPrArtifactHivePaths GetHivePaths(MauiPrBuildArtifact artifact, string? hiveRoot = null)
	{
		ArgumentNullException.ThrowIfNull(artifact);

		var root = ResolveHiveRoot(hiveRoot);
		var hivePath = Path.Combine(
			root,
			GetRepositoryHiveName(),
			$"pr-{artifact.PullRequest}",
			$"build-{artifact.BuildId}");

		return new MauiPrArtifactHivePaths
		{
			HiveRoot = root,
			HivePath = hivePath,
			ExtractPath = Path.Combine(hivePath, "extracted"),
			PackageSourcePath = Path.Combine(hivePath, "packages"),
			ZipPath = Path.Combine(hivePath, "PackageArtifacts.zip"),
			MetadataPath = Path.Combine(hivePath, "metadata.json"),
		};
	}

	static MauiPrArtifactHivePaths CreateStagingPaths(MauiPrArtifactHivePaths finalPaths)
	{
		var stagingHivePath = finalPaths.HivePath + $".tmp-{Guid.NewGuid():N}";
		return new MauiPrArtifactHivePaths
		{
			HiveRoot = finalPaths.HiveRoot,
			HivePath = stagingHivePath,
			ExtractPath = Path.Combine(stagingHivePath, "extracted"),
			PackageSourcePath = Path.Combine(stagingHivePath, "packages"),
			ZipPath = Path.Combine(stagingHivePath, "PackageArtifacts.zip"),
			MetadataPath = Path.Combine(stagingHivePath, "metadata.json"),
		};
	}

	static void ReplaceHive(string stagingHivePath, string finalHivePath)
	{
		var parentDirectory = Path.GetDirectoryName(finalHivePath);
		if (!string.IsNullOrWhiteSpace(parentDirectory))
			Directory.CreateDirectory(parentDirectory);

		if (Directory.Exists(finalHivePath))
			Directory.Delete(finalHivePath, recursive: true);

		Directory.Move(stagingHivePath, finalHivePath);
	}

	async Task<MauiPrBuildArtifact?> FindBuildFromAzureDevOpsAsync(int prNumber, CancellationToken cancellationToken)
	{
		var buildsUrl =
			$"https://dev.azure.com/{DefaultAzureDevOpsOrganization}/{DefaultAzureDevOpsProject}/_apis/build/builds" +
			$"?api-version=7.1&branchName=refs/pull/{prNumber}/merge&$top=25";

		using var response = await SendAsync(buildsUrl, cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException($"Failed to query Azure DevOps builds for PR #{prNumber}: {response.StatusCode}");

		using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		if (!document.RootElement.TryGetProperty("value", out var builds) || builds.ValueKind != JsonValueKind.Array)
			throw new InvalidOperationException("Azure DevOps builds response did not contain a value array.");

		foreach (var build in builds.EnumerateArray())
		{
			if (!IsCompletedMauiPrBuild(build))
				continue;

			var buildId = build.GetProperty("id").GetInt32();
			var artifact = await TryGetPackageArtifactAsync(
				prNumber,
				buildId,
				DefaultAzureDevOpsOrganization,
				DefaultAzureDevOpsProject,
				build,
				cancellationToken);
			if (artifact is not null)
				return artifact;
		}

		return null;
	}

	async Task<MauiPrBuildArtifact?> FindBuildFromGitHubChecksAsync(int prNumber, CancellationToken cancellationToken)
	{
		foreach (var commitSha in await GetPullRequestCommitsAsync(prNumber, cancellationToken))
		{
			var page = 1;
			while (true)
			{
				var url = $"https://api.github.com/repos/{DotNetMauiOwner}/{DotNetMauiRepo}/commits/{commitSha}/check-runs?per_page=100&page={page}";
				using var response = await SendAsync(url, cancellationToken);
				if (!response.IsSuccessStatusCode)
					throw new InvalidOperationException($"Failed to query GitHub check runs for PR #{prNumber}: {response.StatusCode}");

				using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
				using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
				var root = document.RootElement;
				if (!root.TryGetProperty("check_runs", out var checkRuns) || checkRuns.ValueKind != JsonValueKind.Array)
					throw new InvalidOperationException("GitHub check-runs response did not contain a check_runs array.");

				foreach (var checkRun in checkRuns.EnumerateArray())
				{
					var artifact = await TryGetBuildFromCheckRunAsync(prNumber, commitSha, checkRun, cancellationToken);
					if (artifact is not null)
						return artifact;
				}

				var totalCount = root.TryGetProperty("total_count", out var totalCountProperty)
					? totalCountProperty.GetInt32()
					: checkRuns.GetArrayLength();
				if (page * 100 >= totalCount)
					break;

				page++;
			}
		}

		return null;
	}

	async Task<List<string>> GetPullRequestCommitsAsync(int prNumber, CancellationToken cancellationToken)
	{
		var url = $"https://api.github.com/repos/{DotNetMauiOwner}/{DotNetMauiRepo}/pulls/{prNumber}/commits?per_page=100";
		using var response = await SendAsync(url, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			if (response.StatusCode == HttpStatusCode.NotFound)
				throw new InvalidOperationException($"dotnet/maui PR #{prNumber} was not found.");
			if (response.StatusCode == HttpStatusCode.Forbidden)
				throw new InvalidOperationException(
					$"GitHub denied the PR lookup for dotnet/maui PR #{prNumber}. " +
					"Set GITHUB_TOKEN or GH_TOKEN if you are rate limited.");

			throw new InvalidOperationException($"Failed to query GitHub commits for PR #{prNumber}: {response.StatusCode}");
		}

		using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		if (document.RootElement.ValueKind != JsonValueKind.Array)
			throw new InvalidOperationException("GitHub commits response was not an array.");

		var commits = document.RootElement.EnumerateArray()
			.Select(commit => commit.GetProperty("sha").GetString())
			.Where(sha => !string.IsNullOrWhiteSpace(sha))
			.Select(sha => sha!)
			.Reverse()
			.ToList();

		return commits;
	}

	async Task<MauiPrBuildArtifact?> TryGetBuildFromCheckRunAsync(
		int prNumber,
		string commitSha,
		JsonElement checkRun,
		CancellationToken cancellationToken)
	{
		var name = checkRun.GetProperty("name").GetString();
		if (!IsRelevantMauiCheckName(name))
			return null;

		var status = checkRun.GetProperty("status").GetString();
		if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
			return null;

		var conclusion = checkRun.TryGetProperty("conclusion", out var conclusionProperty) && conclusionProperty.ValueKind != JsonValueKind.Null
			? conclusionProperty.GetString()
			: null;
		if (!IsSuccessfulBuildResult(conclusion))
			return null;

		if (!checkRun.TryGetProperty("details_url", out var detailsUrlProperty))
			return null;

		var detailsUrl = detailsUrlProperty.GetString();
		if (string.IsNullOrWhiteSpace(detailsUrl))
			return null;

		var match = s_buildUrlRegex.Match(detailsUrl);
		if (!match.Success || !int.TryParse(match.Groups[3].Value, out var buildId))
			return null;

		var organization = match.Groups[1].Value;
		var project = match.Groups[2].Value;
		if (!string.Equals(organization, DefaultAzureDevOpsOrganization, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(project, DefaultAzureDevOpsProject, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return await TryGetPackageArtifactAsync(
			prNumber,
			buildId,
			organization,
			project,
			buildNumber: $"PR-{prNumber}",
			status: status ?? "completed",
			result: conclusion,
			sourceVersion: commitSha,
			cancellationToken);
	}

	async Task<MauiPrBuildArtifact?> TryGetPackageArtifactAsync(
		int prNumber,
		int buildId,
		string organization,
		string project,
		JsonElement build,
		CancellationToken cancellationToken)
	{
		return await TryGetPackageArtifactAsync(
			prNumber,
			buildId,
			organization,
			project,
			buildNumber: build.GetProperty("buildNumber").GetString() ?? buildId.ToString(),
			status: build.GetProperty("status").GetString() ?? "completed",
			result: build.TryGetProperty("result", out var resultProperty) ? resultProperty.GetString() : null,
			sourceVersion: build.TryGetProperty("sourceVersion", out var sourceVersionProperty) ? sourceVersionProperty.GetString() ?? string.Empty : string.Empty,
			cancellationToken);
	}

	async Task<MauiPrBuildArtifact?> TryGetPackageArtifactAsync(
		int prNumber,
		int buildId,
		string organization,
		string project,
		string buildNumber,
		string status,
		string? result,
		string sourceVersion,
		CancellationToken cancellationToken)
	{
		if (!IsSuccessfulBuildResult(result))
			return null;

		var artifactsUrl = $"https://dev.azure.com/{organization}/{project}/_apis/build/builds/{buildId}/artifacts?api-version=7.1";
		using var response = await SendAsync(artifactsUrl, cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException($"Failed to query artifacts for Azure DevOps build {buildId}: {response.StatusCode}");

		using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		if (!document.RootElement.TryGetProperty("value", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array)
			throw new InvalidOperationException("Azure DevOps artifacts response did not contain a value array.");

		foreach (var artifact in artifacts.EnumerateArray())
		{
			var name = artifact.GetProperty("name").GetString();
			if (!string.Equals(name, PackageArtifactName, StringComparison.OrdinalIgnoreCase))
				continue;

			var downloadUrl = artifact.GetProperty("resource").GetProperty("downloadUrl").GetString();
			if (string.IsNullOrWhiteSpace(downloadUrl))
				throw new InvalidOperationException($"{PackageArtifactName} for build {buildId} did not include a download URL.");

			return new MauiPrBuildArtifact
			{
				PullRequest = prNumber,
				BuildId = buildId,
				BuildNumber = buildNumber,
				Status = status,
				Result = result,
				SourceVersion = sourceVersion,
				Organization = organization,
				Project = project,
				ArtifactName = name ?? PackageArtifactName,
				DownloadUrl = downloadUrl,
				BuildUrl = $"https://dev.azure.com/{organization}/{project}/_build/results?buildId={buildId}",
			};
		}

		return null;
	}

	async Task<HttpResponseMessage> SendAsync(
		string url,
		CancellationToken cancellationToken,
		HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Microsoft.Maui.Cli", "1.0"));
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

		var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
		if (!string.IsNullOrWhiteSpace(token) && url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		return await _httpClient.SendAsync(request, completionOption, cancellationToken);
	}

	static async Task DownloadContentAsync(HttpResponseMessage response, string destinationPath, CancellationToken cancellationToken)
	{
		var contentLength = response.Content.Headers.ContentLength;
		if (contentLength > MaxArtifactZipBytes)
			throw new InvalidOperationException($"{PackageArtifactName} is too large to download ({contentLength} bytes).");

		await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
		await using var fileStream = File.Create(destinationPath);

		var buffer = new byte[128 * 1024];
		long totalBytes = 0;
		while (true)
		{
			var bytesRead = await contentStream.ReadAsync(buffer, cancellationToken);
			if (bytesRead == 0)
				break;

			totalBytes += bytesRead;
			if (totalBytes > MaxArtifactZipBytes)
				throw new InvalidOperationException($"{PackageArtifactName} exceeded the maximum download size of {MaxArtifactZipBytes} bytes.");

			await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
		}
	}

	static void ExtractArtifact(string zipPath, string extractPath)
	{
		var extractRoot = Path.GetFullPath(extractPath);
		Directory.CreateDirectory(extractRoot);

		using var archive = ZipFile.OpenRead(zipPath);
		long extractedBytes = 0;
		var entryCount = 0;
		foreach (var entry in archive.Entries)
		{
			entryCount++;
			if (entryCount > MaxExtractedArtifactEntries)
				throw new InvalidOperationException($"{PackageArtifactName} contains too many files to extract.");

			var destinationPath = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName));
			if (!IsPathWithinDirectory(destinationPath, extractRoot))
				throw new InvalidOperationException($"{PackageArtifactName} contains an entry outside the extraction directory: {entry.FullName}");

			if (string.IsNullOrEmpty(entry.Name))
			{
				Directory.CreateDirectory(destinationPath);
				continue;
			}

			extractedBytes += entry.Length;
			if (extractedBytes > MaxExtractedArtifactBytes)
				throw new InvalidOperationException($"{PackageArtifactName} exceeds the maximum extracted size of {MaxExtractedArtifactBytes} bytes.");

			var destinationDirectory = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrWhiteSpace(destinationDirectory))
				Directory.CreateDirectory(destinationDirectory);

			entry.ExtractToFile(destinationPath, overwrite: true);
		}
	}

	static bool IsCompletedMauiPrBuild(JsonElement build)
	{
		var status = build.GetProperty("status").GetString();
		if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
			return false;

		var result = build.TryGetProperty("result", out var resultProperty) ? resultProperty.GetString() : null;
		if (!IsSuccessfulBuildResult(result))
			return false;

		if (!build.TryGetProperty("definition", out var definition))
			return false;

		var definitionName = definition.GetProperty("name").GetString();
		return string.Equals(definitionName, MauiPrDefinitionName, StringComparison.OrdinalIgnoreCase);
	}

	static bool IsRelevantMauiCheckName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return false;

		if (string.Equals(name, MauiPrDefinitionName, StringComparison.OrdinalIgnoreCase))
			return true;

		if (name.StartsWith("maui-pr (", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("maui-pr-", StringComparison.OrdinalIgnoreCase))
		{
			return !name.Contains("uitests", StringComparison.OrdinalIgnoreCase);
		}

		return false;
	}

	static bool IsSuccessfulBuildResult(string? result)
	{
		return string.Equals(result, "succeeded", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(result, "success", StringComparison.OrdinalIgnoreCase);
	}

	static bool IsPathWithinDirectory(string path, string directory)
	{
		var normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar)
			? directory
			: directory + Path.DirectorySeparatorChar;

		return path.StartsWith(normalizedDirectory, StringComparison.Ordinal) ||
			string.Equals(path, directory, StringComparison.Ordinal);
	}

	static string CopyPackagesAndFindVersion(string extractPath, string packageSourcePath)
	{
		string? version = null;
		foreach (var packagePath in Directory.EnumerateFiles(extractPath, "*.nupkg", SearchOption.AllDirectories))
		{
			var packageLength = new FileInfo(packagePath).Length;
			if (packageLength > MaxPackageBytes)
				throw new InvalidOperationException($"{packagePath} exceeds the maximum package size of {MaxPackageBytes} bytes.");

			var destination = Path.Combine(packageSourcePath, Path.GetFileName(packagePath));
			File.Copy(packagePath, destination, overwrite: true);

			version ??= TryGetPackageVersion(destination, VersionPackageId);
		}

		return version ?? throw new InvalidOperationException(
			$"{PackageArtifactName} did not contain a {VersionPackageId} package.");
	}

	static string? TryGetPackageVersion(string packagePath, string packageId)
	{
		using var archive = ZipFile.OpenRead(packagePath);
		foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)))
		{
			using var stream = entry.Open();
			var document = XDocument.Load(stream);
			var id = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "id")?.Value.Trim();
			if (!string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase))
				continue;

			var version = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "version")?.Value.Trim();
			if (string.IsNullOrWhiteSpace(version))
				throw new InvalidOperationException($"{packagePath} contains {packageId} but no package version.");

			return version;
		}

		return null;
	}

	static void WriteMetadata(MauiPrBuildArtifact artifact, MauiPrArtifactHivePaths paths, string metadataPath, string version)
	{
		var metadata = new MauiPrArtifactMetadata
		{
			CreatedAtUtc = DateTimeOffset.UtcNow,
			Repository = $"{DotNetMauiOwner}/{DotNetMauiRepo}",
			PullRequest = artifact.PullRequest,
			BuildId = artifact.BuildId,
			BuildNumber = artifact.BuildNumber,
			BuildUrl = artifact.BuildUrl,
			Status = artifact.Status,
			Result = artifact.Result,
			SourceVersion = artifact.SourceVersion,
			Organization = artifact.Organization,
			Project = artifact.Project,
			ArtifactName = artifact.ArtifactName,
			PackageVersion = version,
			HiveRoot = paths.HiveRoot,
			HivePath = paths.HivePath,
			PackageSourcePath = paths.PackageSourcePath,
			ZipPath = paths.ZipPath,
		};

		var tempPath = metadataPath + ".tmp";
		using (var stream = File.Create(tempPath))
		{
			JsonSerializer.Serialize(stream, metadata, MauiPrArtifactJsonContext.Default.MauiPrArtifactMetadata);
		}

		File.Move(tempPath, metadataPath, overwrite: true);
	}

	static string ResolveHiveRoot(string? hiveRoot)
	{
		if (!string.IsNullOrWhiteSpace(hiveRoot))
			return Path.GetFullPath(hiveRoot);

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			throw new InvalidOperationException("Could not determine the user profile directory for the MAUI hives cache.");

		return Path.Combine(userProfile, ".maui", "hives");
	}

	static string GetRepositoryHiveName()
	{
		return $"{DotNetMauiOwner}-{DotNetMauiRepo}";
	}
}

[JsonSourceGenerationOptions(
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
	WriteIndented = true)]
[JsonSerializable(typeof(MauiPrArtifactMetadata))]
internal sealed partial class MauiPrArtifactJsonContext : JsonSerializerContext;
