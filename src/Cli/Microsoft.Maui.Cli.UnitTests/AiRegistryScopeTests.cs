using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiRegistryScopeTests
{
	[Theory]
	[InlineData("user", "list", "mcp", "Claude")]
	[InlineData("user", "status", "skill", "Claude")]
	[InlineData("user", "status", "agent", "VsCode")]
	[InlineData("user", "list", "agent", "VsCode")]
	[InlineData("project", "list", "skill", "Claude")]
	[InlineData("project", "list", "mcp", "Claude")]
	[InlineData("project", "status", "skill", "Claude")]
	[InlineData("project", "status", "mcp", "CopilotCli")]
	public async Task UnrelatedCorruptRegistry_DoesNotBlockSelectedCatalogOrInventory(string scope, string command, string kind, string environment)
	{
		using var test = new AiCommandFixture();
		CorruptRegistry(scope == "user" ? test.Home : test.Root);
		var before = test.Snapshot();
		var result = await test.Invoke(command, kind, "--env", environment);
		Assert.Equal(0, result.Exit);
		Assert.Equal(before, test.Snapshot());
		if (command == "status" || kind == "mcp") Assert.Empty(test.Handler.Requests);
	}

	[Fact]
	public async Task DetectedSkillInventory_DoesNotReadEitherUnrelatedOwnershipRegistry()
	{
		using var test = new AiCommandFixture();
		Directory.CreateDirectory(Path.Combine(test.Root, ".claude"));
		CorruptRegistry(test.Root);
		CorruptRegistry(test.Home);
		var before = test.Snapshot();
		Assert.Equal(0, (await test.Invoke("status", "skill")).Exit);
		Assert.Equal(before, test.Snapshot());
		Assert.Empty(test.Handler.Requests);
	}

	[Theory]
	[InlineData("user")]
	[InlineData("project")]
	public async Task SkillOnlySetup_DoesNotRequireMcpOrAgentOwnershipRegistry(string scope)
	{
		using var test = new AiCommandFixture();
		CorruptRegistry(scope == "user" ? test.Home : test.Root);
		var before = test.Snapshot();
		var result = await test.Invoke("init", "--skill", "maui-devflow-debug", "--env", "Claude", "--dry-run");
		Assert.Equal(0, result.Exit);
		Assert.Equal("create", Assert.Single(result.Result["results"]!.AsArray())!["action"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());
		Assert.Empty(test.Handler.Requests);
	}

	[Theory]
	[InlineData("user", "status", "CopilotCli")]
	[InlineData("user", "add", "CopilotCli")]
	[InlineData("user", "update", "CopilotCli")]
	[InlineData("project", "status", "Claude")]
	[InlineData("project", "add", "Claude")]
	[InlineData("project", "update", "Claude")]
	public async Task RelevantCorruptRegistry_FailsClosedBeforeAnyWrites(string scope, string command, string environment)
	{
		using var test = new AiCommandFixture();
		CorruptRegistry(scope == "user" ? test.Home : test.Root);
		var before = test.Snapshot();
		var arguments = new List<string> { command, "mcp" };
		if (command == "add") arguments.Add("maui-devflow");
		arguments.AddRange(["--env", environment]);
		var result = await test.Invoke(arguments.ToArray());
		Assert.Equal(1, result.Exit);
		Assert.Equal("preflight-failed", Assert.Single(result.Result["results"]!.AsArray())!["reasonCode"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());
		Assert.Empty(test.Handler.Requests);
	}

	[Fact]
	public async Task RelevantAgentRegistryCorruption_IsNotIgnored()
	{
		using var test = new AiCommandFixture();
		CorruptRegistry(test.Root);
		var before = test.Snapshot();
		Assert.Equal(1, (await test.Invoke("status", "agent", "--env", "VsCode")).Exit);
		Assert.Equal(before, test.Snapshot());
		Assert.Empty(test.Handler.Requests);
	}

	[Fact]
	public async Task ExplicitTarget_RetainsRelevantRegistryPathAndEvidenceWhileIgnoringOtherScope()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "mcp", "maui-devflow", "--env", "Claude")).Exit);
		var relativePath = "nested/config/mcp.json";
		var path = Path.Combine(test.Root, "nested", "config", "mcp.json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.Move(Path.Combine(test.Root, ".mcp.json"), path);
		var registry = AiAssetRegistry.Read(test.Root);
		Assert.Single(registry["installations"]!.AsArray())!["path"] = relativePath;
		File.WriteAllText(AiAssetRegistry.RegistryPath(test.Root), registry.ToJsonString());
		CorruptRegistry(test.Home);
		var before = test.Snapshot();
		var result = await test.Invoke("status", "mcp", "--env", "Claude");
		Assert.Equal(0, result.Exit);
		var environment = Assert.Single(result.Result["environments"]!.AsArray())!;
		Assert.Equal("explicit-selection", environment["reasonCode"]!.GetValue<string>());
		Assert.Equal(AiAssetRegistry.RegistryPath(test.Root), environment["markerPath"]!.GetValue<string>());
		Assert.Equal(path, environment["mcpPath"]!.GetValue<string>());
		Assert.Equal(path, Assert.Single(result.Result["results"]!.AsArray())!["path"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());
		Assert.Empty(test.Handler.Requests);
	}

	private static void CorruptRegistry(string root)
	{
		var path = AiAssetRegistry.RegistryPath(root);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{ malformed ownership");
	}
}
