// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.DevFlow.Skills;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiCommandExecutionTests : IDisposable
{
	readonly string _originalDirectory = Directory.GetCurrentDirectory();
	readonly string? _originalStateRoot = DevFlowSkillManager.StateRootOverrideForTests;
	readonly Func<HttpClient>? _originalFactory = AiCommands.HttpClientFactoryForTests;
	readonly string _root = Path.Combine(Path.GetTempPath(), $"maui-ai-command-{Guid.NewGuid():N}");
	readonly string _stateRoot = Path.Combine(Path.GetTempPath(), $"maui-ai-command-state-{Guid.NewGuid():N}");

	public AiCommandExecutionTests()
	{
		Directory.CreateDirectory(_root);
		File.WriteAllText(Path.Combine(_root, ".git"), "");
		Directory.SetCurrentDirectory(_root);
		_root = Directory.GetCurrentDirectory();
		DevFlowSkillManager.StateRootOverrideForTests = _stateRoot;
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(new CatalogHandler());
	}

	[Theory]
	[InlineData("init")]
	[InlineData("update")]
	public async Task DryRun_DoesNotWriteProjectOrDevFlowState(string command)
	{
		if (command == "update")
			Directory.CreateDirectory(Path.Combine(_root, ".claude"));
		var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories);

		var args = command == "init"
			? new[] { "ai", command, "--dry-run", "--ci", "--json", "--env", "Claude" }
			: new[] { "ai", command, "--dry-run", "--ci", "--json" };
		var result = await InvokeAsync(args);

		Assert.Equal(0, result.ExitCode);
		Assert.Equal(before, Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories));
		Assert.False(Directory.Exists(_stateRoot));
	}

	[Theory]
	[InlineData("init")]
	[InlineData("add")]
	public async Task McpFailure_ReportsPartialAndNonzeroExit(string command)
	{
		Directory.CreateDirectory(Path.Combine(_root, ".vscode"));
		var config = Path.Combine(_root, ".vscode", "mcp.json");
		await File.WriteAllTextAsync(config, "{broken");
		var args = command == "init"
			? new[] { "ai", "init", "--skill", "test-skill", "--env", "VsCode", "--ci", "--json" }
			: new[] { "ai", "add", "test-skill", "--env", "VsCode", "--ci", "--json" };

		var result = await InvokeAsync(args);

		Assert.Equal(1, result.ExitCode);
		Assert.Contains("\"partial\"", result.Output);
		Assert.Contains("\"configured\": false", result.Output);
		Assert.Equal("{broken", await File.ReadAllTextAsync(config));
		Assert.True(File.Exists(Path.Combine(_root, ".github", "skills", "test-skill", "SKILL.md")));
	}

	[Theory]
	[InlineData("init")]
	[InlineData("add")]
	public async Task CiInstallFailure_SkipsLaterEnvironmentsAndMcp(string command)
	{
		Directory.CreateDirectory(Path.Combine(_root, ".claude"));
		Directory.CreateDirectory(Path.Combine(_root, ".vscode"));
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(new CatalogHandler(failDownload: true));
		var args = command == "init"
			? new[] { "ai", "init", "--skill", "test-skill", "--env", "Claude", "VsCode", "--ci", "--json" }
			: new[] { "ai", "add", "test-skill", "--env", "Claude", "VsCode", "--ci", "--json" };

		var result = await InvokeAsync(args);

		Assert.Equal(1, result.ExitCode);
		Assert.Contains("\"partial\"", result.Output);
		Assert.False(Directory.Exists(Path.Combine(_root, ".github", "skills")));
		Assert.False(File.Exists(Path.Combine(_root, ".mcp.json")));
		Assert.False(File.Exists(Path.Combine(_root, ".vscode", "mcp.json")));
	}

	[Fact]
	public async Task BundledInitListAdd_PreservesOwnershipAndNestedDestination()
	{
		var nested = Path.Combine(_root, "src", "App");
		Directory.CreateDirectory(Path.Combine(nested, ".vscode"));
		Directory.SetCurrentDirectory(nested);
		var init = await InvokeAsync("ai", "init", "--skill", "maui-devflow-debug", "--env", "VsCode", "--no-mcp", "--ci", "--json");
		Assert.Equal(0, init.ExitCode);
		var skillsDirectory = Path.Combine(nested, ".github", "skills");
		Assert.True(File.Exists(Path.Combine(skillsDirectory, "maui-devflow-debug", "SKILL.md")));
		Assert.False(Directory.Exists(Path.Combine(_root, ".github", "skills")));

		var list = await InvokeAsync("ai", "list", "--json");
		Assert.Equal(0, list.ExitCode);
		var rows = JsonNode.Parse(list.Output)!.AsArray();
		Assert.True(rows.Single(row => row!["skill"]!.GetValue<string>() == "maui-devflow-debug")!["installed"]!.GetValue<bool>());

		var add = await InvokeAsync("ai", "add", "maui-devflow-debug", "--env", "VsCode", "--no-mcp", "--ci", "--json");
		Assert.Equal(0, add.ExitCode);
		Assert.False(File.Exists(Path.Combine(skillsDirectory, "maui-devflow-debug", ".skill-version")));

		var check = await DevFlowSkillManager.CheckAsync(
			"project", "github", Path.GetRelativePath(_root, skillsDirectory), false, CancellationToken.None, recordCheck: false);
		Assert.All(check["skills"]!.AsArray(), row => Assert.Equal("up-to-date", row!["status"]!.GetValue<string>()));

		var stateBefore = Directory.GetFiles(_stateRoot, "*", SearchOption.AllDirectories)
			.OrderBy(path => path).Select(path => (path, File.ReadAllText(path), File.GetLastWriteTimeUtc(path))).ToArray();
		var status = await InvokeAsync("ai", "status", "--json");
		Assert.Equal(0, status.ExitCode);
		Assert.Equal(stateBefore, Directory.GetFiles(_stateRoot, "*", SearchOption.AllDirectories)
			.OrderBy(path => path).Select(path => (path, File.ReadAllText(path), File.GetLastWriteTimeUtc(path))).ToArray());
	}

	[Fact]
	public void NestedAndRootGitHubTargets_RemainDistinct()
	{
		var environments = new[]
		{
			new Ai.Models.DetectedEnvironment { Kind = Ai.Models.AgentEnvironmentKind.VsCode, SkillsDirectory = Path.Combine(_root, "src", "App", ".github", "skills") },
			new Ai.Models.DetectedEnvironment { Kind = Ai.Models.AgentEnvironmentKind.CopilotCli, SkillsDirectory = Path.Combine(_root, ".github", "skills") }
		};
		var targets = AiCommands.GetDevFlowBootstrapTargets(environments);
		Assert.Equal(2, targets.Count);
		Assert.Equal(Path.Combine("src", "App", ".github", "skills"), targets[0].CustomPath);
		Assert.Null(targets[1].CustomPath);
	}

	[Theory]
	[InlineData("init")]
	[InlineData("add")]
	[InlineData("update")]
	public async Task BundledCollision_RequiresForceToReplaceLocalEdits(string command)
	{
		var skillDirectory = Path.Combine(_root, ".claude", "skills", "maui-devflow-debug");
		Directory.CreateDirectory(skillDirectory);
		var skillFile = Path.Combine(skillDirectory, "SKILL.md");
		await File.WriteAllTextAsync(skillFile, "local customization");
		string[] args = command switch
		{
			"add" => ["ai", "add", "maui-devflow-debug", "--env", "Claude", "--no-mcp", "--ci", "--json"],
			"init" => ["ai", "init", "--skill", "maui-devflow-debug", "--env", "Claude", "--no-mcp", "--ci", "--json"],
			_ => ["ai", "update", "--skill", "maui-devflow-debug", "--ci", "--json"]
		};

		var skipped = await InvokeAsync(args);
		Assert.Equal(0, skipped.ExitCode);
		Assert.Contains("\"skipped\"", skipped.Output);
		Assert.Equal("local customization", await File.ReadAllTextAsync(skillFile));

		var forced = await InvokeAsync([.. args, "--force"]);
		Assert.Equal(0, forced.ExitCode);
		Assert.NotEqual("local customization", await File.ReadAllTextAsync(skillFile));
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
		DevFlowSkillManager.StateRootOverrideForTests = _originalStateRoot;
		AiCommands.HttpClientFactoryForTests = _originalFactory;
		Directory.Delete(_root, recursive: true);
		if (Directory.Exists(_stateRoot))
			Directory.Delete(_stateRoot, recursive: true);
	}

	sealed class CatalogHandler(bool failDownload = false) : HttpMessageHandler
	{
		int _testSkillRequests;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			var path = uri.AbsolutePath;
			if (path.EndsWith("/test-skill/SKILL.md", StringComparison.Ordinal) && ++_testSkillRequests > 1 && failDownload)
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			string content;
			if (path.EndsWith("/marketplace.json", StringComparison.Ordinal))
				content = """{"plugins":[]}""";
			else if (path.Contains("/git/trees/", StringComparison.Ordinal))
				content = """{"tree":[{"path":".github/skills/test-skill/SKILL.md","type":"blob"},{"path":".github/skills/maui-devflow-debug/SKILL.md","type":"blob"}]}""";
			else if (path.EndsWith("/commits/main", StringComparison.Ordinal))
				content = """{"commit":{"tree":{"sha":"tree"}}}""";
			else if (path.EndsWith("/commits", StringComparison.Ordinal))
				content = """[{"sha":"commit"}]""";
			else if (path.EndsWith("/SKILL.md", StringComparison.Ordinal))
				content = $"---\nname: {(path.Contains("maui-devflow-debug", StringComparison.Ordinal) ? "maui-devflow-debug" : "test-skill")}\ndescription: Test skill\n---\nSkill content";
			else
				throw new InvalidOperationException($"Unexpected HTTP request: {uri}");
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
		}
	}
}
