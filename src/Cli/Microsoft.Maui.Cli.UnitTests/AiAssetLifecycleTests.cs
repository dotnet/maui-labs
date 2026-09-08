using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiAssetLifecycleTests
{
	[Theory]
	[InlineData("VsCode", "CopilotCli")]
	[InlineData("CopilotCli", "VsCode")]
	public async Task SharedAgent_SequentialClientSelectionPreservesOwnership(string first, string second)
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "agent", "maui-helper", "--env", first)).Exit);
		var (statusExit, status) = await test.Invoke("status", "agent", "--env", second);
		Assert.Equal(0, statusExit);
		Assert.True(Assert.Single(status["results"]!.AsArray())!["managed"]!.GetValue<bool>());
		test.Handler.Revision = "second-client-update";
		Assert.Equal(0, (await test.Invoke("update", "agent", "--agent", "maui-helper", "--env", second)).Exit);
		var registry = AiAssetRegistry.Inventory(test.Root, "project");
		var installation = Assert.Single(registry);
		Assert.Equal(2, installation.Environments.Count);
		test.Handler.Revision = "second-client-force-add";
		Assert.Equal(0, (await test.Invoke("add", "agent", "maui-helper", "--env", second, "--force")).Exit);
		Assert.Single(AiAssetRegistry.Inventory(test.Root, "project"));
	}

	[Fact]
	public async Task CoincidentRoots_PreserveScopeAndDoNotExportProjectOwnership()
	{
		using var test = new AiCommandFixture();
		AgentEnvironmentDetector.UserHomeOverrideForTests = test.Root;
		Assert.Equal(0, (await test.Invoke("add", "agent", "maui-helper", "--env", "VsCode")).Exit);
		Assert.Equal(0, (await test.Invoke("status")).Exit);
		Assert.Equal(0, (await test.Invoke("list")).Exit);
		Assert.Equal(0, (await test.Invoke("add", "mcp", "maui-devflow", "--env", "Claude")).Exit);
		Assert.Equal(0, (await test.Invoke("add", "mcp", "maui-devflow", "--env", "CopilotCli")).Exit);
		var project = AiAssetRegistry.Inventory(test.Root, "project");
		Assert.Equal(2, project.Count);
		Assert.All(project, item => Assert.Equal("project", item.Scope));
		var user = Assert.Single(AiAssetRegistry.Inventory(test.Root, "user"));
		Assert.Equal(AiAssetKind.Mcp, user.Kind);
		Assert.Equal("user", user.Scope);
		var (exit, status) = await test.Invoke("status");
		Assert.Equal(0, exit);
		Assert.Equal(3, status["results"]!.AsArray().Count);
		test.Handler.Revision = "refresh-agent-with-shared-registry";
		Assert.Equal(0, (await test.Invoke("update", "agent", "--env", "CopilotCli")).Exit);
		Assert.Single(AiAssetRegistry.Inventory(test.Root, "user"));
		Assert.Equal(2, AiAssetRegistry.Inventory(test.Root, "project").Count);

		var secondProject = Path.Combine(test.Root, "second-project");
		Directory.CreateDirectory(secondProject);
		File.WriteAllText(Path.Combine(secondProject, ".git"), "");
		Directory.SetCurrentDirectory(secondProject);
		var (secondExit, secondStatus) = await test.Invoke("status");
		Assert.Equal(0, secondExit);
		var row = Assert.Single(secondStatus["results"]!.AsArray())!;
		Assert.Equal("mcp", row["kind"]!.GetValue<string>());
		Assert.Equal("user", row["scope"]!.GetValue<string>());
		Assert.Equal("CopilotCli", Assert.Single(row["environments"]!.AsArray())!.GetValue<string>());
	}

	[Fact]
	public async Task PlainInit_UsesNamedRecommendationsInsteadOfAllDiscoveredAssets()
	{
		using var test = new AiCommandFixture();
		var (exit, plan) = await test.Invoke("init", "--env", "VsCode", "--dry-run");
		Assert.Equal(0, exit);
		var results = plan["results"]!.AsArray();
		Assert.Equal(4, results.Count);
		Assert.DoesNotContain(results, row => row!["name"]!.GetValue<string>() == "custom-skill");
		Assert.DoesNotContain(results, row => row!["name"]!.GetValue<string>() == "other-skill");
		Assert.DoesNotContain(results, row => row!["name"]!.GetValue<string>() == "maui-helper");
		var selected = await test.Invoke("init", "--skill", "other-skill", "--env", "VsCode");
		Assert.Equal(0, selected.Exit);
		Assert.Equal("other-skill", Assert.Single(selected.Result["results"]!.AsArray())!["name"]!.GetValue<string>());
	}

	[Theory]
	[InlineData("Skill", "maui-devflow-onboard", true)]
	[InlineData("Skill", "maui-devflow-debug", true)]
	[InlineData("Skill", "maui-devflow-session-review", true)]
	[InlineData("Skill", "maui-current-apis", true)]
	[InlineData("Skill", "maui-project-structure", true)]
	[InlineData("Skill", "maui-app-architecture", true)]
	[InlineData("Skill", "maui-ui-patterns", true)]
	[InlineData("Skill", "maui-unit-testing", true)]
	[InlineData("Skill", "maui-accessibility", true)]
	[InlineData("Agent", "expert-reviewer", true)]
	[InlineData("Mcp", "maui-devflow", true)]
	[InlineData("Agent", "Comet Squad", false)]
	[InlineData("Skill", "comet-go", false)]
	[InlineData("Skill", "xamarin-forms-migration", false)]
	[InlineData("Skill", "maui-platform-backend", false)]
	[InlineData("Skill", "new-catalog-skill", false)]
	[InlineData("Agent", "maui-current-apis", false)]
	public void RecommendedIdentitySet_IsExplicitAndKindSensitive(string kind, string name, bool recommended)
	{
		var asset = new AiAsset { Kind = Enum.Parse<AiAssetKind>(kind), Name = name };
		Assert.Equal(recommended, asset.Recommended);
		Assert.Equal(recommended, asset.ToJson()["recommended"]!.GetValue<bool>());
	}

	[Theory]
	[InlineData("agent", "maui-helper", "VsCode", ".maui/ai-assets.json")]
	[InlineData("skill", "custom-skill", "Claude", ".claude/skills/custom-skill/.skill-version")]
	public async Task ExactUnmanagedContent_RequiresExplicitForceToAdopt(string kind, string name, string environment, string metadataPath)
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", kind, name, "--env", environment)).Exit);
		var metadata = Path.Combine(test.Root, metadataPath);
		File.Delete(metadata);
		var before = test.Snapshot();

		var skipped = await test.Invoke("add", kind, name, "--env", environment, "--yes");
		Assert.Equal(0, skipped.Exit);
		var skippedRow = Assert.Single(skipped.Result["results"]!.AsArray())!;
		Assert.Equal("already-current-unmanaged", skippedRow["reasonCode"]!.GetValue<string>());
		Assert.False(skippedRow["managed"]!.GetValue<bool>());
		Assert.Equal(before, test.Snapshot());

		var planned = await test.Invoke("init", $"--{kind}", name, "--env", environment, "--force", "--dry-run");
		Assert.Equal(0, planned.Exit);
		var plannedRow = Assert.Single(planned.Result["results"]!.AsArray())!;
		Assert.Equal("adopt", plannedRow["action"]!.GetValue<string>());
		Assert.Equal("planned", plannedRow["outcome"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());

		var adopted = await test.Invoke("add", kind, name, "--env", environment, "--force", "--yes");
		Assert.Equal(0, adopted.Exit);
		var adoptedRow = Assert.Single(adopted.Result["results"]!.AsArray())!;
		Assert.Equal("adopt", adoptedRow["action"]!.GetValue<string>());
		Assert.True(adoptedRow["managed"]!.GetValue<bool>());
		Assert.True(File.Exists(metadata));

		test.Handler.Revision = "after-adoption";
		var updated = await test.Invoke("update", kind, $"--{kind}", name, "--env", environment);
		Assert.Equal(0, updated.Exit);
		Assert.Equal("succeeded", Assert.Single(updated.Result["results"]!.AsArray())!["outcome"]!.GetValue<string>());
	}

	[Fact]
	public async Task AgentUpdate_PreservesOriginAndProtectsLocalEdits()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "agent", "maui-helper", "--env", "VsCode", "--repo", "custom/agents", "--branch", "release")).Exit);
		var path = Path.Combine(test.Root, ".github", "agents", "maui-helper.agent.md");
		File.WriteAllText(path, "local agent instructions");
		test.Handler.Revision = "updated";
		test.Handler.Requests.Clear();
		Assert.Equal(1, (await test.Invoke("update", "agent", "--env", "VsCode")).Exit);
		Assert.Equal("local agent instructions", File.ReadAllText(path));
		Assert.Equal(0, (await test.Invoke("update", "agent", "--env", "VsCode", "--force")).Exit);
		Assert.Contains("custom/agents/release", File.ReadAllText(path));
		Assert.All(test.Handler.Requests, uri => Assert.Contains("custom/agents/release", uri.AbsolutePath));
		Assert.Equal(0, (await test.Invoke("status", "agent")).Exit);
	}

	[Fact]
	public async Task ManagedMcp_CustomizedOwnedFieldsRequireForce_ButSettingsDoNot()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "mcp", "maui-devflow", "--env", "VsCode")).Exit);
		var path = Path.Combine(test.Root, ".vscode", "mcp.json");
		var config = JsonNode.Parse(File.ReadAllText(path))!;
		var server = config["servers"]!["maui-devflow"]!;
		server["env"] = new JsonObject { ["SECRET"] = "preserved" };
		File.WriteAllText(path, config.ToJsonString());
		Assert.Equal(0, (await test.Invoke("update", "mcp", "--env", "VsCode")).Exit);
		server["args"] = new JsonArray("something", "else");
		File.WriteAllText(path, config.ToJsonString());
		Assert.Equal(1, (await test.Invoke("update", "mcp", "--env", "VsCode")).Exit);
		Assert.Equal(0, (await test.Invoke("update", "mcp", "--env", "VsCode", "--force")).Exit);
		config = JsonNode.Parse(File.ReadAllText(path))!;
		Assert.Equal("devflow", config["servers"]!["maui-devflow"]!["args"]![0]!.GetValue<string>());
		Assert.Equal("preserved", config["servers"]!["maui-devflow"]!["env"]!["SECRET"]!.GetValue<string>());
	}

	[Fact]
	public async Task MissingMcp_RemainsVisibleWithoutDetectionMarker()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "mcp", "maui-devflow", "--env", "Claude")).Exit);
		File.Delete(Path.Combine(test.Root, ".mcp.json"));
		var (exit, status) = await test.Invoke("status", "mcp");
		Assert.Equal(0, exit);
		Assert.Equal("missing", Assert.Single(status["results"]!.AsArray())!["state"]!.GetValue<string>());
	}

	[Fact]
	public async Task BundledMissing_UpdateDoesNotRestoreOrCreateSiblingDirectories()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "skill", "maui-devflow-debug", "--env", "Claude")).Exit);
		Directory.Delete(Path.Combine(test.Root, ".claude", "skills", "maui-devflow-debug"), true);
		var before = test.Snapshot();
		var (exit, update) = await test.Invoke("update", "skill", "--env", "Claude", "--force");
		Assert.Equal(0, exit);
		Assert.Equal("skipped-missing", Assert.Single(update["results"]!.AsArray())!["reasonCode"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task BundledExactUnmanaged_ForceAdoptsThroughExistingOwnerOnly()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "skill", "maui-devflow-debug", "--env", "Claude")).Exit);
		var stateRoot = Path.Combine(test.Home, ".maui", "devflow");
		Directory.Delete(stateRoot, true);
		var (exit, result) = await test.Invoke("add", "skill", "maui-devflow-debug", "--env", "Claude");
		Assert.Equal(0, exit);
		Assert.Equal("already-current-unmanaged", Assert.Single(result["results"]!.AsArray())!["reasonCode"]!.GetValue<string>());
		Assert.False(Directory.Exists(stateRoot));
		Assert.Equal(0, (await test.Invoke("update", "skill", "--env", "Claude", "--force")).Exit);
		Assert.False(Directory.Exists(stateRoot));
		var before = test.Snapshot();
		var plan = await test.Invoke("init", "--skill", "maui-devflow-debug", "--env", "Claude", "--force", "--dry-run");
		Assert.Equal(0, plan.Exit);
		Assert.Equal("adopt", Assert.Single(plan.Result["results"]!.AsArray())!["action"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());

		var adopted = await test.Invoke("add", "skill", "maui-devflow-debug", "--env", "Claude", "--force", "--yes");
		Assert.Equal(0, adopted.Exit);
		var row = Assert.Single(adopted.Result["results"]!.AsArray())!;
		Assert.Equal("adopt", row["action"]!.GetValue<string>());
		Assert.True(row["managed"]!.GetValue<bool>());
		Assert.Equal("devflow", row["owner"]!.GetValue<string>());
		Assert.True(Directory.Exists(stateRoot));
		Assert.False(File.Exists(AiAssetRegistry.RegistryPath(test.Root)));
		Assert.False(File.Exists(Path.Combine(test.Root, ".claude", "skills", "maui-devflow-debug", ".skill-version")));
		Assert.Single(Directory.GetDirectories(Path.Combine(test.Root, ".claude", "skills")));
		Assert.Equal(0, (await test.Invoke("update", "skill", "--skill", "maui-devflow-debug", "--env", "Claude")).Exit);
	}

	[Theory]
	[InlineData("Claude", "add")]
	[InlineData("VsCode", "init")]
	[InlineData("OpenCode", "add")]
	[InlineData("CopilotCli", "init")]
	public async Task ExactMcpForceAdoption_PreservesConfigurationBytesAndWritesOnlyScopedRegistry(string environmentName, string operation)
	{
		using var test = new AiCommandFixture();
		var environment = AgentEnvironmentDetector.Canonical(Enum.Parse<AgentEnvironmentKind>(environmentName), test.Root);
		var definition = McpConfigurator.OwnedDefinition(environment.Kind);
		definition["env"] = new JsonObject { ["TOKEN"] = "private-token" };
		definition["tools"] = new JsonArray("restricted");
		definition["options"] = new JsonObject { ["permission"] = "ask" };
		var config = new JsonObject
		{
			[AgentEnvironmentDetector.Describe(environment.Kind).McpContainer] = new JsonObject { ["maui-devflow"] = definition }
		};
		var path = environment.McpConfigPath;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "/* preserve original formatting and comment */\n" + config.ToJsonString() + "\n");
		var original = File.ReadAllBytes(path);
		var registryRoot = environment.Kind == AgentEnvironmentKind.CopilotCli ? test.Home : test.Root;
		var registryPath = AiAssetRegistry.RegistryPath(registryRoot);
		Assert.Equal(0, (await test.Invoke("update", "mcp", "--env", environmentName, "--force")).Exit);
		Assert.False(File.Exists(registryPath));
		Assert.Equal(0, (await test.Invoke("add", "mcp", "maui-devflow", "--env", environmentName, "--yes")).Exit);
		Assert.False(File.Exists(registryPath));
		Assert.Equal(original, File.ReadAllBytes(path));

		string[] arguments = operation == "add" ? ["add", "mcp", "maui-devflow"] : ["init", "--mcp", "maui-devflow"];
		var planned = await test.Invoke([.. arguments, "--env", environmentName, "--force", "--dry-run"]);
		Assert.Equal(0, planned.Exit);
		Assert.Equal("adopt", Assert.Single(planned.Result["results"]!.AsArray())!["action"]!.GetValue<string>());
		Assert.False(File.Exists(registryPath));
		var (exit, result) = await test.Invoke([.. arguments, "--env", environmentName, "--force", "--yes"]);
		Assert.Equal(0, exit);
		var row = Assert.Single(result["results"]!.AsArray())!;
		Assert.Equal("adopt", row["action"]!.GetValue<string>());
		Assert.True(row["managed"]!.GetValue<bool>());
		Assert.Equal(original, File.ReadAllBytes(path));
		Assert.False(File.Exists(path + ".bak"));
		Assert.True(File.Exists(registryPath));
		Assert.DoesNotContain("private-token", File.ReadAllText(registryPath));
		Assert.DoesNotContain("private-token", result.ToJsonString());
		if (environment.Kind == AgentEnvironmentKind.CopilotCli)
			Assert.False(File.Exists(AiAssetRegistry.RegistryPath(test.Root)));
	}

	[Fact]
	public async Task ListAll_HasCanonicalSkillsAgentsAndKnownMcp()
	{
		using var test = new AiCommandFixture();
		var before = test.Snapshot();
		var (exit, catalog) = await test.Invoke("list", "--env", "VsCode");
		Assert.Equal(0, exit);
		var rows = catalog["results"]!.AsArray();
		Assert.Equal(7, rows.Count);
		Assert.Single(rows, r => r!["name"]!.GetValue<string>() == "maui-devflow-debug");
		Assert.Contains(rows, r => r!["kind"]!.GetValue<string>() == "agent");
		Assert.Contains(rows, r => r!["kind"]!.GetValue<string>() == "mcp");
		Assert.Equal(4, rows.Count(row => row!["recommended"]!.GetValue<bool>()));
		Assert.False(Assert.Single(rows, row => row!["name"]!.GetValue<string>() == "other-skill")!["recommended"]!.GetValue<bool>());
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task ExecutionFailure_StopsAndReportsNotExecutedActions()
	{
		using var test = new AiCommandFixture();
		using var http = new HttpClient(test.Handler, disposeHandler: false);
		var service = new AiAssetService(http, test.Root, []);
		var request = new AiAssetRequest("init", null,
			new() { [AiAssetKind.Agent] = ["maui-helper"], [AiAssetKind.Mcp] = ["maui-devflow"] },
			[AgentEnvironmentKind.VsCode], null, null, false, false);
		var plan = await service.PlanAsync(request, CancellationToken.None);
		// Introduce a destination change between the read-only plan and the apply.
		var first = plan.First();
		if (first.Kind == AiAssetKind.Mcp)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(first.Path)!);
			File.WriteAllText(first.Path, """{"servers":{"maui-devflow":{"command":"concurrent-edit"}}}""");
		}
		else
		{
			Directory.CreateDirectory(Path.GetDirectoryName(first.Path)!);
			File.WriteAllText(first.Path, "concurrent-edit");
		}
		await service.ExecuteAsync(request, plan, CancellationToken.None);
		Assert.Equal("failed", first.Outcome);
		Assert.All(plan.Skip(1), row => Assert.Equal("not-executed", row.Outcome));
	}

	[Fact]
	public async Task Registry_ConcurrentIndependentAddsPreserveBothIdentities()
	{
		using var test = new AiCommandFixture();
		var one = Asset(test.Root, "one");
		var two = Asset(test.Root, "two");
		await Task.WhenAll(Apply(one), Apply(two));
		Assert.Equal(2, AiAssetRegistry.Inventory(test.Root, "project").Count);

		Task Apply(AiAsset asset) => AiAssetRegistry.ApplyAsync(asset, test.Root, async () =>
		{
			await Task.Delay(20);
			File.WriteAllText(asset.Path, asset.Name);
			asset.AppliedHash = AiContentHash.Bytes(System.Text.Encoding.UTF8.GetBytes(asset.Name));
		}, CancellationToken.None);
	}

	[Fact]
	public async Task RegistryWriteFailure_ReportsContentAppliedButUntracked()
	{
		using var test = new AiCommandFixture();
		var asset = Asset(test.Root, "one");
		var error = await Assert.ThrowsAsync<IOException>(() => AiAssetRegistry.ApplyAsync(asset, test.Root, () =>
		{
			File.WriteAllText(asset.Path, "applied");
			Directory.CreateDirectory(AiAssetRegistry.RegistryPath(test.Root));
			return Task.CompletedTask;
		}, CancellationToken.None));
		Assert.Contains("content was applied", error.Message);
		Assert.Equal("applied", File.ReadAllText(asset.Path));
	}

	[Fact]
	public async Task RegistryOwnershipRace_BlocksStalePlan()
	{
		using var test = new AiCommandFixture();
		var asset = Asset(test.Root, "one");
		await AiAssetRegistry.ApplyAsync(asset, test.Root, () => { asset.AppliedHash = "hash"; return Task.CompletedTask; }, CancellationToken.None);
		var applied = false;
		await Assert.ThrowsAsync<IOException>(() => AiAssetRegistry.ApplyAsync(asset, test.Root, () =>
		{
			applied = true;
			return Task.CompletedTask;
		}, CancellationToken.None));
		Assert.False(applied);
	}

	[Theory]
	[InlineData("../outside.agent.md")]
	[InlineData("/outside.agent.md")]
	public void Registry_RejectsEscapingDestinations(string path)
	{
		using var test = new AiCommandFixture();
		Directory.CreateDirectory(Path.Combine(test.Root, ".maui"));
		var registry = new JsonObject
		{
			["schemaVersion"] = 1, ["installations"] = new JsonArray(new JsonObject
			{
				["kind"] = "agent", ["name"] = "unsafe", ["scope"] = "project", ["path"] = path, ["environments"] = new JsonArray("VsCode")
			})
		};
		File.WriteAllText(AiAssetRegistry.RegistryPath(test.Root), registry.ToJsonString());
		Assert.ThrowsAny<Exception>(() => AiAssetRegistry.Inventory(test.Root, "project"));
	}

	private static AiAsset Asset(string root, string name)
	{
		var item = new AiAsset { Kind = AiAssetKind.Agent, Name = name, Path = Path.Combine(root, name + ".agent.md"), Origin = new("owner/repo", "branch", ".github/agents/" + name + ".agent.md") };
		item.Environments.Add(AgentEnvironmentKind.VsCode);
		return item;
	}
}
