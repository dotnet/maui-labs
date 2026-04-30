// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

public static class ErrorsCommands
{
	public static Command Create()
	{
		var command = new Command("errors", "Error code catalogue and diagnostics");
		command.Add(CreateListCommand());
		return command;
	}

	static Command CreateListCommand()
	{
		var categoryOption = new Option<string?>("--category") { Description = "Filter by category: 'tool' or 'platform'" };
		var prefixOption = new Option<string?>("--prefix") { Description = "Filter by code prefix, e.g. 'E21' for Android errors" };
		var listCommand = new Command("list", "List all registered error codes") { categoryOption, prefixOption };

		listCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var category = parseResult.GetValue(categoryOption);
			var prefix = parseResult.GetValue(prefixOption);

			IReadOnlyList<ErrorCodeDescriptor> codes = ErrorCodeCatalogue.All;
			if (category is not null)
				codes = ErrorCodeCatalogue.ByCategory(category);
			if (prefix is not null)
				codes = [.. codes.Where(d => d.Code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];

			if (useJson)
			{
				formatter.Write(new ErrorCodeCatalogueResult { Codes = codes });
			}
			else
			{
				if (!codes.Any())
				{
					formatter.WriteWarning("No error codes match the specified filter.");
					return 0;
				}
				if (formatter is SpectreOutputFormatter spectre)
				{
					spectre.WriteTable(codes,
						("Code", d => d.Code),
						("Name", d => d.Name),
						("Category", d => d.Subcategory is null ? d.Category : $"{d.Category}/{d.Subcategory}"),
						("Description", d => d.Description));
				}
			}
			return 0;
		});
		return listCommand;
	}
}
