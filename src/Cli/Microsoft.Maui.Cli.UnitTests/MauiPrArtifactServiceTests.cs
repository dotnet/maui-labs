// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Maui.Cli.Services;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class MauiPrArtifactServiceTests
{
	[Fact]
	public async Task FindPackageArtifactAsync_AzureDevOpsBuildWithPackageArtifacts_ReturnsArtifact()
	{
		var service = CreateService(request =>
		{
			var url = request.RequestUri!.ToString();
			if (url.Contains("_apis/build/builds?api-version=7.1", StringComparison.Ordinal))
			{
				return JsonResponse("""
					{
					  "value": [
					    {
					      "id": 123456,
					      "buildNumber": "20260608.1",
					      "status": "completed",
					      "result": "succeeded",
					      "sourceVersion": "abc123",
					      "definition": { "name": "maui-pr" }
					    }
					  ]
					}
					""");
			}

			if (url.Contains("_apis/build/builds/123456/artifacts", StringComparison.Ordinal))
			{
				return JsonResponse("""
					{
					  "value": [
					    {
					      "name": "PackageArtifacts",
					      "resource": { "downloadUrl": "https://example.test/artifacts.zip" }
					    }
					  ]
					}
					""");
			}

			return NotFound();
		});

		var artifact = await service.FindPackageArtifactAsync(24888);

		Assert.Equal(24888, artifact.PullRequest);
		Assert.Equal(123456, artifact.BuildId);
		Assert.Equal("20260608.1", artifact.BuildNumber);
		Assert.Equal("https://example.test/artifacts.zip", artifact.DownloadUrl);
		Assert.Equal("https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456", artifact.BuildUrl);
	}

	[Fact]
	public async Task FindPackageArtifactAsync_FailedAzureDevOpsBuild_DoesNotReturnArtifact()
	{
		var service = CreateService(request =>
		{
			var url = request.RequestUri!.ToString();
			if (url.Contains("_apis/build/builds?api-version=7.1", StringComparison.Ordinal))
			{
				return JsonResponse("""
					{
					  "value": [
					    {
					      "id": 123456,
					      "buildNumber": "20260608.1",
					      "status": "completed",
					      "result": "failed",
					      "sourceVersion": "abc123",
					      "definition": { "name": "maui-pr" }
					    }
					  ]
					}
					""");
			}

			if (url.Contains("/pulls/24888/commits", StringComparison.Ordinal))
				return JsonResponse("[]");

			return NotFound();
		});

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FindPackageArtifactAsync(24888));
		Assert.Contains("No completed dotnet/maui PR build", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DownloadPackageArtifactAsync_CopiesPackagesAndReadsControlsVersionFromNuspec()
	{
		using var directory = TemporaryDirectory.Create();
		var artifactZip = CreateArtifactZip(
			("nested/Microsoft.Maui.Controls.Build.Tasks.1.0.0.nupkg", CreateNupkg("Microsoft.Maui.Controls.Build.Tasks", "1.0.0")),
			("nested/Microsoft.Maui.Controls.10.0.999-ci.pr.1.nupkg", CreateNupkg("Microsoft.Maui.Controls", "10.0.999-ci.pr.1")));
		var service = CreateService(request =>
		{
			if (request.RequestUri!.ToString() == "https://example.test/artifacts.zip")
			{
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new ByteArrayContent(artifactZip),
				};
			}

			return NotFound();
		});
		var artifact = new MauiPrBuildArtifact
		{
			PullRequest = 24888,
			BuildId = 123456,
			BuildNumber = "20260608.1",
			Status = "completed",
			Result = "succeeded",
			SourceVersion = "abc123",
			Organization = "dnceng-public",
			Project = "public",
			ArtifactName = "PackageArtifacts",
			DownloadUrl = "https://example.test/artifacts.zip",
			BuildUrl = "https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456",
		};

		var result = await service.DownloadPackageArtifactAsync(artifact, directory.Path);

		Assert.Equal("10.0.999-ci.pr.1", result.Version);
		Assert.Equal(directory.Path, result.HiveRoot);
		Assert.Equal(Path.Combine(directory.Path, "dotnet-maui", "pr-24888", "build-123456"), result.ArtifactPath);
		Assert.Equal(Path.Combine(result.ArtifactPath, "packages"), result.PackageSourcePath);
		Assert.Equal(Path.Combine(result.ArtifactPath, "metadata.json"), result.MetadataPath);
		Assert.True(File.Exists(Path.Combine(result.PackageSourcePath, "Microsoft.Maui.Controls.10.0.999-ci.pr.1.nupkg")));
		Assert.True(File.Exists(Path.Combine(result.PackageSourcePath, "Microsoft.Maui.Controls.Build.Tasks.1.0.0.nupkg")));
		Assert.True(File.Exists(result.MetadataPath));

		using var metadata = JsonDocument.Parse(File.ReadAllText(result.MetadataPath));
		var root = metadata.RootElement;
		Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
		Assert.Equal("dotnet/maui", root.GetProperty("repository").GetString());
		Assert.Equal(24888, root.GetProperty("pull_request").GetInt32());
		Assert.Equal(123456, root.GetProperty("build_id").GetInt32());
		Assert.Equal("20260608.1", root.GetProperty("build_number").GetString());
		Assert.Equal("abc123", root.GetProperty("source_version").GetString());
		Assert.Equal("10.0.999-ci.pr.1", root.GetProperty("package_version").GetString());
		Assert.Equal(result.PackageSourcePath, root.GetProperty("package_source_path").GetString());
		Assert.Equal(result.ArtifactPath, root.GetProperty("hive_path").GetString());
	}

	[Fact]
	public async Task DownloadPackageArtifactAsync_DownloadFailure_DoesNotReplaceExistingHive()
	{
		using var directory = TemporaryDirectory.Create();
		var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
		var artifact = CreateArtifact();
		var paths = service.GetHivePaths(artifact, directory.Path);
		Directory.CreateDirectory(paths.HivePath);
		var existingFile = Path.Combine(paths.HivePath, "existing.txt");
		await File.WriteAllTextAsync(existingFile, "keep");

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadPackageArtifactAsync(artifact, directory.Path));

		Assert.Contains("Failed to download", exception.Message, StringComparison.Ordinal);
		Assert.True(File.Exists(existingFile));
		Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(paths.HivePath)!, $"{Path.GetFileName(paths.HivePath)}.tmp-*"));
	}

	[Fact]
	public async Task DownloadPackageArtifactAsync_ZipSlipEntry_ThrowsAndDoesNotCreateHive()
	{
		using var directory = TemporaryDirectory.Create();
		var artifactZip = CreateArtifactZip(("../evil.txt", [1, 2, 3]));
		var service = CreateDownloadService(artifactZip);
		var artifact = CreateArtifact();
		var paths = service.GetHivePaths(artifact, directory.Path);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadPackageArtifactAsync(artifact, directory.Path));

		Assert.Contains("outside the extraction directory", exception.Message, StringComparison.Ordinal);
		Assert.False(File.Exists(Path.Combine(directory.Path, "dotnet-maui", "evil.txt")));
		Assert.False(Directory.Exists(paths.HivePath));
	}

	[Fact]
	public async Task DownloadPackageArtifactAsync_MissingControlsPackage_DoesNotReplaceExistingHive()
	{
		using var directory = TemporaryDirectory.Create();
		var artifactZip = CreateArtifactZip(("nested/Microsoft.Maui.Core.1.0.0.nupkg", CreateNupkg("Microsoft.Maui.Core", "1.0.0")));
		var service = CreateDownloadService(artifactZip);
		var artifact = CreateArtifact();
		var paths = service.GetHivePaths(artifact, directory.Path);
		Directory.CreateDirectory(paths.HivePath);
		var existingFile = Path.Combine(paths.HivePath, "existing.txt");
		await File.WriteAllTextAsync(existingFile, "keep");

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadPackageArtifactAsync(artifact, directory.Path));

		Assert.Contains("did not contain a Microsoft.Maui.Controls package", exception.Message, StringComparison.Ordinal);
		Assert.True(File.Exists(existingFile));
		Assert.False(File.Exists(paths.MetadataPath));
	}

	[Fact]
	public void GetHivePaths_CustomRoot_ReturnsRepositoryScopedBuildHive()
	{
		using var directory = TemporaryDirectory.Create();
		var service = CreateService(_ => NotFound());
		var artifact = new MauiPrBuildArtifact
		{
			PullRequest = 24888,
			BuildId = 123456,
			BuildNumber = "20260608.1",
			Status = "completed",
			Result = "succeeded",
			SourceVersion = "abc123",
			Organization = "dnceng-public",
			Project = "public",
			ArtifactName = "PackageArtifacts",
			DownloadUrl = "https://example.test/artifacts.zip",
			BuildUrl = "https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456",
		};

		var paths = service.GetHivePaths(artifact, directory.Path);

		var expectedHivePath = Path.Combine(directory.Path, "dotnet-maui", "pr-24888", "build-123456");
		Assert.Equal(directory.Path, paths.HiveRoot);
		Assert.Equal(expectedHivePath, paths.HivePath);
		Assert.Equal(Path.Combine(expectedHivePath, "extracted"), paths.ExtractPath);
		Assert.Equal(Path.Combine(expectedHivePath, "packages"), paths.PackageSourcePath);
		Assert.Equal(Path.Combine(expectedHivePath, "PackageArtifacts.zip"), paths.ZipPath);
		Assert.Equal(Path.Combine(expectedHivePath, "metadata.json"), paths.MetadataPath);
	}

	static MauiPrArtifactService CreateService(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
		new(new HttpClient(new StubHandler(handler))
		{
			Timeout = Timeout.InfiniteTimeSpan,
		});

	static MauiPrArtifactService CreateDownloadService(byte[] artifactZip) =>
		CreateService(request =>
		{
			if (request.RequestUri!.ToString() == "https://example.test/artifacts.zip")
			{
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new ByteArrayContent(artifactZip),
				};
			}

			return NotFound();
		});

	static MauiPrBuildArtifact CreateArtifact() =>
		new()
		{
			PullRequest = 24888,
			BuildId = 123456,
			BuildNumber = "20260608.1",
			Status = "completed",
			Result = "succeeded",
			SourceVersion = "abc123",
			Organization = "dnceng-public",
			Project = "public",
			ArtifactName = "PackageArtifacts",
			DownloadUrl = "https://example.test/artifacts.zip",
			BuildUrl = "https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456",
		};

	static HttpResponseMessage JsonResponse(string json) =>
		new(HttpStatusCode.OK)
		{
			Content = new StringContent(json),
		};

	static HttpResponseMessage NotFound() =>
		new(HttpStatusCode.NotFound)
		{
			Content = new StringContent("{}"),
		};

	static byte[] CreateArtifactZip(params (string Path, byte[] Content)[] entries)
	{
		using var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var entry in entries)
			{
				var zipEntry = archive.CreateEntry(entry.Path);
				using var entryStream = zipEntry.Open();
				entryStream.Write(entry.Content);
			}
		}

		return stream.ToArray();
	}

	static byte[] CreateNupkg(string id, string version)
	{
		using var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			var nuspec = archive.CreateEntry($"{id}.nuspec");
			using var writer = new StreamWriter(nuspec.Open());
			writer.Write($"""
				<?xml version="1.0" encoding="utf-8"?>
				<package>
				  <metadata>
				    <id>{id}</id>
				    <version>{version}</version>
				  </metadata>
				</package>
				""");
		}

		return stream.ToArray();
	}

	sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(handler(request));
	}

	sealed class TemporaryDirectory : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maui-cli-tests", Guid.NewGuid().ToString("N"));

		TemporaryDirectory()
		{
			Directory.CreateDirectory(Path);
		}

		public static TemporaryDirectory Create() => new();

		public void Dispose()
		{
			if (Directory.Exists(Path))
				Directory.Delete(Path, recursive: true);
		}
	}
}
