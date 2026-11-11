using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class DevFlowFoldCommandTests
{
    [Theory]
    [InlineData("fold")]
    [InlineData("hinge")]
    public async Task Help_AdvertisesHostOperationsAndOptIn(string command)
    {
        var result = await new CliTestHarness(1).InvokeRawAsync("devflow", "device", command, "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("capabilities", result.StdOut);
        Assert.Contains("--experimental", result.StdOut);
        Assert.Contains("--udid", result.StdOut);
    }

    [Theory]
    [InlineData("capabilities")]
    [InlineData("get")]
    public async Task Read_MissingTarget_FailsWithoutAgentOrNativeProbe(string command)
    {
        var result = await new CliTestHarness(1).InvokeRawAsync("devflow", "device", "fold", command, "--json");
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdErr);
        Assert.Equal("InvalidArgument", json.RootElement.GetProperty("type").GetString());
        Assert.Contains("exactly one", json.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("partially-folded", "--angle", "NaN")]
    [InlineData("folded", "--angle", "90")]
    [InlineData("unfolded", "--duration", "-1")]
    [InlineData("partially-folded", "--timeout", "0")]
    public async Task Set_InvalidInput_IsStructuredErrorBeforeNativeIo(string mode, string option, string value)
    {
        var result = await new CliTestHarness(1).InvokeRawAsync(
            "devflow", "device", "fold", "set", mode, "--device", "emulator-19554", option, value, "--json");
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdErr);
        Assert.Equal("InvalidArgument", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void Target_BothPlatforms_IsRejected()
        => Assert.Throws<ArgumentException>(() => FoldOperations.CreateDevice(
            "emulator-5554", "BEF095D0-5BDD-4CCB-B08B-C0BA630AEE72", false));

    [Fact]
    public async Task Set_DryRun_ValidatesAndPreviewsWithoutDeviceAccess()
    {
        var result = await new CliTestHarness(1).InvokeRawAsync(
            "devflow", "device", "fold", "set", "partially-folded", "--angle", "110",
            "--device", "emulator-19554", "--dry-run", "--json");
        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.True(json.GetProperty("dryRun").GetBoolean());
        Assert.Equal(110, json.GetProperty("requestedAngleDegrees").GetDouble());
    }

    [Fact]
    public async Task Set_DryRun_InvalidAngleStillFails()
    {
        var result = await new CliTestHarness(1).InvokeRawAsync(
            "devflow", "device", "fold", "set", "partially-folded", "--angle", "NaN",
            "--device", "emulator-19554", "--dry-run", "--json");
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Mcp_MissingTarget_IsAnActionableToolError()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => FoldTools.GetState(new McpAgentSession()));
        Assert.Contains("exactly one", error.Message);
    }

    [Fact]
    public async Task Mcp_InvalidAngle_IsRejectedWithoutAnAgent()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => FoldTools.SetState(
            new McpAgentSession(), "partially-folded", androidDevice: "emulator-19554", angleDegrees: double.NaN));
        Assert.Contains("finite", error.Message);
    }

    [Fact]
    public void JsonContext_CoversHostResultsWithoutReflection()
    {
        var json = CliJson.SerializeUntyped(new FoldSetResult
        {
            RequestedMode = FoldMode.PartiallyFolded, RequestedAngleDegrees = 110,
            State = new FoldState { Mode = FoldMode.PartiallyFolded, Hinges = [new FoldHinge { AngleDegrees = 110 }] },
        }, indented: false);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("partially-folded", document.RootElement.GetProperty("requestedMode").GetString());
        Assert.Equal(110, document.RootElement.GetProperty("state").GetProperty("hinges")[0].GetProperty("angleDegrees").GetDouble());
        Assert.Contains("canSetAngle", CliJson.SerializeUntyped(new FoldCapabilities()));
    }
}
