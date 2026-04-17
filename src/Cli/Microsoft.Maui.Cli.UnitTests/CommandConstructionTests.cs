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
}
