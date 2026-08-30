using System.Text.Json;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class JsonParseErrorTests
{
    [Fact]
    public async Task MissingRequiredArgument_InJsonMode_ReturnsStructuredJson()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = await Program.Main(
                ["devflow", "flow", "replay", "--json"]);

            Assert.Equal(1, exitCode);
            using var error = JsonDocument.Parse(stderr.ToString());
            Assert.Contains(
                "Required argument missing",
                error.RootElement.ToString(),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Usage:", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
