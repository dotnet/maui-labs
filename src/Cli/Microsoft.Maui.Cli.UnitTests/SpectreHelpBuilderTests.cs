// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.Maui.Cli.Output;
using Spectre.Console.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class SpectreHelpBuilderTests
{
	[Fact]
	public void WriteHelp_SingleAliasOption_RendersNonEmptyLabel()
	{
		// Options like --json that have no secondary alias should still render with their name
		var command = new Command("test", "A test command");
		command.Options.Add(new Option<bool>("--json") { Description = "Output as JSON" });

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		Assert.Contains("--json", output);
		Assert.Contains("Output as JSON", output);
	}

	[Fact]
	public void WriteHelp_MultiAliasOption_ShowsShortBeforeLong()
	{
		var command = new Command("test", "A test command");
		var option = new Option<bool>("--verbose", "-v") { Description = "Enable verbose output" };
		command.Options.Add(option);

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		// Short alias (-v) should appear before long alias (--verbose)
		var shortIdx = output.IndexOf("-v");
		var longIdx = output.IndexOf("--verbose");
		Assert.True(shortIdx < longIdx, "Short alias should appear before long alias");
	}

	[Fact]
	public void WriteHelp_CommandWithNoUserOptions_StillShowsOptionsSection()
	{
		// Even a bare command with zero user-defined options should show the Options section
		// because --help is always available.
		var command = new Command("bare", "A command with no options");

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		Assert.Contains("Options:", output);
		Assert.Contains("--help", output);
	}

	[Fact]
	public void WriteHelp_RootCommand_ShowsVersionOption()
	{
		var command = new RootCommand("Root command");

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		Assert.Contains("--version", output);
		Assert.Contains("Show version information", output);
	}

	[Fact]
	public void WriteHelp_NonRootCommand_DoesNotShowVersionOption()
	{
		var command = new Command("child", "A child command");

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		Assert.DoesNotContain("--version", output);
	}

	[Fact]
	public void WriteHelp_ColumnsStaySeparated_ForLongestNameRow()
	{
		// Verifies that name and description columns don't concatenate,
		// even for the row with the longest option name.
		var command = new Command("test", "A test command");
		command.Options.Add(new Option<string>("--very-long-option-name") { Description = "Long option description" });
		command.Options.Add(new Option<bool>("--short") { Description = "Short description" });

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		var lines = output.Split('\n');

		// Find the line with the longest option
		var longLine = lines.FirstOrDefault(l => l.Contains("--very-long-option-name"));
		Assert.NotNull(longLine);

		// There should be whitespace between the option name and the description
		var nameEnd = longLine.IndexOf("--very-long-option-name") + "--very-long-option-name".Length;
		var descStart = longLine.IndexOf("Long option description");
		Assert.True(descStart > nameEnd, "Description should be separated from option name by whitespace");
	}

	[Fact]
	public void WriteHelp_UsageLine_AlwaysIncludesOptions()
	{
		// Even a command with no user options should show [options] in usage line
		var command = new Command("bare", "A bare command");

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		// The usage line should contain [options]
		var lines = output.Split('\n');
		var usageLine = lines.FirstOrDefault(l => l.Contains("bare") && l.Contains("[options]"));
		Assert.NotNull(usageLine);
	}

	[Fact]
	public void WriteHelp_BuiltInHelpOption_NotDuplicated()
	{
		// System.CommandLine injects HelpOption automatically.
		// We filter it and render manually — verify no duplication.
		var command = new Command("test", "A test command");
		command.Options.Add(new Option<bool>("--json") { Description = "JSON output" });

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		// Count occurrences of "--help" — should appear exactly once
		var count = output.Split("--help").Length - 1;
		Assert.Equal(1, count);
	}

	[Fact]
	public void WriteHelp_SubcommandsRendered_InAlphabeticalOrder()
	{
		var parent = new Command("parent", "Parent command");
		parent.Subcommands.Add(new Command("zebra", "Last alphabetically"));
		parent.Subcommands.Add(new Command("alpha", "First alphabetically"));
		parent.Subcommands.Add(new Command("middle", "In between"));

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(parent, console);

		var output = console.Output;
		var alphaIdx = output.IndexOf("alpha");
		var middleIdx = output.IndexOf("middle");
		var zebraIdx = output.IndexOf("zebra");
		Assert.True(alphaIdx < middleIdx && middleIdx < zebraIdx,
			"Subcommands should be rendered in alphabetical order");
	}

	[Fact]
	public void WriteHelp_ArgumentsRendered_BeforeOptions()
	{
		var command = new Command("test", "A test command");
		command.Arguments.Add(new Argument<string>("path") { Description = "The file path" });
		command.Options.Add(new Option<bool>("--force") { Description = "Force the operation" });

		var console = new TestConsole();
		SpectreHelpBuilder.WriteHelp(command, console);

		var output = console.Output;
		var argsIdx = output.IndexOf("Arguments:");
		var optsIdx = output.IndexOf("Options:");
		Assert.True(argsIdx < optsIdx, "Arguments section should appear before Options section");
	}
}
