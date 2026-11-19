using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Output;
using Spectre.Console.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiHumanPlanTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RootHelp_IsBoundedAndDoesNotIncludeChildHelpDetails(bool spectre)
	{
		using var test = new AiCommandFixture();
		string output;
		if (spectre)
		{
			var console = new TestConsole();
			SpectreHelpBuilder.WriteHelp(AiCommands.Create(), console);
			output = console.Output;
		}
		else
		{
			var result = await AiEnvironmentSelectionTests.Invoke(["ai", "--help"]);
			Assert.Equal(0, result.Exit);
			output = result.Output;
		}
		Assert.InRange(output.Split('\n').Length, 10, 65);
		Assert.Equal(1, output.Split("Examples:", StringSplitOptions.None).Length - 1);
		Assert.DoesNotContain("Global flags:", output);
		Assert.DoesNotContain("[type] is optional", output);
		Assert.DoesNotContain("maui ai update skill", output);
		foreach (var command in AiCommands.Create().Subcommands)
		{
			Assert.DoesNotContain("\n", command.Description);
			Assert.DoesNotContain("Examples:", command.Description);
		}
		Assert.Empty(test.Handler.Requests);
	}

	[Fact]
	public void SpectreLeafHelp_RetainsItsOwnExamplesFlagsAndTypeGuidance()
	{
		var console = new TestConsole();
		var command = AiCommands.Create().Subcommands.Single(command => command.Name == "update");
		SpectreHelpBuilder.WriteHelp(command, console);
		Assert.Contains("maui ai update skill --skill maui-devflow-debug", console.Output);
		Assert.Contains("Global flags:", console.Output);
		Assert.Contains("[type] is optional", console.Output);
		Assert.Equal(1, console.Output.Split("Examples:", StringSplitOptions.None).Length - 1);
	}

	[Theory]
	[InlineData("init")]
	[InlineData("list")]
	[InlineData("status")]
	[InlineData("update")]
	[InlineData("add", "skill")]
	[InlineData("add", "agent")]
	[InlineData("add", "mcp")]
	public async Task CommandHelp_ContainsCopyableExamplesAndAutomationFlags(params string[] command)
	{
		using var test = new AiCommandFixture();
		var result = await AiEnvironmentSelectionTests.Invoke(["ai", .. command, "--help"]);
		Assert.Equal(0, result.Exit);
		Assert.Contains("Examples:", result.Output);
		Assert.Contains("maui ai " + string.Join(" ", command), result.Output);
		Assert.Contains("--env", result.Output);
		Assert.Contains("--json", result.Output);
		Assert.Contains("--ci", result.Output);
		Assert.Contains("--dry-run", result.Output);
		if (command[0] is "list" or "status" or "update")
			Assert.Contains("[type] is optional", result.Output);
		var examples = result.Output.Split('\n').Select(line => line.Trim())
			.Where(line => line.StartsWith("maui ai ", StringComparison.Ordinal)).ToArray();
		Assert.NotEmpty(examples);
		foreach (var example in examples)
			Assert.Empty(Program.BuildRootCommand().Parse(example["maui ".Length..]).Errors);
		Assert.Empty(test.Handler.Requests);
	}

	[Fact]
	public async Task HumanCatalog_LabelsRecommendationsWithoutSelectingEveryAsset()
	{
		using var test = new AiCommandFixture();
		var result = await AiEnvironmentSelectionTests.Invoke(["ai", "list", "--env", "VsCode"]);
		Assert.Equal(0, result.Exit);
		Assert.Contains("maui-devflow-debug (recommended)", result.Output);
		Assert.Contains("maui-devflow (recommended)", result.Output);
		Assert.Contains("custom-skill", result.Output);
		Assert.DoesNotContain("custom-skill (recommended)", result.Output);
	}

	[Fact]
	public async Task HumanYes_PrintsSelectionAndCompletePlanBeforeAnyWrite()
	{
		using var test = new AiCommandFixture();
		var console = new TestConsole();
		console.Interactive();
		AiCommands.ConsoleForTests = console;
		AiCommands.InputRedirectedForTests = false;
		var before = test.Snapshot();
		var original = Console.Out;
		using var output = new BeforeWriteObserver(line =>
		{
			if (line?.StartsWith("user ", StringComparison.Ordinal) == true)
				Assert.Equal(before, test.Snapshot());
		});
		try
		{
			Console.SetOut(output);
			Assert.Equal(0, await Program.BuildRootCommand().Parse(
				["ai", "add", "mcp", "maui-devflow", "--env", "CopilotCli", "--yes"]).InvokeAsync());
		}
		finally { Console.SetOut(original); }
		Assert.Contains("explicit-selection", output.ToString());
		Assert.Contains("user-wide MCP", output.ToString());
		Assert.Contains("user     mcp    create", output.ToString());
		Assert.True(File.Exists(Path.Combine(test.Home, ".copilot", "mcp-config.json")));
		Assert.Empty(console.Output);
	}

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

	private sealed class BeforeWriteObserver(Action<string?> observe) : StringWriter
	{
		public override void WriteLine(string? value)
		{
			observe(value);
			base.WriteLine(value);
		}
	}
}
