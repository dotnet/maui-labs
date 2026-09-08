// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Commands;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiSkillOriginTests : IDisposable
{
	readonly string _originalDirectory = Directory.GetCurrentDirectory();
	readonly Func<HttpClient>? _originalFactory = AiCommands.HttpClientFactoryForTests;
	readonly string _root;
	readonly CatalogHandler _handler = new();

	public AiSkillOriginTests()
	{
		_root = Path.Combine(_originalDirectory, "artifacts", $"ai-origin-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_root);
		File.WriteAllText(Path.Combine(_root, ".git"), "");
		Directory.CreateDirectory(Path.Combine(_root, ".claude", "skills"));
		Directory.SetCurrentDirectory(_root);
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(_handler, disposeHandler: false);
	}

	[Theory]
	[InlineData(null, null, "custom/skills", "release")]
	[InlineData("override/skills", null, "override/skills", "release")]
	[InlineData(null, "preview", "custom/skills", "preview")]
	[InlineData("dotnet/maui-labs", "main", "dotnet/maui-labs", "main")]
	public async Task Update_UsesRecordedOriginUnlessExplicitlyOverridden(
		string? repo, string? branch, string expectedRepo, string expectedBranch)
	{
		var directory = await InstallMetadataAsync("test-skill");
		var args = new List<string> { "ai", "update", "--skill", "test-skill", "--ci", "--json" };
		if (repo is not null)
			args.AddRange(["--repo", repo]);
		if (branch is not null)
			args.AddRange(["--branch", branch]);

		var result = await InvokeAsync(args.ToArray());

		Assert.Equal(0, result.ExitCode);
		var version = await SkillVersionStore.ReadAsync(directory);
		Assert.Equal(expectedRepo, version!.Source);
		Assert.Equal(expectedBranch, version.Branch);
		Assert.Equal("new-commit", version.Commit);
		Assert.Equal(".github/skills/test-skill", version.PluginPath);
		Assert.Contains($"{expectedRepo}/{expectedBranch}", await File.ReadAllTextAsync(Path.Combine(directory, "SKILL.md")));
		Assert.All(_handler.Requests.Where(uri => uri.AbsolutePath.EndsWith("/commits", StringComparison.Ordinal)), uri =>
		{
			Assert.Equal($"/repos/{expectedRepo}/commits", uri.AbsolutePath);
			Assert.Contains($"sha={expectedBranch}", uri.Query);
		});
	}

	[Fact]
	public async Task Update_SameOriginAcrossSkills_ReusesCatalog()
	{
		await InstallMetadataAsync("test-skill");
		await InstallMetadataAsync("other-skill");

		var result = await InvokeAsync("ai", "update", "--skill", "test-skill", "other-skill", "--ci", "--json");

		Assert.Equal(0, result.ExitCode);
		Assert.Equal(1, _handler.Requests.Count(uri => uri.AbsolutePath == "/repos/custom/skills/commits/release"));
		Assert.All(new[] { "test-skill", "other-skill" }, name =>
			Assert.Contains("custom/skills/release", File.ReadAllText(Path.Combine(SkillsDirectory, name, "SKILL.md"))));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Update_MissingOrLegacyMetadata_IsUncheckableEvenWhenForced(bool legacy)
	{
		var directory = await InstallMetadataAsync("test-skill");
		var metadata = Path.Combine(directory, ".skill-version");
		if (legacy)
			await File.WriteAllTextAsync(metadata, """{"commit":"old-commit","branch":"release"}""");
		else
			File.Delete(metadata);
		var result = await InvokeAsync("ai", "update", "--skill", "test-skill", "--force", "--ci", "--json");

		Assert.Equal(1, result.ExitCode);
		Assert.DoesNotContain("All selected AI development assets are up to date", result.Output);
		Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(directory, "SKILL.md")));
		Assert.DoesNotContain(_handler.Requests, uri => uri.AbsolutePath.EndsWith("/commits", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Update_CiFailure_StopsBeforeLaterSkills(bool missingCatalog)
	{
		var first = await InstallMetadataAsync("other-skill");
		var later = await InstallMetadataAsync("test-skill");
		if (missingCatalog)
			_handler.CatalogChange = "missing";
		else
			_handler.FailOtherSkillDownload = true;

		var result = await InvokeAsync("ai", "update", "--skill", "other-skill", "test-skill", "--ci", "--json");

		Assert.Equal(1, result.ExitCode);
		Assert.Contains("partial_failure", result.Output);
		Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(first, "SKILL.md")));
		Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(later, "SKILL.md")));
		Assert.Equal(missingCatalog ? 0 : 1, _handler.Requests.Count(uri =>
			uri.AbsolutePath == "/custom/skills/release/.github/skills/test-skill/SKILL.md"));
	}

	[Theory]
	[InlineData("missing")]
	[InlineData("moved")]
	[InlineData("renamed")]
	public async Task Update_MissingOrChangedCatalogIdentity_FailsWithoutReplacingInstallation(string change)
	{
		var directory = await InstallMetadataAsync("test-skill");
		_handler.CatalogChange = change;

		var result = await InvokeAsync("ai", "update", "--skill", "test-skill", "--ci", "--json");

		Assert.Equal(1, result.ExitCode);
		Assert.Contains("partial_failure", result.Output);
		Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(directory, "SKILL.md")));
		Assert.Equal("old-commit", (await SkillVersionStore.ReadAsync(directory))!.Commit);
	}

	[Theory]
	[InlineData(null, null, "custom/skills", "release")]
	[InlineData("other/repo", null, "other/repo", "release")]
	[InlineData(null, "main", "custom/skills", "main")]
	public async Task Status_UsesRecordedOriginAndDoesNotWriteMetadata(
		string? repo, string? branch, string expectedRepo, string expectedBranch)
	{
		var directory = await InstallMetadataAsync("test-skill");
		var metadata = Path.Combine(directory, ".skill-version");
		var before = await File.ReadAllTextAsync(metadata);
		var timestamp = File.GetLastWriteTimeUtc(metadata);
		using var http = new HttpClient(_handler, disposeHandler: false);

		var rows = await AiCommands.GetMarketplaceSkillStatusRowsAsync(
			[Environment()], true, http, repo, branch, CancellationToken.None);

		Assert.Equal("Update available", Assert.Single(rows).Status);
		var request = Assert.Single(_handler.Requests);
		Assert.Equal($"/repos/{expectedRepo}/commits", request.AbsolutePath);
		Assert.Contains($"sha={expectedBranch}", request.Query);
		Assert.Equal(before, await File.ReadAllTextAsync(metadata));
		Assert.Equal(timestamp, File.GetLastWriteTimeUtc(metadata));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Status_MissingOrLegacyMetadata_IsUnknownWithoutRemoteRequests(bool legacy)
	{
		var directory = await InstallMetadataAsync("test-skill");
		if (legacy)
			await File.WriteAllTextAsync(Path.Combine(directory, ".skill-version"), """{"commit":"old-commit","branch":"release"}""");
		else
			File.Delete(Path.Combine(directory, ".skill-version"));
		using var http = new HttpClient(_handler, disposeHandler: false);

		var rows = await AiCommands.GetMarketplaceSkillStatusRowsAsync(
			[Environment()], true, http, null, null, CancellationToken.None);

		Assert.Equal("Unknown", Assert.Single(rows).Status);
		Assert.Empty(_handler.Requests);
	}

	[Fact]
	public async Task StatusCommand_OmittedOverrides_QueriesRecordedOrigin()
	{
		await InstallMetadataAsync("test-skill");

		var result = await InvokeAsync("ai", "status", "--check-updates", "--json");

		Assert.Equal(0, result.ExitCode);
		var request = Assert.Single(_handler.Requests, uri => uri.AbsolutePath.EndsWith("/commits", StringComparison.Ordinal));
		Assert.Equal("/repos/custom/skills/commits", request.AbsolutePath);
		Assert.Contains("sha=release", request.Query);
		Assert.Contains("Update available", result.Output);
	}

	[Fact]
	public void ResolveOrigin_MissingSourceAndBranch_UsesDefaults()
	{
		Assert.Equal(("dotnet/maui-labs", "main"),
			AiCommands.ResolveInstalledSkillOrigin(new InstalledSkillVersion(), null, null));
	}

	string SkillsDirectory => Path.Combine(_root, ".claude", "skills");

	DetectedEnvironment Environment() => new() { Kind = AgentEnvironmentKind.Claude, SkillsDirectory = SkillsDirectory };

	async Task<string> InstallMetadataAsync(string name)
	{
		var directory = Path.Combine(SkillsDirectory, name);
		Directory.CreateDirectory(directory);
		await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"), "original");
		await SkillVersionStore.WriteAsync(directory, new InstalledSkillVersion
		{
			Name = name, Source = "custom/skills", Branch = "release",
			PluginPath = $".github/skills/{name}", Commit = "old-commit"
		});
		return directory;
	}

	static async Task<(int ExitCode, string Output)> InvokeAsync(params string[] args)
	{
		var original = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			var exitCode = await Program.BuildRootCommand().Parse(args).InvokeAsync();
			return (exitCode, output.ToString());
		}
		finally
		{
			Console.SetOut(original);
		}
	}

	public void Dispose()
	{
		Directory.SetCurrentDirectory(_originalDirectory);
		AiCommands.HttpClientFactoryForTests = _originalFactory;
		_handler.Dispose();
		Directory.Delete(_root, recursive: true);
	}

	sealed class CatalogHandler : HttpMessageHandler
	{
		public List<Uri> Requests { get; } = [];
		public string? CatalogChange { get; set; }
		public bool FailOtherSkillDownload { get; set; }
		int _otherSkillRequests;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			Requests.Add(uri);
			var path = uri.AbsolutePath;
			if (path.Contains("/custom/skills/release/", StringComparison.Ordinal) &&
				path.EndsWith("/other-skill/SKILL.md", StringComparison.Ordinal) &&
				++_otherSkillRequests > 1 && FailOtherSkillDownload)
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			string content;
			if (path.EndsWith("/marketplace.json", StringComparison.Ordinal))
				content = """{"plugins":[]}""";
			else if (path.Contains("/git/trees/", StringComparison.Ordinal))
				content = CatalogChange == "missing"
					? """{"tree":[]}"""
					: $$"""{"tree":[{"path":".github/skills/{{(CatalogChange == "moved" ? "moved-skill" : "test-skill")}}/SKILL.md","type":"blob"},{"path":".github/skills/other-skill/SKILL.md","type":"blob"}]}""";
			else if (path.EndsWith("/commits", StringComparison.Ordinal))
				content = """[{"sha":"new-commit"}]""";
			else if (path.Contains("/commits/", StringComparison.Ordinal))
				content = """{"commit":{"tree":{"sha":"tree"}}}""";
			else if (path.EndsWith("/SKILL.md", StringComparison.Ordinal))
			{
				var name = path.Contains("/other-skill/", StringComparison.Ordinal) ? "other-skill"
					: CatalogChange == "renamed" ? "renamed-skill" : "test-skill";
				content = $"---\nname: {name}\ndescription: Test skill\n---\n{path}";
			}
			else
				throw new InvalidOperationException($"Unexpected HTTP request: {uri}");
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
		}
	}
}
