using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;

namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

public sealed class CliTestHarness
{
    private static readonly FieldInfo? s_errorOccurredField =
        typeof(DevFlowCommands).GetField("_errorOccurred", BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_agentLabelEmittedField =
        typeof(DevFlowCommands).GetField("_agentLabelEmitted", BindingFlags.Static | BindingFlags.NonPublic);

    private readonly RootCommand _rootCommand;
    private readonly int _mockAgentPort;

    public CliTestHarness(int mockAgentPort)
    {
        _mockAgentPort = mockAgentPort;
        Program.ResetServices();
        _rootCommand = Program.BuildRootCommand();
    }

    public Task<CliResult> InvokeAsync(params string[] args) =>
        InvokeRawAsync(
            [
                .. args,
                "--agent-host", "localhost",
                "--agent-port", _mockAgentPort.ToString()
            ]);

    public async Task<CliResult> InvokeRawAsync(params string[] args)
    {
        var stdOut = new StringWriter();
        var stdErr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        int exitCode;

        try
        {
            Console.SetOut(stdOut);
            Console.SetError(stdErr);
            s_errorOccurredField?.SetValue(null, false);
            s_agentLabelEmittedField?.SetValue(null, false);

            var parseResult = _rootCommand.Parse(args);

            try
            {
                exitCode = await parseResult.InvokeAsync();
            }
            catch (Exception ex)
            {
                // Mirror Program.Main: command handlers may let exceptions (e.g. the
                // multi-agent refusal) propagate; surface the message and fail the command.
                Console.Error.WriteLine(ex.Message);
                exitCode = 1;
            }

            if (s_errorOccurredField?.GetValue(null) is true)
                exitCode = 1;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        return new CliResult
        {
            ExitCode = exitCode,
            StdOut = stdOut.ToString().TrimEnd(),
            StdErr = stdErr.ToString().TrimEnd()
        };
    }
}

public sealed class CliResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;

    public JsonElement ParseJsonOutput()
    {
        using var document = JsonDocument.Parse(StdOut);
        return document.RootElement.Clone();
    }
}
