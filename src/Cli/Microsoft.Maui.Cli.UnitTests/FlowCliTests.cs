using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class FlowCliTests
{
    [Fact]
    public async Task FlowValidate_EmptyFlow_ReturnsFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"empty-flow-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(
            path,
            """
            # Scenario: empty

            ```json maui-test
            {
              "schema": 2,
              "name": "empty",
              "steps": []
            }
            ```
            """);
        try
        {
            var result = await new CliTestHarness(1).InvokeAsync(
                "devflow",
                "flow",
                "validate",
                path,
                "--json");

            Assert.NotEqual(0, result.ExitCode);
            var json = result.ParseJsonOutput();
            Assert.False(json.GetProperty("ok").GetBoolean());
            Assert.Contains(
                json.GetProperty("errors").EnumerateArray(),
                error => error.GetString()?.Contains(
                    "at least one step",
                    StringComparison.OrdinalIgnoreCase) == true);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
