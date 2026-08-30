using System.Text.Json;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Recorded interactions with the device rather than the app.
/// <para>
/// These exist so a flow that hits a permission prompt can be authored at all. What they must not
/// do is quietly encourage coordinate-based tests, so fragility is surfaced rather than hidden.
/// </para>
/// </summary>
public class DeviceStepTests
{
    [Fact]
    public void AStepWithANativeViewIsNotFragile()
    {
        // Matching a native view by text survives a layout change; a coordinate does not.
        var step = new DeviceStep { X = 540, Y = 1620, NativeText = "Allow" };

        Assert.False(step.IsFragile);
    }

    [Fact]
    public void ACoordinateOnlyStepIsFragile()
    {
        var step = new DeviceStep { X = 540, Y = 1620 };

        Assert.True(step.IsFragile);
    }

    [Fact]
    public void AnIdIsEnoughToBeDurable()
    {
        var step = new DeviceStep { X = 1, Y = 2, NativeId = "com.android.permissioncontroller:id/allow" };

        Assert.False(step.IsFragile);
    }

    [Fact]
    public void DescribeNamesTheNativeView_SoAReviewerCanJudgeIt()
    {
        // "tap Allow" is reviewable. "tap (540, 1620)" is not, and a review surface that shows the
        // latter is asking a human to approve something they cannot evaluate.
        var named = new DeviceStep { Action = "tap", X = 540, Y = 1620, NativeText = "Allow" };
        var bare = new DeviceStep { Action = "tap", X = 540, Y = 1620 };

        Assert.Contains("Allow", named.Describe());
        Assert.Contains("540", bare.Describe());
    }

    [Fact]
    public void ParsesFromFlowExtensionData()
    {
        var json = """
            {"deviceSteps":[
              {"afterStep":2,"action":"tap","x":540,"y":1620,"nativeText":"Allow"}
            ]}
            """;
        var extensionData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        var steps = DeviceStep.FromExtensionData(extensionData);

        var step = Assert.Single(steps);
        Assert.Equal(2, step.AfterStep);
        Assert.Equal("Allow", step.NativeText);
        Assert.Equal(540, step.X);
    }

    [Fact]
    public void ReturnsEmpty_WhenAFlowDeclaresNone()
    {
        // The overwhelmingly common case: existing flows carry no device steps and must be
        // completely unaffected.
        Assert.Empty(DeviceStep.FromExtensionData(null));
        Assert.Empty(DeviceStep.FromExtensionData(new Dictionary<string, JsonElement>()));
    }

    [Fact]
    public void ReturnsEmpty_ForAMalformedBlock()
    {
        // A wrong shape must not throw into flow loading; a flow with unreadable device steps is
        // still a valid flow for every step that is readable.
        var extensionData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"deviceSteps":{"not":"an array"}}""")!;

        Assert.Empty(DeviceStep.FromExtensionData(extensionData));
    }

    [Fact]
    public void UnknownFieldsSurvive_SoTheFormatCanGrow()
    {
        var extensionData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"deviceSteps":[{"action":"tap","x":1,"y":2,"somethingNew":true}]}""")!;

        var step = Assert.Single(DeviceStep.FromExtensionData(extensionData));

        Assert.Equal("tap", step.Action);
    }

    [Fact]
    public void StrictParser_RejectsDeclaredWorkWithoutBothCoordinates()
    {
        var extensionData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"deviceSteps":[{"action":"tap","x":1,"nativeId":"allow"}]}""")!;

        var ok = DeviceStep.TryReadFromExtensionData(extensionData, out var steps, out var error);

        Assert.False(ok);
        Assert.Empty(steps);
        Assert.Contains("coordinates", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StrictParser_RejectsNullEntriesDeterministically()
    {
        var extensionData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"deviceSteps":[null]}""")!;

        var ok = DeviceStep.TryReadFromExtensionData(extensionData, out var steps, out var error);

        Assert.False(ok);
        Assert.Empty(steps);
        Assert.Equal("deviceSteps entries must be non-null objects.", error);
    }
}
