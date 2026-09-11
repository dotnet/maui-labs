// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.DevFlow.Skills;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiAssetAcceptanceTests : IDisposable
{
	readonly string _originalDirectory = Directory.GetCurrentDirectory();
	readonly string? _originalStateRoot = DevFlowSkillManager.StateRootOverrideForTests;
	readonly string? _originalHome = AgentEnvironmentDetector.UserHomeOverrideForTests;
	readonly Func<HttpClient>? _originalHttpFactory = AiCommands.HttpClientFactoryForTests;
	readonly string _root;
	readonly string _stateRoot;
	readonly string _homeRoot;
	string _revision = "v1";
	bool _rejectNetwork;

	public AiAssetAcceptanceTests()
	{
		var temporaryRoot = Path.Combine(Path.GetTempPath(), $"maui-ai-assets-{Guid.NewGuid():N}");
		Directory.CreateDirectory(temporaryRoot);
		Directory.SetCurrentDirectory(temporaryRoot);
		_root = Directory.GetCurrentDirectory();
		File.WriteAllText(Path.Combine(_root, ".git"), "");
		_stateRoot = Path.Combine(_root, "isolated-devflow-state");
		_homeRoot = Path.Combine(_root, "isolated-user-home");
		Directory.CreateDirectory(_homeRoot);
		AgentEnvironmentDetector.UserHomeOverrideForTests = _homeRoot;
		DevFlowSkillManager.StateRootOverrideForTests = _stateRoot;
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(new CatalogHandler(() => _revision, () => _rejectNetwork));
	}

	[Theory]
	[InlineData("ai add test-skill")]
	[InlineData("ai add hook test")]
	[InlineData("ai list hooks")]
	[InlineData("ai status canvas")]
	[InlineData("ai update agent --skill")]
	public void Grammar_RejectsAmbiguousUnsupportedOrEmptyInputs(string command)
		=> Assert.NotEmpty(Program.BuildRootCommand().Parse(command).Errors);

	[Theory]
	[InlineData("--yes")]
	[InlineData("-y")]
	public void Yes_IsNotForce(string flag)
	{
		var root = Program.BuildRootCommand();
		var ai = Assert.Single(root.Subcommands, command => command.Name == "ai");
		var init = Assert.Single(ai.Subcommands, command => command.Name == "init");
		var yes = Assert.IsType<Option<bool>>(Assert.Single(init.Options, option => option.Name == "--yes"));
		var force = Assert.IsType<Option<bool>>(Assert.Single(init.Options, option => option.Name == "--force"));
		var accepted = root.Parse($"ai init {flag}");
		Assert.Empty(accepted.Errors);
		Assert.True(accepted.GetValue(yes));
		Assert.False(accepted.GetValue(force));
		var authorized = root.Parse("ai init --force");
		Assert.True(authorized.GetValue(force));
		Assert.False(authorized.GetValue(yes));
	}

	[Fact]
	public async Task AddSkill_InstallsOnlyOneBundleAndNoMcp()
	{
		var result = await RunAsync("add", "skill", "maui-devflow-debug", "--env", "Claude", "--yes");
		AssertSucceeded(result);
		Assert.True(File.Exists(SkillFile("maui-devflow-debug")));
		Assert.False(Directory.Exists(Path.GetDirectoryName(SkillFile("maui-devflow-onboard"))));
		Assert.False(Directory.Exists(Path.GetDirectoryName(SkillFile("maui-devflow-session-review"))));
		Assert.False(File.Exists(Path.Combine(_root, ".mcp.json")));
		Assert.False(Directory.Exists(Path.Combine(_root, ".github", "agents")));
		Assert.All(Results(result), row => Assert.Equal("skill", row["kind"]!.GetValue<string>()));
	}

	[Fact]
	public async Task ExplicitInit_UsesOnlyExactUnion()
	{
		var result = await RunAsync("init", "--skill", "maui-devflow-debug", "--mcp", "maui-devflow", "--env", "Claude", "--yes");
		AssertSucceeded(result);
		Assert.True(File.Exists(SkillFile("maui-devflow-debug")));
		Assert.False(Directory.Exists(Path.GetDirectoryName(SkillFile("maui-devflow-onboard"))));
		Assert.True(File.Exists(Path.Combine(_root, ".mcp.json")));
		Assert.False(Directory.Exists(Path.Combine(_root, ".github", "agents")));
		Assert.Equal(2, Results(result).Count);
	}

	[Fact]
	public async Task Recommendations_DoNotAutomaticallyIncludeNewCatalogEntries()
	{
		var preview = await RunAsync("init", "--env", "Claude", "--dry-run");
		AssertSucceeded(preview);
		Assert.DoesNotContain(Results(preview), row => row["name"]!.GetValue<string>() is "test-skill" or "second-skill" or "maui-reviewer");
		Assert.Contains(Results(preview), row => row["name"]!.GetValue<string>() == "maui-devflow-debug");
		Assert.Contains(Results(preview), row => row["kind"]!.GetValue<string>() == "mcp");
		AssertSucceeded(await RunAsync("init", "--skill", "second-skill", "--env", "Claude", "--yes"));
		Assert.True(File.Exists(SkillFile("second-skill")));
		Assert.False(File.Exists(SkillFile("maui-devflow-debug")));
		Assert.False(File.Exists(Path.Combine(_root, ".mcp.json")));
	}

	[Fact]
	public async Task AddAgent_OnlyWritesSelectedAgentAndOwnership()
	{
		var result = await RunAsync("add", "agent", "maui-reviewer", "--env", "VsCode", "--yes");
		AssertSucceeded(result);
		Assert.True(File.Exists(AgentFile));
		Assert.False(Directory.Exists(Path.Combine(_root, ".github", "skills")));
		Assert.False(File.Exists(Path.Combine(_root, ".vscode", "mcp.json")));
		Assert.All(Results(result), row => Assert.Equal("agent", row["kind"]!.GetValue<string>()));
	}

	[Fact]
	public async Task List_DefaultIncludesAllSupportedKindsAndTypeNarrows()
	{
		var all = await RunAsync("list", "--env", "VsCode");
		AssertSucceeded(all);
		Assert.Equal(["agent", "mcp", "skill"], Results(all).Select(row => row["kind"]!.GetValue<string>()).Distinct().OrderBy(kind => kind));
		var agents = await RunAsync("list", "agent", "--env", "VsCode");
		AssertSucceeded(agents);
		Assert.NotEmpty(Results(agents));
		Assert.All(Results(agents), row => Assert.Equal("agent", row["kind"]!.GetValue<string>()));
	}

	[Theory]
	[InlineData("agent")]
	[InlineData("mcp")]
	[InlineData("skill")]
	public async Task Update_EmptyInventoryNeverCreatesRecommendations(string kind)
	{
		Directory.CreateDirectory(Path.Combine(_root, ".vscode"));
		var before = Snapshot();
		var result = await RunAsync("update", kind, "--env", "VsCode", "--yes", "--force");
		AssertSucceeded(result);
		Assert.Equal(before, Snapshot());
	}

	[Fact]
	public async Task Update_DefaultDoesNotRestoreTrackedMissingOrAddBundledSiblings()
	{
		AssertSucceeded(await RunAsync("add", "skill", "maui-devflow-debug", "--env", "Claude", "--yes"));
		Directory.Delete(Path.GetDirectoryName(SkillFile("maui-devflow-debug"))!, recursive: true);
		var result = await RunAsync("update", "--env", "Claude", "--yes", "--force");
		AssertSucceeded(result);
		Assert.False(File.Exists(SkillFile("maui-devflow-debug")));
		Assert.False(Directory.Exists(Path.GetDirectoryName(SkillFile("maui-devflow-onboard"))));
		Assert.False(File.Exists(Path.Combine(_root, ".mcp.json")));
		var status = await RunAsync("status", "skill", "--env", "Claude");
		AssertSucceeded(status);
		Assert.Contains(Results(status), row => row["name"]!.GetValue<string>() == "maui-devflow-debug" && row["state"]!.GetValue<string>().Contains("missing", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Update_UnmanagedAgentIsNotAdoptedEvenWithForce()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(AgentFile)!);
		await File.WriteAllTextAsync(AgentFile, "manual agent");
		var result = await RunAsync("update", "agent", "--env", "VsCode", "--force", "--yes");
		AssertSucceeded(result);
		Assert.Equal("manual agent", await File.ReadAllTextAsync(AgentFile));
		Assert.False(File.Exists(Path.Combine(_root, ".maui", "ai-assets.json")));
	}

	[Fact]
	public async Task Update_DoesNotRestoreMissingTrackedAgentOrMcp()
	{
		AssertSucceeded(await RunAsync("init", "--agent", "maui-reviewer", "--mcp", "maui-devflow", "--env", "VsCode", "--yes"));
		File.Delete(AgentFile);
		var config = Path.Combine(_root, ".vscode", "mcp.json");
		File.Delete(config);
		AssertSucceeded(await RunAsync("update", "--env", "VsCode", "--force", "--yes"));
		Assert.False(File.Exists(AgentFile));
		Assert.False(File.Exists(config));
		Assert.False(Directory.Exists(Path.Combine(_root, ".github", "skills")));
	}

	[Fact]
	public async Task ExplicitUpdate_UnmanagedSelectionFailsInsteadOfAdopting()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(AgentFile)!);
		await File.WriteAllTextAsync(AgentFile, "manual agent");
		var before = Snapshot();
		var result = await RunAsync("update", "agent", "--agent", "maui-reviewer", "--env", "VsCode", "--yes", "--force");
		Assert.NotEqual(0, result.ExitCode);
		Assert.Equal(before, Snapshot());
	}

	[Fact]
	public async Task CorruptOwnershipRegistry_FailsWithoutOverwritingIt()
	{
		Directory.CreateDirectory(Path.Combine(_root, ".maui"));
		var registry = Path.Combine(_root, ".maui", "ai-assets.json");
		await File.WriteAllTextAsync(registry, "{broken");
		var before = Snapshot();
		Assert.NotEqual(0, (await RunAsync("add", "agent", "maui-reviewer", "--env", "VsCode", "--force", "--yes")).ExitCode);
		Assert.Equal(before, Snapshot());
	}

	[Fact]
	public async Task Yes_DoesNotAuthorizeUnmanagedSkillReplacement()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(SkillFile("test-skill"))!);
		await File.WriteAllTextAsync(SkillFile("test-skill"), "manual skill");
		var blocked = await RunAsync("add", "skill", "test-skill", "--env", "Claude", "--yes");
		Assert.NotEqual(0, blocked.ExitCode);
		Assert.Equal("manual skill", await File.ReadAllTextAsync(SkillFile("test-skill")));
		Assert.False(File.Exists(Path.Combine(_root, ".mcp.json")));
		Assert.Contains(Results(blocked), row => row["outcome"]!.GetValue<string>() == "blocked");
		AssertSucceeded(await RunAsync("add", "skill", "test-skill", "--env", "Claude", "--force", "--yes"));
		Assert.Contains("v1", await File.ReadAllTextAsync(SkillFile("test-skill")));
	}

	[Fact]
	public async Task ManagedAgent_UpdatePreservesEditsUntilForced()
	{
		AssertSucceeded(await RunAsync("add", "agent", "maui-reviewer", "--env", "VsCode", "--yes"));
		await File.WriteAllTextAsync(AgentFile, "edited locally");
		_revision = "v2";
		var blocked = await RunAsync("update", "agent", "--agent", "maui-reviewer", "--env", "VsCode", "--yes");
		Assert.NotEqual(0, blocked.ExitCode);
		Assert.Equal("edited locally", await File.ReadAllTextAsync(AgentFile));
		AssertSucceeded(await RunAsync("update", "agent", "--agent", "maui-reviewer", "--env", "VsCode", "--force", "--yes"));
		Assert.Contains("v2", await File.ReadAllTextAsync(AgentFile));
	}

	[Fact]
	public async Task ExactUnmanagedAgent_ForceCanAdoptItForFutureUpdates()
	{
		AssertSucceeded(await RunAsync("add", "agent", "maui-reviewer", "--env", "VsCode", "--yes"));
		var registry = Path.Combine(_root, ".maui", "ai-assets.json");
		File.Delete(registry);
		AssertSucceeded(await RunAsync("add", "agent", "maui-reviewer", "--env", "VsCode", "--force", "--yes"));
		Assert.True(File.Exists(registry));
		_revision = "v2";
		AssertSucceeded(await RunAsync("update", "agent", "--agent", "maui-reviewer", "--env", "VsCode", "--yes"));
		Assert.Contains("v2", await File.ReadAllTextAsync(AgentFile));
	}

	[Fact]
	public async Task McpAdoptionAndUpdate_PreserveUserOptionsAndOtherServers()
	{
		var config = Path.Combine(_root, ".vscode", "mcp.json");
		Directory.CreateDirectory(Path.GetDirectoryName(config)!);
		var original = """
			{"servers":{"other":{"command":"other"},"maui-devflow":{"command":"custom","args":["custom"],"type":"stdio","env":{"TOKEN":"do-not-record"},"tools":["restricted"]}}}
			""";
		await File.WriteAllTextAsync(config, original);
		var blocked = await RunAsync("add", "mcp", "maui-devflow", "--env", "VsCode", "--yes");
		Assert.NotEqual(0, blocked.ExitCode);
		Assert.Equal(original, await File.ReadAllTextAsync(config));
		AssertSucceeded(await RunAsync("add", "mcp", "maui-devflow", "--env", "VsCode", "--force", "--yes"));
		var configured = JsonNode.Parse(await File.ReadAllTextAsync(config))!;
		Assert.Equal("other", configured["servers"]!["other"]!["command"]!.GetValue<string>());
		Assert.Equal("do-not-record", configured["servers"]!["maui-devflow"]!["env"]!["TOKEN"]!.GetValue<string>());
		Assert.Equal("restricted", configured["servers"]!["maui-devflow"]!["tools"]![0]!.GetValue<string>());
		Assert.DoesNotContain("do-not-record", await File.ReadAllTextAsync(Path.Combine(_root, ".maui", "ai-assets.json")));

		configured["servers"]!["maui-devflow"]!["command"] = "user-edit";
		await File.WriteAllTextAsync(config, configured.ToJsonString());
		Assert.NotEqual(0, (await RunAsync("update", "mcp", "--env", "VsCode", "--yes")).ExitCode);
		AssertSucceeded(await RunAsync("update", "mcp", "--env", "VsCode", "--force", "--yes"));
		var updated = JsonNode.Parse(await File.ReadAllTextAsync(config))!;
		Assert.Equal("maui", updated["servers"]!["maui-devflow"]!["command"]!.GetValue<string>());
		Assert.Equal("do-not-record", updated["servers"]!["maui-devflow"]!["env"]!["TOKEN"]!.GetValue<string>());
	}

	[Fact]
	public async Task ExactUnmanagedMcp_SkipsWithoutAdoption()
	{
		var config = Path.Combine(_root, ".mcp.json");
		await File.WriteAllTextAsync(config, """{"mcpServers":{"maui-devflow":{"command":"maui","args":["devflow","mcp"]}}}""");
		var result = await RunAsync("add", "mcp", "maui-devflow", "--env", "Claude", "--yes");
		AssertSucceeded(result);
		Assert.False(File.Exists(Path.Combine(_root, ".maui", "ai-assets.json")));
		Assert.Contains(Results(result), row => row["action"]!.GetValue<string>() == "skip" && !row["managed"]!.GetValue<bool>());
	}

	[Fact]
	public async Task ExactUnmanagedMcp_ForceAdoptsWithoutRewritingConfiguration()
	{
		var config = Path.Combine(_root, ".mcp.json");
		const string contents = """
			{ "mcpServers": { "maui-devflow": { "command": "maui", "args": ["devflow", "mcp"], "env": { "TOKEN": "preserve-only" } } } }
			""";
		await File.WriteAllTextAsync(config, contents);
		var modifiedAt = File.GetLastWriteTimeUtc(config);
		AssertSucceeded(await RunAsync("add", "mcp", "maui-devflow", "--env", "Claude", "--force", "--yes"));
		Assert.True(File.Exists(Path.Combine(_root, ".maui", "ai-assets.json")));
		Assert.Equal(contents, await File.ReadAllTextAsync(config));
		Assert.Equal(modifiedAt, File.GetLastWriteTimeUtc(config));
		Assert.DoesNotContain("preserve-only", await File.ReadAllTextAsync(Path.Combine(_root, ".maui", "ai-assets.json")));
	}

	[Fact]
	public async Task MalformedMcp_ForceCannotReplaceSharedConfiguration()
	{
		var path = Path.Combine(_root, ".mcp.json");
		await File.WriteAllTextAsync(path, "{broken");
		var result = await RunAsync("add", "mcp", "maui-devflow", "--env", "Claude", "--yes", "--force");
		Assert.NotEqual(0, result.ExitCode);
		Assert.Equal("{broken", await File.ReadAllTextAsync(path));
	}

	[Theory]
	[InlineData("add agent maui-reviewer --env Claude VsCode")]
	[InlineData("init --skill test-skill --agent maui-reviewer --env Claude VsCode")]
	[InlineData("list agent --env Claude")]
	[InlineData("update skill --agent maui-reviewer --env VsCode")]
	[InlineData("init --skill maui-devflow-debug missing-skill --env Claude")]
	public async Task InvalidExplicitSelection_FailsBeforeAnyWrites(string command)
	{
		var before = Snapshot();
		var result = await RunAsync(command.Split(' '));
		Assert.NotEqual(0, result.ExitCode);
		Assert.Equal(before, Snapshot());
	}

	[Fact]
	public async Task DryRun_ShowsTypedDestinationsWithoutWriting()
	{
		var before = Snapshot();
		var result = await RunAsync("init", "--skill", "maui-devflow-debug", "--mcp", "maui-devflow", "--env", "Claude", "--dry-run");
		AssertSucceeded(result);
		Assert.Equal(before, Snapshot());
		var envelope = JsonNode.Parse(result.Output)!;
		Assert.True(envelope["dryRun"]!.GetValue<bool>());
		Assert.NotNull(envelope["schemaVersion"]);
		Assert.Contains(Results(result), row => row["kind"]!.GetValue<string>() == "mcp" && row["path"]!.GetValue<string>() == Path.Combine(_root, ".mcp.json") && row["scope"]!.GetValue<string>() == "project");
		Assert.All(Results(result), row => Assert.Equal("planned", row["outcome"]!.GetValue<string>()));
	}

	[Fact]
	public async Task Status_IsOfflineAndReadOnly()
	{
		AssertSucceeded(await RunAsync("init", "--skill", "maui-devflow-debug", "--mcp", "maui-devflow", "--env", "Claude", "--yes"));
		_rejectNetwork = true;
		var before = Snapshot();
		AssertSucceeded(await RunAsync("status", "--env", "Claude"));
		Assert.Equal(before, Snapshot());
	}

	[Fact]
	public async Task CopilotMcp_PreviewsUserScopeAndKeepsOwnershipOutOfProject()
	{
		var config = Path.Combine(_homeRoot, ".copilot", "mcp-config.json");
		var registry = Path.Combine(_homeRoot, ".maui", "ai-assets.json");
		var before = Snapshot();
		var preview = await RunAsync("add", "mcp", "maui-devflow", "--env", "CopilotCli", "--dry-run");
		AssertSucceeded(preview);
		Assert.Equal(before, Snapshot());
		var planned = Assert.Single(Results(preview));
		Assert.Equal("user", planned["scope"]!.GetValue<string>());
		Assert.Equal(config, planned["path"]!.GetValue<string>());

		AssertSucceeded(await RunAsync("add", "mcp", "maui-devflow", "--env", "CopilotCli", "--yes"));
		Assert.True(File.Exists(config));
		Assert.True(File.Exists(registry));
		Assert.False(File.Exists(Path.Combine(_root, ".maui", "ai-assets.json")));
		Assert.False(File.Exists(Path.Combine(_root, ".mcp.json")));

		var created = Snapshot();
		var projectStatus = await RunAsync("status", "mcp", "--env", "VsCode");
		AssertSucceeded(projectStatus);
		Assert.DoesNotContain(Results(projectStatus), row => row["scope"]!.GetValue<string>() == "user");
		AssertSucceeded(await RunAsync("update", "mcp", "--env", "VsCode", "--yes", "--force"));
		Assert.Equal(created, Snapshot());
	}

	[Fact]
	public async Task SharedAgentDestination_DeduplicatesAndRetainsClientAssociations()
	{
		var result = await RunAsync("add", "agent", "maui-reviewer", "--env", "VsCode", "CopilotCli", "--yes");
		AssertSucceeded(result);
		var asset = Assert.Single(Results(result));
		Assert.Equal("project", asset["scope"]!.GetValue<string>());
		Assert.Equal(["CopilotCli", "VsCode"], asset["environments"]!.AsArray().Select(node => node!.GetValue<string>()).OrderBy(value => value));
		Assert.True(File.Exists(AgentFile));
		Assert.False(Directory.Exists(Path.Combine(_homeRoot, ".maui")));
	}

	[Theory]
	[InlineData("VsCode", "CopilotCli")]
	[InlineData("CopilotCli", "VsCode")]
	public async Task SharedAgentOwnership_IsRecognizedAcrossSequentialClientSelections(string first, string second)
	{
		AssertSucceeded(await RunAsync("add", "agent", "maui-reviewer", "--env", first, "--yes"));
		var status = await RunAsync("status", "agent", "--env", second);
		AssertSucceeded(status);
		Assert.True(Assert.Single(Results(status))["managed"]!.GetValue<bool>());

		_revision = "v2";
		AssertSucceeded(await RunAsync("update", "agent", "--agent", "maui-reviewer", "--env", second, "--yes"));
		Assert.Contains("v2", await File.ReadAllTextAsync(AgentFile));
		AssertSucceeded(await RunAsync("add", "agent", "maui-reviewer", "--env", first, "--yes"));
		var combined = await RunAsync("status", "agent", "--env", "VsCode", "CopilotCli");
		AssertSucceeded(combined);
		Assert.Single(Results(combined));
	}

	[Fact]
	public async Task ProjectEqualsHome_SeparatesRegistryScopesAndDoesNotExportProjectOwnership()
	{
		AgentEnvironmentDetector.UserHomeOverrideForTests = _root;
		AssertSucceeded(await RunAsync("add", "agent", "maui-reviewer", "--env", "VsCode", "--yes"));
		AssertSucceeded(await RunAsync("status"));
		AssertSucceeded(await RunAsync("list"));
		AssertSucceeded(await RunAsync("add", "mcp", "maui-devflow", "--env", "Claude", "--yes"));
		AssertSucceeded(await RunAsync("add", "mcp", "maui-devflow", "--env", "CopilotCli", "--yes"));
		var status = await RunAsync("status", "mcp");
		AssertSucceeded(status);
		var registrations = Results(status);
		Assert.Equal(2, registrations.Count);
		Assert.Contains(registrations, row => row["scope"]!.GetValue<string>() == "project" && row["path"]!.GetValue<string>() == Path.Combine(_root, ".mcp.json"));
		Assert.Contains(registrations, row => row["scope"]!.GetValue<string>() == "user" && row["path"]!.GetValue<string>() == Path.Combine(_root, ".copilot", "mcp-config.json"));

		var otherProject = Path.Combine(_root, "other-project");
		Directory.CreateDirectory(otherProject);
		File.WriteAllText(Path.Combine(otherProject, ".git"), "");
		Directory.SetCurrentDirectory(otherProject);
		var otherStatus = await RunAsync("status");
		AssertSucceeded(otherStatus);
		Assert.DoesNotContain(Results(otherStatus), row => row["kind"]!.GetValue<string>() == "agent");
		Assert.Single(Results(otherStatus), row => row["kind"]!.GetValue<string>() == "mcp");
	}

	string SkillFile(string name) => Path.Combine(_root, ".claude", "skills", name, "SKILL.md");
	string AgentFile => Path.Combine(_root, ".github", "agents", "maui-reviewer.agent.md");

	string[] Snapshot() => Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories)
		.OrderBy(path => path, StringComparer.Ordinal)
		.Select(path => Directory.Exists(path) ? $"dir:{path}" : $"{path}:{Convert.ToBase64String(File.ReadAllBytes(path))}:{File.GetLastWriteTimeUtc(path).Ticks}")
		.ToArray();

	static List<JsonObject> Results((int ExitCode, string Output) result)
		=> JsonNode.Parse(result.Output)!["results"]!.AsArray().Select(row => Assert.IsType<JsonObject>(row)).ToList();

	static void AssertSucceeded((int ExitCode, string Output) result)
		=> Assert.True(result.ExitCode == 0, result.Output);

	static async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
	{
		var originalOut = Console.Out;
		using var writer = new StringWriter();
		try
		{
			Console.SetOut(writer);
			var exitCode = await Program.BuildRootCommand().Parse(["ai", .. args, "--json"]).InvokeAsync();
			return (exitCode, writer.ToString());
		}
		finally
		{
			Console.SetOut(originalOut);
		}
	}

	public void Dispose()
	{
		Directory.SetCurrentDirectory(_originalDirectory);
		DevFlowSkillManager.StateRootOverrideForTests = _originalStateRoot;
		AgentEnvironmentDetector.UserHomeOverrideForTests = _originalHome;
		AiCommands.HttpClientFactoryForTests = _originalHttpFactory;
		Directory.Delete(_root, recursive: true);
	}

	sealed class CatalogHandler(Func<string> revision, Func<bool> rejectNetwork) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (rejectNetwork())
				throw new InvalidOperationException($"Unexpected network access during inventory: {request.RequestUri}");
			var path = request.RequestUri!.AbsolutePath;
			string body;
			if (path.EndsWith("/marketplace.json", StringComparison.Ordinal))
				body = """{"plugins":[]}""";
			else if (path.Contains("/git/trees/", StringComparison.Ordinal))
				body = """{"tree":[{"path":".github/skills/test-skill/SKILL.md","type":"blob"},{"path":".github/skills/second-skill/SKILL.md","type":"blob"},{"path":".github/agents/maui-reviewer.agent.md","type":"blob"}]}""";
			else if (path.Contains("/commits/", StringComparison.Ordinal))
				body = """{"commit":{"tree":{"sha":"tree"}}}""";
			else if (path.EndsWith("/commits", StringComparison.Ordinal))
				body = $$"""[{"sha":"{{revision()}}"}]""";
			else if (path.EndsWith("/maui-reviewer.agent.md", StringComparison.Ordinal))
				body = $"---\nname: maui-reviewer\ndescription: MAUI review guidance\n---\nMAUI agent {revision()}";
			else if (path.EndsWith("/SKILL.md", StringComparison.Ordinal))
				body = $"---\nname: {(path.Contains("second-skill", StringComparison.Ordinal) ? "second-skill" : "test-skill")}\ndescription: MAUI skill\n---\nMAUI skill {revision()}";
			else
				throw new InvalidOperationException($"Unexpected catalog request: {request.RequestUri}");
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
		}
	}
}
