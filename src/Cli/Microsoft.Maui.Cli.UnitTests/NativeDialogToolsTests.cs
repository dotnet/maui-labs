using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class NativeDialogToolsTests
{
    [Fact]
    public async Task Respond_WithoutUserConfirmation_RequiresUserAction()
    {
        var json = await NativeDialogTools.Respond(
            null!,
            "reviewed-prompt",
            "Allow",
            confirmedByUser: false);
        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("userActionRequired").GetBoolean());
        Assert.Contains(
            "confirm",
            document.RootElement.GetProperty("instruction").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
