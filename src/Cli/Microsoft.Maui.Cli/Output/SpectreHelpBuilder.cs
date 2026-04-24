// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Output;

/// <summary>
/// Custom help rendering using Spectre.Console for colorized output.
/// Produces styled help matching the tool's visual identity.
/// </summary>
static class SpectreHelpBuilder
{
	/// <summary>
	/// Writes colorized help for a command to the console.
	/// </summary>
	internal static void WriteHelp(Command command) => WriteHelp(command, AnsiConsole.Console);

	/// <summary>
	/// Writes colorized help for a command to the specified console (testable overload).
	/// </summary>
	internal static void WriteHelp(Command command, IAnsiConsole console)
	{
		// Description
		if (!string.IsNullOrEmpty(command.Description))
		{
			console.MarkupLine("[yellow]Description:[/]");
			console.MarkupLine($"  {Markup.Escape(command.Description)}");
			console.WriteLine();
		}

		// Usage — always shows [options] since help is always available
		console.MarkupLine("[yellow]Usage:[/]");
		console.MarkupLine($"  {Markup.Escape(BuildUsageLine(command))}");
		console.WriteLine();

		// Arguments (render before options, like dotnet CLI convention)
		var arguments = command.Arguments.Where(a => !a.Hidden).ToList();
		if (arguments.Count > 0)
		{
			console.MarkupLine("[yellow]Arguments:[/]");
			WriteTwoColumnTable(console, arguments.Select(a =>
				($"<{a.Name}>", a.Description ?? string.Empty)));
			console.WriteLine();
		}

		// Options — build all rows first, including standard help/version,
		// so the section always appears (every command has at least --help).
		var options = GetVisibleOptions(command).ToList();
		var optionRows = new List<(string, string)>();
		foreach (var option in options)
			optionRows.Add((FormatAliases(option), option.Description ?? string.Empty));

		// Standard help option (always available on every command)
		optionRows.Add(("-?, -h, --help", "Show help and usage information"));

		// Standard version option (root only)
		if (command is RootCommand)
			optionRows.Add(("--version", "Show version information"));

		if (optionRows.Count > 0)
		{
			console.MarkupLine("[yellow]Options:[/]");
			WriteTwoColumnTable(console, optionRows);
			console.WriteLine();
		}

		// Subcommands
		var subcommands = command.Subcommands.Where(c => !c.Hidden).OrderBy(c => c.Name).ToList();
		if (subcommands.Count > 0)
		{
			console.MarkupLine("[yellow]Commands:[/]");
			WriteTwoColumnTable(console, subcommands.Select(s =>
				(s.Name, s.Description ?? string.Empty)));
			console.WriteLine();
		}
	}

	/// <summary>
	/// Renders a two-column table (name | description) with consistent alignment.
	/// Uses explicit padding in a single markup line per row so output stays stable
	/// whether stdout is a terminal or captured/piped.
	/// </summary>
	static void WriteTwoColumnTable(IAnsiConsole console, IEnumerable<(string Name, string Description)> rows)
	{
		var materialized = rows.Where(r => !string.IsNullOrEmpty(r.Name)).ToList();
		if (materialized.Count == 0)
			return;

		var maxNameLen = materialized.Max(r => r.Name.Length);
		foreach (var (name, description) in materialized)
		{
			// Pad the name to maxNameLen + 2 (column separator) INSIDE the color segment.
			// Spectre collapses whitespace immediately after a closing markup tag,
			// so padding must be inside [green]...[/] to preserve column alignment.
			var paddedName = name.PadRight(maxNameLen + 2);
			console.MarkupLine($"  [green]{Markup.Escape(paddedName)}[/]{Markup.Escape(description)}");
		}
	}

	static string BuildUsageLine(Command command)
	{
		var parts = new List<string>();

		// Walk up to build the full command path
		var current = command;
		var path = new Stack<string>();
		while (current != null)
		{
			if (!string.IsNullOrEmpty(current.Name))
				path.Push(current.Name);
			current = current.Parents.OfType<Command>().FirstOrDefault();
		}
		parts.Add(string.Join(" ", path));

		if (command.Subcommands.Any(c => !c.Hidden))
			parts.Add("[command]");

		// Every command has at least --help, so [options] always applies
		parts.Add("[options]");

		foreach (var arg in command.Arguments.Where(a => !a.Hidden))
			parts.Add($"<{arg.Name}>");

		return string.Join(" ", parts);
	}

	static IEnumerable<Option> GetVisibleOptions(Command command)
	{
		// Filter out the built-in HelpOption / VersionOption — we render those manually
		// so their presentation stays consistent across all commands.
		// Match by well-known aliases instead of internal type names, which are fragile
		// across System.CommandLine beta releases.
		static bool IsBuiltIn(Option o) =>
			o.Aliases.Contains("--help") || o.Name == "--help" ||
			o.Aliases.Contains("--version") || o.Name == "--version";

		var options = command.Options.Where(o => !o.Hidden && !IsBuiltIn(o)).ToList();

		// Walk all ancestor commands to collect inherited/recursive options
		foreach (var ancestor in command.Parents.OfType<Command>())
		{
			foreach (var globalOpt in ancestor.Options.Where(o => !o.Hidden && !IsBuiltIn(o) && o.Recursive))
			{
				if (!options.Any(o => o.Name == globalOpt.Name))
					options.Add(globalOpt);
			}
		}

		return options;
	}

	static string FormatAliases(Option option)
	{
		// Option.Aliases in System.CommandLine 2.0 beta5+ does NOT include the primary Name,
		// so we must prepend it explicitly to avoid producing a blank label.
		var names = new List<string>();
		if (!string.IsNullOrEmpty(option.Name))
			names.Add(option.Name);
		foreach (var alias in option.Aliases)
		{
			if (!string.IsNullOrEmpty(alias) && !names.Contains(alias, StringComparer.Ordinal))
				names.Add(alias);
		}
		// Show short aliases (e.g. "-v") before long ones (e.g. "--verbose") for readability.
		names.Sort((a, b) => a.Length.CompareTo(b.Length));
		return string.Join(", ", names);
	}
}
