using System.CommandLine;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.DevFlow;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class CommandConstructionTests
{
	[Fact]
	public void BuildRootCommand_DoesNotThrow()
	{
		// Verifies all commands and options can be constructed without errors.
		// Catches issues like descriptions passed as aliases (which throws
		// ArgumentException for whitespace in alias names).
		var rootCommand = Program.BuildRootCommand();

		Assert.NotNull(rootCommand);
		Assert.NotEmpty(rootCommand.Subcommands);
	}

	[Fact]
	public void RootCommand_IncludesProjectVersionCommands()
	{
		var rootCommand = Program.BuildRootCommand();

		var projectCommand = Assert.Single(rootCommand.Subcommands, command => command.Name == "project");
		var versionCommand = Assert.Single(projectCommand.Subcommands, command => command.Name == "version");
		Assert.Contains(versionCommand.Subcommands, command => command.Name == "show");
		Assert.Contains(versionCommand.Subcommands, command => command.Name == "list");
		Assert.Contains(versionCommand.Subcommands, command => command.Name == "set");
		Assert.Contains(versionCommand.Subcommands, command => command.Name == "use-workload");
		var setCommand = Assert.Single(versionCommand.Subcommands, command => command.Name == "set");
		Assert.Contains(setCommand.Options, option => option.Name == "--pr");
		var hivePathOption = Assert.Single(setCommand.Options, option => option.Name == "--hive-path");
		Assert.Contains("--artifact-path", hivePathOption.Aliases);
		var frameworkOption = Assert.Single(setCommand.Options, option => option.Name == "--target-framework");
		Assert.Contains("--framework", frameworkOption.Aliases);
		Assert.Contains("-f", frameworkOption.Aliases);

		var showCommand = Assert.Single(versionCommand.Subcommands, command => command.Name == "show");
		Assert.Contains("check", showCommand.Aliases);
	}

	[Fact]
	public void VersionCommand_RemainsCliVersionCommand()
	{
		var rootCommand = Program.BuildRootCommand();

		var versionCommand = Assert.Single(rootCommand.Subcommands, command => command.Name == "version");
		Assert.Empty(versionCommand.Subcommands);
	}

	[Theory]
	[InlineData("--pr", "1234")]
	[InlineData("--pull-request", "1234")]
	[InlineData("--pr", "https://github.com/dotnet/maui/pull/1234")]
	[InlineData("--pull-request", "https://github.com/dotnet/maui/pull/1234/")]
	[InlineData("--pr", "https://github.com/dotnet/maui/pull/1234?diff=split#issuecomment-1")]
	[InlineData("--pr", "https://GITHUB.COM/DOTNET/MAUI/pull/1234")]
	public void ProjectVersionSet_PullRequestNumberOrUrl_ParsesNumber(string alias, string input)
	{
		var root = Program.BuildRootCommand();
		var project = Assert.Single(root.Subcommands, command => command.Name == "project");
		var version = Assert.Single(project.Subcommands, command => command.Name == "version");
		var set = Assert.Single(version.Subcommands, command => command.Name == "set");
		var option = Assert.IsType<Option<int?>>(Assert.Single(set.Options, option => option.Name == "--pr"));

		var result = root.Parse(["project", "version", "set", alias, input]);

		Assert.Empty(result.Errors);
		Assert.Equal(1234, result.GetValue(option));
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("2147483648")]
	[InlineData("abc")]
	[InlineData("https://github.com/dotnet/maui/pull/0")]
	[InlineData("https://github.com/dotnet/maui/pull/2147483648")]
	[InlineData("https://github.com/dotnet/maui-labs/pull/1234")]
	[InlineData("https://github.com/dotnet/maui/issues/1234")]
	[InlineData("https://github.com.example.com/dotnet/maui/pull/1234")]
	[InlineData("http://github.com/dotnet/maui/pull/1234")]
	[InlineData("https://github.com/dotnet/maui/pull/1234/files")]
	public void ProjectVersionSet_InvalidPullRequest_FailsDuringParsing(string input)
	{
		var result = Program.BuildRootCommand().Parse(["project", "version", "set", "--pr", input]);

		Assert.Contains(result.Errors, error => error.Message.Contains("--pr must be a positive pull request number", StringComparison.Ordinal));
	}

	[Fact]
	public void ProjectVersionSet_MissingPullRequest_FailsDuringParsing()
	{
		var result = Program.BuildRootCommand().Parse("project version set --pr");

		Assert.NotEmpty(result.Errors);
	}

	[Fact]
	public void RootCommand_IncludesSelfUpdate()
	{
		var command = Assert.Single(Program.BuildRootCommand().Subcommands, command => command.Name == "update");
		Assert.Contains(command.Options, option => option.Name == "--self");
		Assert.Contains(command.Options, option => option.Name == "--prerelease");
		Assert.Empty(command.Subcommands);
	}

	[Fact]
	public void DevFlowCommand_AllOptionsHaveValidAliases()
	{
		var jsonOption = new Option<bool>("--json");
		var devflowCommand = DevFlowCommands.CreateDevFlowCommand(jsonOption);

		// Recursively verify every option in the command tree has no whitespace in aliases
		AssertNoWhitespaceAliases(devflowCommand);
	}

	[Fact]
	public void DevFlowCommand_UsesMcpAsPrimaryCommandName()
	{
		var jsonOption = new Option<bool>("--json");
		var devflowCommand = DevFlowCommands.CreateDevFlowCommand(jsonOption);

		var mcpCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "mcp");
		Assert.Contains("mcp-serve", mcpCommand.Aliases);
	}

	[Fact]
	public void DevFlowCommand_IncludesInitAndSkillsCommands()
	{
		var jsonOption = new Option<bool>("--json");
		var devflowCommand = DevFlowCommands.CreateDevFlowCommand(jsonOption);

		var initCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "init");
		Assert.Contains("onboard", initCommand.Aliases);

		var skillsCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "skills");
		Assert.Contains(skillsCommand.Subcommands, c => c.Name == "install");
		Assert.Contains(skillsCommand.Subcommands, c => c.Name == "list");
		Assert.Contains(skillsCommand.Subcommands, c => c.Name == "check");
		Assert.Contains(skillsCommand.Subcommands, c => c.Name == "update");
		Assert.Contains(skillsCommand.Subcommands, c => c.Name == "remove");
		Assert.Contains(skillsCommand.Subcommands, c => c.Name == "doctor");
	}

	[Fact]
	public void DevFlowCommand_IncludesThemeCommands()
	{
		var jsonOption = new Option<bool>("--json");
		var devflowCommand = DevFlowCommands.CreateDevFlowCommand(jsonOption);

		var themeCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "theme");
		Assert.Contains(themeCommand.Subcommands, c => c.Name == "get");
		var setCommand = Assert.Single(themeCommand.Subcommands, c => c.Name == "set");
		var scopeOption = (Option<string>)Assert.Single(setCommand.Options, option => option.Name == "--scope");
		var parseResult = themeCommand.Parse("set dark");

		Assert.Empty(parseResult.Errors);
		Assert.Equal("auto", parseResult.GetValue(scopeOption));
	}

	[Fact]
	public void DevFlowCommand_IncludesLayoutDiagnosticsCommand()
	{
		var jsonOption = new Option<bool>("--json");
		var devflowCommand = DevFlowCommands.CreateDevFlowCommand(jsonOption);

		var uiCommand = Assert.Single(devflowCommand.Subcommands, command => command.Name == "ui");
		var diagnosticsCommand = Assert.Single(uiCommand.Subcommands, command => command.Name == "diagnostics");

		Assert.Contains(diagnosticsCommand.Options, option => option.Name == "--profile");
		Assert.Contains(diagnosticsCommand.Options, option => option.Name == "--root");
		Assert.Contains(diagnosticsCommand.Options, option => option.Name == "--checks");
		Assert.Contains(diagnosticsCommand.Options, option => option.Name == "--watch");
		Assert.Contains(diagnosticsCommand.Options, option => option.Name == "--fail-on");
		Assert.Empty(diagnosticsCommand.Parse("diagnostics --profile strict --checks layout.element-clipped").Errors);
	}

	[Fact]
	public void LayoutDiagnostics_CiProfileDefaultsToSeriousFailureThreshold()
	{
		Assert.Equal(
			"serious",
			DevFlowCommands.ResolveLayoutDiagnosticsFailOn("ci", failOn: null));
		Assert.Equal(
			"critical",
			DevFlowCommands.ResolveLayoutDiagnosticsFailOn("ci", "critical"));
		Assert.Null(DevFlowCommands.ResolveLayoutDiagnosticsFailOn("agent", failOn: null));
	}

	[Fact]
	public void LayoutDiagnostics_CiProfileFailsIncompleteScans()
	{
		var result = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionResult
		{
			Summary = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionSummary
			{
				Incomplete = 1
			}
		};

		Assert.True(DevFlowCommands.ShouldFailLayoutDiagnostics(
			result,
			"serious",
			failOnIncomplete: true));
		Assert.False(DevFlowCommands.ShouldFailLayoutDiagnostics(
			result,
			"serious",
			failOnIncomplete: false));
	}

	[Fact]
	public void DevFlowRequestedExitCode_OverridesGenericErrorExit()
	{
		try
		{
			DevFlowCommands.RequestExitCode(130);
			Assert.Equal(130, Program.ResolveDevFlowExitCode(0));
		}
		finally
		{
			DevFlowCommands.ResetBrokerClientForTests();
		}
	}

	[Fact]
	public void DevFlowCommand_UpdateSkillIsHiddenCompatibilityAliasForSkillsUpdate()
	{
		var jsonOption = new Option<bool>("--json");
		var devflowCommand = DevFlowCommands.CreateDevFlowCommand(jsonOption);

		var updateSkillCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "update-skill");
		Assert.True(updateSkillCommand.Hidden);
		Assert.Contains(updateSkillCommand.Options, option => option.Name == "--scope");
		Assert.Contains(updateSkillCommand.Options, option => option.Name == "--target");
		Assert.Contains(updateSkillCommand.Options, option => option.Name == "--path");
		Assert.Contains(updateSkillCommand.Options, option => option.Name == "--force");
		Assert.Contains(updateSkillCommand.Options, option => option.Name == "--allow-downgrade");
		Assert.Contains(updateSkillCommand.Options, option => option.Name == "--interactive");
		Assert.DoesNotContain(updateSkillCommand.Options, option => option.Name == "--branch");
		Assert.DoesNotContain(updateSkillCommand.Options, option => option.Name == "--output");
	}

	[Fact]
	public void DevFlowCommand_TargetOptionsDefaultToAuto()
	{
		var jsonOption = new Option<bool>("--json");
		var devflowCommand = DevFlowCommands.CreateDevFlowCommand(jsonOption);

		var initCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "init");
		AssertTargetOptionDefault(initCommand, "init");

		var skillsCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "skills");
		AssertTargetOptionDefault(Assert.Single(skillsCommand.Subcommands, c => c.Name == "install"), "install");
		AssertTargetOptionDefault(Assert.Single(skillsCommand.Subcommands, c => c.Name == "list"), "list");
		AssertTargetOptionDefault(Assert.Single(skillsCommand.Subcommands, c => c.Name == "check"), "check");
		AssertTargetOptionDefault(Assert.Single(skillsCommand.Subcommands, c => c.Name == "update"), "update");
		AssertTargetOptionDefault(Assert.Single(skillsCommand.Subcommands, c => c.Name == "doctor"), "doctor");
		AssertTargetOptionDefault(Assert.Single(skillsCommand.Subcommands, c => c.Name == "remove"), "remove maui-devflow-onboard");

		AssertTargetOptionDefault(Assert.Single(devflowCommand.Subcommands, c => c.Name == "update-skill"), "update-skill");
	}

	[Fact]
	public void DevFlowCommand_InvalidSkillScopeAndTargetFailDuringParsing()
	{
		var jsonOption = new Option<bool>("--json");
		var devflowCommand = DevFlowCommands.CreateDevFlowCommand(jsonOption);
		var initCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "init");
		var skillsCommand = Assert.Single(devflowCommand.Subcommands, c => c.Name == "skills");
		var updateCommand = Assert.Single(skillsCommand.Subcommands, c => c.Name == "update");

		Assert.NotEmpty(initCommand.Parse("init --target bogus").Errors);
		Assert.NotEmpty(initCommand.Parse("init --scope all").Errors);
		Assert.Empty(updateCommand.Parse("update --scope all").Errors);
		Assert.NotEmpty(updateCommand.Parse("update --scope bogus").Errors);
	}

	private static void AssertNoWhitespaceAliases(Command command)
	{
		foreach (var option in command.Options)
		{
			Assert.False(option.Name.Any(char.IsWhiteSpace), $"Option name contains whitespace: \"{option.Name}\" in command '{command.Name}'");
			foreach (var alias in option.Aliases)
			{
				Assert.False(alias.Any(char.IsWhiteSpace), $"Option alias contains whitespace: \"{alias}\" in command '{command.Name}'");
			}
		}

		foreach (var subcommand in command.Subcommands)
		{
			AssertNoWhitespaceAliases(subcommand);
		}
	}

	private static void AssertTargetOptionDefault(Command command, string commandLine)
	{
		var targetOption = (Option<string>)Assert.Single(command.Options, option => option.Name == "--target");
		var parseResult = command.Parse(commandLine);

		Assert.Empty(parseResult.Errors);
		Assert.Equal("auto", parseResult.GetValue(targetOption));
	}
}
