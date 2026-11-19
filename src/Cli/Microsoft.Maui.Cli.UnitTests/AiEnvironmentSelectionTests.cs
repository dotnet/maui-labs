using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Commands;
using Spectre.Console.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiEnvironmentSelectionTests
{
	[Theory]
	[InlineData("--ci")]
	[InlineData("--json")]
	[InlineData("--dry-run")]
	[InlineData("--yes")]
	[InlineData("redirected")]
	public async Task FirstRun_AutomationNeverPromptsOrDownloadsOrWrites(string flag)
	{
		using var test = new AiCommandFixture();
		var console = new TestConsole();
		console.Interactive();
		AiCommands.ConsoleForTests = console;
		AiCommands.InputRedirectedForTests = flag == "redirected";
		var before = test.Snapshot();
		var result = await Invoke(flag == "redirected" ? ["ai", "init"] : ["ai", "init", flag]);
		Assert.Equal(1, result.Exit);
		Assert.Contains("maui ai init --env Claude", result.Output);
		foreach (var environment in Enum.GetNames<AgentEnvironmentKind>()) Assert.Contains(environment, result.Output);
		Assert.Empty(console.Output);
		Assert.Empty(test.Handler.Requests);
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task FirstRun_InteractivePickerCanSelectMultipleClientsAndDeclineWithoutWrites()
	{
		using var test = new AiCommandFixture();
		var console = new TestConsole();
		console.Interactive();
		console.Input.PushKey(ConsoleKey.Spacebar);
		console.Input.PushKey(ConsoleKey.DownArrow);
		console.Input.PushKey(ConsoleKey.Spacebar);
		console.Input.PushKey(ConsoleKey.Enter);
		console.Input.PushTextWithEnter("n");
		AiCommands.ConsoleForTests = console;
		AiCommands.InputRedirectedForTests = false;
		var before = test.Snapshot();
		var result = await Invoke(["ai", "init", "--mcp", "maui-devflow"]);
		Assert.Equal(0, result.Exit);
		Assert.Contains("No configuration markers found", console.Output);
		Assert.Contains("Apply the selected AI assets?", console.Output);
		Assert.Contains("Claude: guided-selection", result.Output);
		Assert.Contains("VsCode: guided-selection", result.Output);
		Assert.DoesNotContain("CopilotCli: guided-selection", result.Output);
		Assert.Contains("declined", result.Output);
		Assert.Empty(test.Handler.Requests);
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task FirstRun_InteractivePickerAcceptedInstallsOnlyChosenClientsWithScopedOwnership()
	{
		using var test = new AiCommandFixture();
		var console = new TestConsole();
		console.Interactive();
		console.Input.PushKey(ConsoleKey.Spacebar);
		console.Input.PushKey(ConsoleKey.DownArrow);
		console.Input.PushKey(ConsoleKey.DownArrow);
		console.Input.PushKey(ConsoleKey.Spacebar);
		console.Input.PushKey(ConsoleKey.Enter);
		console.Input.PushTextWithEnter("y");
		AiCommands.ConsoleForTests = console;
		AiCommands.InputRedirectedForTests = false;
		var result = await Invoke(["ai", "init", "--mcp", "maui-devflow"]);
		Assert.Equal(0, result.Exit);
		Assert.Contains("No configuration markers found", console.Output);
		Assert.Contains("Apply the selected AI assets?", console.Output);
		Assert.Contains("Claude: guided-selection", result.Output);
		Assert.Contains("CopilotCli: guided-selection", result.Output);
		Assert.DoesNotContain("VsCode: guided-selection", result.Output);
		Assert.True(File.Exists(Path.Combine(test.Root, ".mcp.json")));
		Assert.True(File.Exists(Path.Combine(test.Home, ".copilot", "mcp-config.json")));
		Assert.False(Directory.Exists(Path.Combine(test.Root, ".vscode")));
		Assert.False(File.Exists(Path.Combine(test.Root, "opencode.json")));
		Assert.False(Directory.Exists(Path.Combine(test.Root, ".github")));
		var project = Assert.Single(AiAssetRegistry.Inventory(test.Root, "project"));
		Assert.Equal("project", project.Scope);
		Assert.Equal(AgentEnvironmentKind.Claude, Assert.Single(project.Environments));
		Assert.Equal(Path.Combine(test.Root, ".mcp.json"), project.Path);
		var user = Assert.Single(AiAssetRegistry.Inventory(test.Home, "user"));
		Assert.Equal("user", user.Scope);
		Assert.Equal(AgentEnvironmentKind.CopilotCli, Assert.Single(user.Environments));
		Assert.Equal(Path.Combine(test.Home, ".copilot", "mcp-config.json"), user.Path);
		Assert.Empty(test.Handler.Requests);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ExplicitAction_CiAloneOrRedirectedInputAloneAppliesWithoutPrompt(bool redirected)
	{
		using var test = new AiCommandFixture();
		var console = new TestConsole();
		console.Interactive();
		AiCommands.ConsoleForTests = console;
		AiCommands.InputRedirectedForTests = redirected;
		var arguments = new List<string> { "ai", "add", "mcp", "maui-devflow", "--env", "Claude" };
		if (!redirected) arguments.Add("--ci");
		var result = await Invoke(arguments.ToArray());
		Assert.Equal(0, result.Exit);
		Assert.Empty(console.Output);
		Assert.DoesNotContain("Apply the selected AI assets?", result.Output);
		Assert.Contains("succeeded", result.Output);
		Assert.True(File.Exists(Path.Combine(test.Root, ".mcp.json")));
		Assert.True(Assert.Single(AiAssetRegistry.Inventory(test.Root, "project")).Managed);
		Assert.Empty(test.Handler.Requests);
	}

	[Fact]
	public async Task FirstRun_EmptyPickerSelectionNeverFallsBackOrWrites()
	{
		using var test = new AiCommandFixture();
		var console = new TestConsole();
		console.Interactive();
		console.Input.PushKey(ConsoleKey.Enter);
		AiCommands.ConsoleForTests = console;
		AiCommands.InputRedirectedForTests = false;
		var before = test.Snapshot();
		var result = await Invoke(["ai", "init"]);
		Assert.Equal(1, result.Exit);
		Assert.Contains("environment-selection-cancelled", result.Output);
		Assert.Empty(test.Handler.Requests);
		Assert.Equal(before, test.Snapshot());
	}

	[Theory]
	[InlineData("add")]
	[InlineData("update")]
	[InlineData("list")]
	[InlineData("status")]
	public void NonInitCommands_NeverEnterFirstRunPicker(string operation)
	{
		Assert.False(AiCommands.ShouldPromptForEnvironments(operation, false, false, false, false, false));
	}

	[Fact]
	public async Task NestedMarker_ExplainsActualPathsAndExplicitSelectionDistinctly()
	{
		using var test = new AiCommandFixture();
		var nested = Path.Combine(test.Root, "src", "app");
		var marker = Path.Combine(nested, ".opencode");
		Directory.CreateDirectory(marker);
		Directory.SetCurrentDirectory(nested);
		var result = await test.Invoke("add", "mcp", "maui-devflow");
		Assert.Equal(0, result.Exit);
		var environment = Assert.Single(result.Result["environments"]!.AsArray())!;
		Assert.Equal("detected-marker", environment["reasonCode"]!.GetValue<string>());
		Assert.Equal(marker, environment["markerPath"]!.GetValue<string>());
		Assert.Equal(Path.Combine(nested, ".opencode", "skills"), environment["skillsPath"]!.GetValue<string>());
		Assert.Equal(Path.Combine(nested, "opencode.json"), environment["mcpPath"]!.GetValue<string>());
		Assert.True(File.Exists(Path.Combine(nested, "opencode.json")));
		Assert.False(File.Exists(Path.Combine(test.Root, "opencode.json")));
		var selected = await test.Invoke("status", "--env", "OpenCode");
		Assert.Equal("explicit-selection", Assert.Single(selected.Result["environments"]!.AsArray())!["reasonCode"]!.GetValue<string>());
	}

	[Theory]
	[InlineData(".claude", true, "Claude")]
	[InlineData(".mcp.json", false, "Claude")]
	[InlineData(".vscode", true, "VsCode")]
	[InlineData(".github/skills", true, "VsCode")]
	[InlineData(".github/agents", true, "VsCode")]
	[InlineData(".opencode", true, "OpenCode")]
	[InlineData("opencode.json", false, "OpenCode")]
	[InlineData("opencode.jsonc", false, "OpenCode")]
	public async Task EachProjectMarker_ReportsTheExactEvidenceAndReason(string relativePath, bool directory, string name)
	{
		using var test = new AiCommandFixture();
		var marker = Path.Combine(test.Root, relativePath);
		if (directory) Directory.CreateDirectory(marker);
		else File.WriteAllText(marker, "{}");
		var result = await test.Invoke("status");
		Assert.Equal(0, result.Exit);
		var environment = Assert.Single(result.Result["environments"]!.AsArray())!;
		Assert.Equal(name, environment["name"]!.GetValue<string>());
		Assert.Equal("detected-marker", environment["reasonCode"]!.GetValue<string>());
		Assert.Equal(marker, environment["markerPath"]!.GetValue<string>());
		Assert.Equal("project", environment["scope"]!.GetValue<string>());
		Assert.Empty(test.Handler.Requests);
	}

	[Fact]
	public async Task RegistryOnlyTarget_IsExplainedAndRetainsMissingConfigPath()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "mcp", "maui-devflow", "--env", "Claude")).Exit);
		File.Delete(Path.Combine(test.Root, ".mcp.json"));
		var result = await test.Invoke("status", "mcp");
		Assert.Equal(0, result.Exit);
		var environment = Assert.Single(result.Result["environments"]!.AsArray())!;
		Assert.Equal("managed-registry", environment["reasonCode"]!.GetValue<string>());
		Assert.Equal(AiAssetRegistry.RegistryPath(test.Root), environment["markerPath"]!.GetValue<string>());
		Assert.Equal(Path.Combine(test.Root, ".mcp.json"), environment["mcpPath"]!.GetValue<string>());
	}

	[Fact]
	public async Task UserCopilotMarker_IsNotPresentedAsProjectOrExecutableEvidence()
	{
		using var test = new AiCommandFixture();
		Directory.CreateDirectory(Path.Combine(test.Home, ".copilot"));
		var result = await test.Invoke("init", "--mcp", "maui-devflow", "--dry-run");
		Assert.Equal(0, result.Exit);
		var environment = Assert.Single(result.Result["environments"]!.AsArray())!;
		Assert.Equal("CopilotCli", environment["name"]!.GetValue<string>());
		Assert.Equal("user", environment["scope"]!.GetValue<string>());
		Assert.Equal("user", environment["mcpScope"]!.GetValue<string>());
		Assert.Equal(Path.Combine(test.Home, ".copilot"), environment["markerPath"]!.GetValue<string>());
		Assert.Equal(Path.Combine(test.Root, ".github", "skills"), environment["skillsPath"]!.GetValue<string>());
		var human = await Invoke(["ai", "init", "--mcp", "maui-devflow", "--dry-run"]);
		Assert.Contains("executables were not checked", human.Output);
		Assert.Contains("user-wide MCP", human.Output);
	}

	internal static async Task<(int Exit, string Output)> Invoke(string[] arguments)
	{
		var original = Console.Out;
		using var writer = new StringWriter();
		try
		{
			Console.SetOut(writer);
			return (await Program.BuildRootCommand().Parse(arguments).InvokeAsync(), writer.ToString());
		}
		finally { Console.SetOut(original); }
	}
}
