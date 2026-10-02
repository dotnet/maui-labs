using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace Microsoft.Maui.Cli.Output;

internal sealed class DetailedHelpCommand : Command
{
	internal string HelpDetails { get; set; } = "";

	internal DetailedHelpCommand(string name, string description) : base(name, description)
	{
		Add(new HelpOption { Action = new DetailedHelpAction() });
	}

	private sealed class DetailedHelpAction : SynchronousCommandLineAction
	{
		public override int Invoke(ParseResult parseResult)
		{
			var exitCode = new HelpAction().Invoke(parseResult);
			if (parseResult.CommandResult.Command is DetailedHelpCommand { HelpDetails.Length: > 0 } command)
				parseResult.InvocationConfiguration.Output.WriteLine(command.HelpDetails);
			return exitCode;
		}
	}
}
