using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.DevFlow.Skills;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiCommandExecutionTests
{
	[Theory]
	[InlineData("skill", "custom-skill", "Claude")]
	[InlineData("agent", "maui-helper", "VsCode")]
	[InlineData("mcp", "maui-devflow", "CopilotCli")]
	public async Task AddOneKind_DoesNotInstallSiblings(string kind, string name, string environment)
	{
		using var test = new AiCommandFixture();
		var (exit, result) = await test.Invoke("add", kind, name, "--env", environment);
		Assert.Equal(0, exit);
		var row = Assert.Single(result["results"]!.AsArray());
		Assert.Equal(kind, row!["kind"]!.GetValue<string>());
		Assert.Equal("succeeded", row["outcome"]!.GetValue<string>());
		if (kind != "skill") Assert.False(Directory.Exists(Path.Combine(test.Root, ".claude", "skills")));
		if (kind != "mcp") Assert.False(File.Exists(Path.Combine(test.Root, ".mcp.json")));
		Assert.False(File.Exists(Path.Combine(test.Root, ".gitignore")));
	}

	[Theory]
	[InlineData("init", "--skill", "custom-skill", "--env", "Claude")]
	[InlineData("init", "--agent", "maui-helper", "--env", "VsCode")]
	[InlineData("init", "--mcp", "maui-devflow", "--env", "OpenCode")]
	public async Task DryRun_IsTypedAndDoesNotWrite(params string[] arguments)
	{
		using var test = new AiCommandFixture();
		var before = test.Snapshot();
		var (exit, result) = await test.Invoke([.. arguments, "--dry-run"]);
		Assert.Equal(0, exit);
		Assert.Equal(1, result["schemaVersion"]!.GetValue<int>());
		var row = Assert.Single(result["results"]!.AsArray())!;
		foreach (var field in new[] { "kind", "name", "environments", "scope", "path", "owner", "managed", "state", "action", "outcome", "reasonCode", "message", "requiresForce" })
			Assert.True(row.AsObject().ContainsKey(field), field);
		Assert.Equal("create", row["action"]!.GetValue<string>());
		Assert.Equal("planned", row["outcome"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task ExactUnion_InstallsOnlySelectedAssets()
	{
		using var test = new AiCommandFixture();
		var (exit, result) = await test.Invoke("init", "--skill", "maui-devflow-debug", "--agent", "maui-helper", "--mcp", "maui-devflow", "--env", "VsCode");
		Assert.Equal(0, exit);
		Assert.Equal(3, result["results"]!.AsArray().Count);
		Assert.False(Directory.Exists(Path.Combine(test.Root, ".github", "skills", "maui-devflow-onboard")));
		Assert.False(Directory.Exists(Path.Combine(test.Root, ".github", "skills", "custom-skill")));
	}

	[Theory]
	[InlineData("init", "--skill", "custom-skill", "unknown", "--env", "Claude")]
	[InlineData("add", "agent", "maui-helper", "--env", "Claude", "VsCode")]
	[InlineData("update", "skill", "--agent", "maui-helper", "--env", "VsCode")]
	[InlineData("list", "agent", "--env", "Claude")]
	[InlineData("init", "--skill", "", "--env", "Claude")]
	[InlineData("init", "--mcp", "maui-devflow", "--env", "Imaginary")]
	public async Task InvalidSelection_FailsCompletePreflight(params string[] arguments)
	{
		using var test = new AiCommandFixture();
		var before = test.Snapshot();
		Assert.Equal(1, (await test.Invoke(arguments)).Exit);
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task NoDetection_DoesNotInventDefaultEnvironment()
	{
		using var test = new AiCommandFixture();
		var (exit, result) = await test.Invoke("init");
		Assert.Equal(1, exit);
		Assert.Contains("--env", result.ToJsonString());
		Assert.False(Directory.Exists(Path.Combine(test.Root, ".claude")));
	}

	[Fact]
	public async Task NestedBundledSkill_UsesOwnerStateAndDoesNotRestoreSiblings()
	{
		using var test = new AiCommandFixture();
		var nested = Path.Combine(test.Root, "src", "App");
		Directory.CreateDirectory(Path.Combine(nested, ".vscode"));
		Directory.SetCurrentDirectory(nested);
		Assert.Equal(0, (await test.Invoke("add", "skill", "maui-devflow-debug", "--env", "VsCode")).Exit);
		var directory = Path.Combine(nested, ".github", "skills", "maui-devflow-debug");
		Assert.True(File.Exists(Path.Combine(directory, "SKILL.md")));
		Assert.False(File.Exists(Path.Combine(directory, ".skill-version")));
		Assert.False(Directory.Exists(Path.Combine(test.Root, ".github")));
		var before = test.Snapshot();
		Assert.Equal(0, (await test.Invoke("status", "skill", "--env", "VsCode")).Exit);
		Assert.Equal(before, test.Snapshot());
		Assert.Equal(0, (await test.Invoke("update", "skill", "--env", "VsCode", "--force")).Exit);
		Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(directory)!));
	}

	[Fact]
	public async Task AgentPathDedup_PreservesEnvironmentAssociations()
	{
		using var test = new AiCommandFixture();
		var (_, result) = await test.Invoke("add", "agent", "maui-helper", "--env", "VsCode", "CopilotCli");
		Assert.Equal(2, Assert.Single(result["results"]!.AsArray())!["environments"]!.AsArray().Count);
		var registry = JsonNode.Parse(File.ReadAllText(Path.Combine(test.Root, ".maui", "ai-assets.json")))!;
		Assert.Equal(2, Assert.Single(registry["installations"]!.AsArray())!["environments"]!.AsArray().Count);
	}

	[Theory]
	[InlineData("Claude", ".mcp.json", "mcpServers")]
	[InlineData("VsCode", ".vscode/mcp.json", "servers")]
	[InlineData("OpenCode", "opencode.json", "mcp")]
	[InlineData("CopilotCli", ".copilot/mcp-config.json", "mcpServers")]
	public async Task McpConflict_ForceAdoptsOnlyOwnedLaunchFields(string environment, string relativePath, string container)
	{
		using var test = new AiCommandFixture();
		var root = environment == "CopilotCli" ? test.Home : test.Root;
		var path = Path.Combine(root, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var config = new JsonObject
		{
			[container] = new JsonObject
			{
				["maui-devflow"] = new JsonObject { ["command"] = "custom", ["env"] = new JsonObject { ["SECRET"] = "private-value" }, ["tools"] = new JsonArray("one") },
				["other"] = new JsonObject { ["command"] = "keep-me" }
			}
		};
		File.WriteAllText(path, config.ToJsonString());
		var original = File.ReadAllText(path);
		var blocked = await test.Invoke("add", "mcp", "maui-devflow", "--env", environment, "--yes");
		Assert.Equal(1, blocked.Exit);
		Assert.Equal(original, File.ReadAllText(path));
		Assert.DoesNotContain("private-value", blocked.Result.ToJsonString());
		var adopted = await test.Invoke("add", "mcp", "maui-devflow", "--env", environment, "--force");
		Assert.Equal(0, adopted.Exit);
		Assert.Equal("adopt", Assert.Single(adopted.Result["results"]!.AsArray())!["action"]!.GetValue<string>());
		var merged = JsonNode.Parse(File.ReadAllText(path))!;
		Assert.Equal("private-value", merged[container]!["maui-devflow"]!["env"]!["SECRET"]!.GetValue<string>());
		Assert.Equal("one", merged[container]!["maui-devflow"]!["tools"]![0]!.GetValue<string>());
		Assert.Equal("keep-me", merged[container]!["other"]!["command"]!.GetValue<string>());
		var registry = File.ReadAllText(Path.Combine(root, ".maui", "ai-assets.json"));
		Assert.DoesNotContain("private-value", registry);
		if (environment == "CopilotCli") Assert.False(File.Exists(Path.Combine(test.Root, ".maui", "ai-assets.json")));
	}

	[Fact]
	public async Task ExactUnmanagedMcp_AddSkipsWithoutAdopting()
	{
		using var test = new AiCommandFixture();
		File.WriteAllText(Path.Combine(test.Root, ".mcp.json"), """{"mcpServers":{"maui-devflow":{"command":"maui","args":["devflow","mcp"]}}}""");
		var (exit, result) = await test.Invoke("add", "mcp", "maui-devflow", "--env", "Claude");
		Assert.Equal(0, exit);
		Assert.Equal("already-configured-unmanaged", Assert.Single(result["results"]!.AsArray())!["reasonCode"]!.GetValue<string>());
		Assert.False(File.Exists(Path.Combine(test.Root, ".maui", "ai-assets.json")));
	}

	[Theory]
	[InlineData("{broken")]
	[InlineData("""{"servers":[]}""")]
	[InlineData("""{"servers":{"maui-devflow":"broken"}}""")]
	public async Task MalformedMcp_RemainsUnchangedEvenWithForce(string config)
	{
		using var test = new AiCommandFixture();
		Directory.CreateDirectory(Path.Combine(test.Root, ".vscode"));
		var path = Path.Combine(test.Root, ".vscode", "mcp.json");
		File.WriteAllText(path, config);
		Assert.Equal(1, (await test.Invoke("add", "mcp", "maui-devflow", "--env", "VsCode", "--force")).Exit);
		Assert.Equal(config, File.ReadAllText(path));
		Assert.False(File.Exists(Path.Combine(test.Root, ".maui", "ai-assets.json")));
	}

	[Theory]
	[InlineData("agent", "maui-helper", "VsCode", ".github/agents/maui-helper.agent.md")]
	[InlineData("mcp", "maui-devflow", "Claude", ".mcp.json")]
	public async Task Update_TrackedMissingIsSkippedEvenWithForce(string kind, string name, string environment, string relativePath)
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", kind, name, "--env", environment)).Exit);
		File.Delete(Path.Combine(test.Root, relativePath));
		var (exit, result) = await test.Invoke("update", kind, "--env", environment, "--force");
		Assert.Equal(0, exit);
		Assert.Equal("skipped-missing", Assert.Single(result["results"]!.AsArray())!["reasonCode"]!.GetValue<string>());
		Assert.False(File.Exists(Path.Combine(test.Root, relativePath)));
		Assert.Equal(1, (await test.Invoke("update", kind, $"--{kind}", name, "--env", environment, "--force")).Exit);
	}

	[Fact]
	public async Task JsoncMerge_PreservesOriginalBackup()
	{
		using var test = new AiCommandFixture();
		var path = Path.Combine(test.Root, ".mcp.json");
		const string original = "{ // keep comment\n \"mcpServers\": {\"other\":{\"command\":\"other\"}}, }";
		File.WriteAllText(path, original);
		Assert.Equal(0, (await test.Invoke("add", "mcp", "maui-devflow", "--env", "Claude")).Exit);
		Assert.Equal(original, File.ReadAllText(path + ".bak"));
	}
}

internal sealed class AiCommandFixture : IDisposable
{
	private readonly string originalDirectory = Directory.GetCurrentDirectory();
	private readonly string? originalHome = AgentEnvironmentDetector.UserHomeOverrideForTests;
	private readonly string? originalStateRoot = DevFlowSkillManager.StateRootOverrideForTests;
	private readonly Func<HttpClient>? originalFactory = AiCommands.HttpClientFactoryForTests;
	internal string Root { get; }
	internal string Home { get; }
	internal AiTestCatalog Handler { get; } = new();
	internal AiCommandFixture()
	{
		Root = Path.Combine(originalDirectory, "artifacts", $"ai-command-{Guid.NewGuid():N}");
		Home = Path.Combine(Root, "test-home");
		Directory.CreateDirectory(Home);
		File.WriteAllText(Path.Combine(Root, ".git"), "");
		Directory.SetCurrentDirectory(Root);
		AgentEnvironmentDetector.UserHomeOverrideForTests = Home;
		DevFlowSkillManager.StateRootOverrideForTests = Path.Combine(Home, ".maui", "devflow");
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(Handler, disposeHandler: false);
	}
	internal async Task<(int Exit, JsonObject Result)> Invoke(params string[] arguments)
	{
		var originalOutput = Console.Out;
		using var writer = new StringWriter();
		try
		{
			Console.SetOut(writer);
			var exit = await Program.BuildRootCommand().Parse(["ai", .. arguments, "--json", "--ci"]).InvokeAsync();
			return (exit, JsonNode.Parse(writer.ToString())!.AsObject());
		}
		finally { Console.SetOut(originalOutput); }
	}
	internal string[] Snapshot() => Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
		.Select(path => path + (File.Exists(path) ? ":" + File.ReadAllText(path) : "/")).ToArray();
	public void Dispose()
	{
		Directory.SetCurrentDirectory(originalDirectory);
		AgentEnvironmentDetector.UserHomeOverrideForTests = originalHome;
		DevFlowSkillManager.StateRootOverrideForTests = originalStateRoot;
		AiCommands.HttpClientFactoryForTests = originalFactory;
		Handler.Dispose();
		Directory.Delete(Root, true);
	}
}

internal sealed class AiTestCatalog : HttpMessageHandler
{
	internal string Revision { get; set; } = "initial";
	internal List<Uri> Requests { get; } = [];
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var uri = request.RequestUri!;
		Requests.Add(uri);
		var path = uri.AbsolutePath;
		string content;
		if (path.Contains("/git/trees/"))
			content = """{"tree":[{"path":".github/skills/custom-skill/SKILL.md","type":"blob"},{"path":".github/skills/other-skill/SKILL.md","type":"blob"},{"path":".github/agents/maui-helper.agent.md","type":"blob"}]}""";
		else if (path.EndsWith("/marketplace.json")) content = """{"plugins":[]}""";
		else if (path.Contains("/commits/")) content = """{"commit":{"tree":{"sha":"tree"}}}""";
		else if (path.EndsWith("/commits")) content = """[{"sha":"new-commit"}]""";
		else if (path.EndsWith("/SKILL.md"))
			content = $"---\nname: {(path.Contains("other-skill") ? "other-skill" : "custom-skill")}\ndescription: MAUI skill\n---\n{path} {Revision}";
		else if (path.EndsWith(".agent.md"))
			content = $"---\nname: maui-helper\ndescription: MAUI development agent\n---\n{path} {Revision}";
		else return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
	}
}
