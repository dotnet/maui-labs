using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Skills;
using Spectre.Console.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class DevFlowSkillOutputTests
{
    [Fact]
    public void PrintInitSummary_EmphasizesNextPromptWithoutRawMarkup()
    {
        var console = new TestConsole();
        var result = new JsonObject
        {
            ["results"] = new JsonArray
            {
                new JsonObject
                {
                    ["skillId"] = "maui-devflow-onboard",
                    ["action"] = "written",
                    ["status"] = "up-to-date",
                    ["path"] = ".claude/skills/maui-devflow-onboard",
                    ["message"] = "Installed missing skill files from the current CLI bundle."
                }
            }
        };

        DevFlowSkillCommands.PrintInitSummary(result, console);

        var output = console.Output;
        Assert.Contains("MAUI DevFlow skills initialized", output);
        Assert.Contains("Next prompt for your AI agent", output);
        Assert.Contains("maui-devflow-onboard", output);
        Assert.Contains("maui-devflow-debug", output);
        Assert.Contains("maui devflow ui tree --depth 1", output);
        Assert.DoesNotContain("[yellow]", output);
        Assert.DoesNotContain("[/]", output);
    }
}
