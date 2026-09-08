using Microsoft.Maui.Cli.Commands;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class AiCommandsTests
{
	[Theory]
	[InlineData("add skill example --env Claude --yes")]
	[InlineData("add agent example --env VsCode -y --force")]
	[InlineData("add mcp maui-devflow --env CopilotCli")]
	[InlineData("init --skill one --skill two --agent helper --mcp maui-devflow --env Claude VsCode")]
	[InlineData("update skill --skill one two --env OpenCode")]
	[InlineData("list agent --env VsCode")]
	[InlineData("status mcp --env Claude")]
	[InlineData("list")]
	[InlineData("status")]
	[InlineData("update")]
	public void ApprovedGrammar_Parses(string arguments) => Assert.Empty(AiCommands.Create().Parse(arguments).Errors);

	[Theory]
	[InlineData("add example")]
	[InlineData("add skill")]
	[InlineData("add hooks foo")]
	[InlineData("add canvases foo")]
	[InlineData("status hooks")]
	[InlineData("list canvas")]
	[InlineData("init --skill")]
	[InlineData("init --agent")]
	[InlineData("init --mcp")]
	[InlineData("init --env")]
	[InlineData("update --skill")]
	[InlineData("init --no-mcp")]
	[InlineData("add mcp maui-devflow --repo owner/repo")]
	[InlineData("add mcp maui-devflow --branch main")]
	[InlineData("status --repo owner/repo")]
	[InlineData("status mcp --branch main")]
	public void ObsoleteOrIncompleteGrammar_Fails(string arguments) => Assert.NotEmpty(AiCommands.Create().Parse(arguments).Errors);

	[Fact]
	public void YesAndForce_AreSeparateOptions()
	{
		var init = AiCommands.Create().Subcommands.Single(c => c.Name == "init");
		Assert.DoesNotContain("-y", init.Options.Single(o => o.Name == "--force").Aliases);
		Assert.Contains("-y", init.Options.Single(o => o.Name == "--yes").Aliases);
	}

	[Theory]
	[InlineData("skill", "No implicit MCP", "Exact catalog skill name")]
	[InlineData("agent", "project-scoped", "Exact catalog agent name")]
	[InlineData("mcp", "user-wide", "Known MCP registration name")]
	public void AddHelp_IsSpecificToAssetKind(string kind, string purpose, string argument)
	{
		var add = AiCommands.Create().Subcommands.Single(c => c.Name == "add").Subcommands.Single(c => c.Name == kind);
		Assert.Contains(purpose, add.Description);
		Assert.Contains(argument, add.Arguments.Single().Description);
		if (kind == "agent") Assert.Contains("VsCode, CopilotCli", add.Options.Single(o => o.Name == "--env").Description);
		if (kind is "agent" or "mcp")
			Assert.DoesNotContain("downgrading", add.Options.Single(o => o.Name == "--force").Description);
		else
			Assert.Contains("downgrading bundled DevFlow", add.Options.Single(o => o.Name == "--force").Description);
	}

	[Fact]
	public void RootHelp_DisclosesUserWideCopilotMcpException()
	{
		var root = AiCommands.Create();
		Assert.Contains("Copilot CLI MCP is user-wide", root.Description);
		Assert.Contains("~/.copilot/mcp-config.json", root.Description);
		Assert.Contains("detected nested skill directories", root.Description);
		Assert.Contains("downgrading bundled DevFlow", root.Subcommands.Single(c => c.Name == "init").Options.Single(o => o.Name == "--force").Description);
		Assert.Contains("downgrading bundled DevFlow", root.Subcommands.Single(c => c.Name == "update").Options.Single(o => o.Name == "--force").Description);
	}

	[Fact]
	public void UpdateHelp_DoesNotSuggestAdoptionOrEnvironmentCreation()
	{
		var update = AiCommands.Create().Subcommands.Single(c => c.Name == "update");
		Assert.Contains("never adopt unmanaged or restore missing", update.Options.Single(o => o.Name == "--force").Description);
		Assert.Contains("never create", update.Options.Single(o => o.Name == "--env").Description);
		Assert.Contains("prompted by default", update.Options.Single(o => o.Name == "--yes").Description);
		Assert.Contains("Optional", update.Arguments.Single().Description);
	}

	[Theory]
	[InlineData("list", "mcp", "--repo", "owner/repo")]
	[InlineData("update", "mcp", "--branch", "other")]
	[InlineData("init", "--mcp", "maui-devflow", "--repo", "owner/repo")]
	public async Task McpOnlySelection_RejectsUnusedSourceOverrides(params string[] arguments)
	{
		using var test = new AiCommandFixture();
		var before = test.Snapshot();
		var (exit, result) = await test.Invoke([.. arguments, "--env", "Claude"]);
		Assert.Equal(1, exit);
		Assert.Contains("apply only to skills and agents", result.ToJsonString());
		Assert.Equal(before, test.Snapshot());
		Assert.Empty(test.Handler.Requests);
	}

	[Fact]
	public void GitHubClient_UsesFiniteTimeout()
	{
		using var client = AiCommands.CreateGitHubHttpClient();
		Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
	}
}
