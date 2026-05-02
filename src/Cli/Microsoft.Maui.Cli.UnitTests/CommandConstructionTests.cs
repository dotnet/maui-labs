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
