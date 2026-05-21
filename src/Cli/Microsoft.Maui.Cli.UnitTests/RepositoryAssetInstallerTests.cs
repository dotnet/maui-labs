// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class RepositoryAssetInstallerTests : IDisposable
{
	readonly string _tempDir;

	public RepositoryAssetInstallerTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	[Fact]
	public async Task GetCopilotAgentsAsync_ReturnsOnlyMauiRelatedAgents()
	{
		using var http = new HttpClient(new MapHttpMessageHandler(new Dictionary<string, string>
		{
			["expert-reviewer.agent.md"] = """
				---
				name: expert-reviewer
				description: Expert .NET MAUI DevFlow code reviewer.
				---
				# Expert Reviewer
				""",
			["generic.agent.md"] = """
				---
				name: generic
				description: Generic workflow helper.
				---
				# Generic
				"""
		}));
		var treeEntries = new List<(string Path, string Type)>
		{
			(".github/agents/expert-reviewer.agent.md", "blob"),
			(".github/agents/generic.agent.md", "blob")
		};

		var assets = await RepositoryAssetInstaller.GetCopilotAgentsAsync(
			http, "owner/repo", "main", treeEntries);

		var asset = Assert.Single(assets);
		Assert.Equal("expert-reviewer", asset.Name);
		Assert.Equal("agent", asset.Category);
		Assert.Equal(".github/agents/expert-reviewer.agent.md", asset.RemotePath);
	}

	[Fact]
	public async Task InstallAssetAsync_WritesAgentAndSkipsExistingWithoutForce()
	{
		using var http = new HttpClient(new MapHttpMessageHandler(new Dictionary<string, string>
		{
			["expert-reviewer.agent.md"] = "agent content"
		}));
		var asset = new RepositoryAssetInfo
		{
			Name = "expert-reviewer",
			Category = "agent",
			RemotePath = ".github/agents/expert-reviewer.agent.md",
			DestinationRoot = ".github/agents",
			Files = [".github/agents/expert-reviewer.agent.md"]
		};

		var first = await RepositoryAssetInstaller.InstallAssetAsync(
			http, asset, _tempDir, "owner/repo", "main", force: false);
		var second = await RepositoryAssetInstaller.InstallAssetAsync(
			http, asset, _tempDir, "owner/repo", "main", force: false);

		Assert.Equal(1, first.FilesInstalled);
		Assert.Equal(0, second.FilesInstalled);
		Assert.True(File.Exists(Path.Combine(_tempDir, ".github", "agents", "expert-reviewer.agent.md")));
		Assert.Equal(Path.Combine(_tempDir, ".github", "agents"), first.InstallPath);
	}

	[Fact]
	public void GetInstalledCopilotAgents_ReturnsOnlyMauiRelatedAgents()
	{
		var agentsDir = Path.Combine(_tempDir, ".github", "agents");
		Directory.CreateDirectory(agentsDir);
		File.WriteAllText(Path.Combine(agentsDir, "expert-reviewer.agent.md"), """
			---
			name: expert-reviewer
			description: Expert .NET MAUI DevFlow code reviewer.
			---
			# Expert Reviewer
			""");
		File.WriteAllText(Path.Combine(agentsDir, "generic.agent.md"), """
			---
			name: generic
			description: Generic workflow helper.
			---
			# Generic
			""");

		var assets = RepositoryAssetInstaller.GetInstalledCopilotAgents(_tempDir);

		var asset = Assert.Single(assets);
		Assert.Equal("expert-reviewer", asset.Name);
		Assert.Equal("agent", asset.Category);
		Assert.Equal(".github/agents", asset.DestinationRoot);
	}

	[Fact]
	public async Task InstallAssetAsync_ForceOverwritesExistingAgent()
	{
		using var http = new HttpClient(new MapHttpMessageHandler(new Dictionary<string, string>
		{
			["expert-reviewer.agent.md"] = "updated agent content"
		}));
		var asset = new RepositoryAssetInfo
		{
			Name = "expert-reviewer",
			Category = "agent",
			RemotePath = ".github/agents/expert-reviewer.agent.md",
			DestinationRoot = ".github/agents",
			Files = [".github/agents/expert-reviewer.agent.md"]
		};
		var localPath = Path.Combine(_tempDir, ".github", "agents", "expert-reviewer.agent.md");
		Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
		await File.WriteAllTextAsync(localPath, "old content");

		var result = await RepositoryAssetInstaller.InstallAssetAsync(
			http, asset, _tempDir, "owner/repo", "main", force: true);

		Assert.Equal(1, result.FilesInstalled);
		Assert.Equal("updated agent content", await File.ReadAllTextAsync(localPath));
	}

	sealed class MapHttpMessageHandler(Dictionary<string, string> responses) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri?.AbsolutePath ?? string.Empty;
			foreach (var (suffix, content) in responses)
			{
				if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				{
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(content)
					});
				}
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}
}
