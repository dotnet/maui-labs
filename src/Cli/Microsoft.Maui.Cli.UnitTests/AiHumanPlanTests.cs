using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Commands;
using Spectre.Console.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiHumanPlanTests
{
	[Fact]
	public async Task HumanDryRun_ShowsActionScopeAndDestinationWithoutPromptingOrWriting()
	{
		using var test = new AiCommandFixture();
		var before = test.Snapshot();
		var original = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			var exit = await Program.BuildRootCommand().Parse(
				["ai", "add", "mcp", "maui-devflow", "--env", "CopilotCli", "--dry-run"]).InvokeAsync();
			Assert.Equal(0, exit);
		}
		finally { Console.SetOut(original); }
		var text = output.ToString();
		Assert.Contains("Action", text);
		Assert.Contains("create", text);
		Assert.Contains("user", text);
		Assert.Contains("mcp", text);
		Assert.Contains("CopilotCli", text);
		Assert.Contains(Path.Combine(test.Home, ".copilot", "mcp-config.json"), text);
		Assert.DoesNotContain("Apply the selected", text);
		Assert.Equal(1, text.Split("Scope    Kind", StringSplitOptions.None).Length - 1);
		Assert.Equal(before, test.Snapshot());
	}

	[Theory]
	[InlineData("create")]
	[InlineData("replace")]
	[InlineData("adopt")]
	[InlineData("skip")]
	public void SharedHumanPlan_ShowsEveryActionExplicitly(string action)
	{
		using var test = new AiCommandFixture();
		var asset = Asset(test, action);
		var original = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			AiCommands.WriteHumanAssetPlan([asset], ["Client exclusions are included in this exact plan."]);
		}
		finally { Console.SetOut(original); }
		Assert.Contains($"mcp    {action}", output.ToString());
		Assert.Contains("Client exclusions", output.ToString());
		Assert.Contains(asset.Path, output.ToString());
	}

	[Fact]
	public void InteractiveConfirmation_ShowsExactPlanAndCanDecline()
	{
		using var test = new AiCommandFixture();
		var before = test.Snapshot();
		var asset = Asset(test, "adopt");
		var console = new TestConsole();
		console.Interactive();
		console.Input.PushTextWithEnter("n");
		var original = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			Assert.False(AiCommands.ConfirmAssetPlan([asset], ["This registration is user-wide."], console));
		}
		finally { Console.SetOut(original); }
		Assert.Contains("Apply the selected AI assets?", console.Output);
		Assert.Contains("This registration is user-wide.", output.ToString());
		Assert.Contains("adopt", output.ToString());
		Assert.Contains("user", output.ToString());
		Assert.Contains(asset.Path, output.ToString());
		Assert.Equal(before, test.Snapshot());
	}

	private static AiAsset Asset(AiCommandFixture test, string action)
	{
		var asset = new AiAsset
		{
			Kind = AiAssetKind.Mcp, Name = "maui-devflow", Scope = "user", Action = action,
			Path = Path.Combine(test.Home, ".copilot", "mcp-config.json")
		};
		asset.Environments.Add(AgentEnvironmentKind.CopilotCli);
		return asset;
	}
}
