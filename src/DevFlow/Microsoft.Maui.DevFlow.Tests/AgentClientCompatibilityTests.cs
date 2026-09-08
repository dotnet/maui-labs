using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class AgentClientCompatibilityTests
{
    [Fact]
    public void CaptureAwareOverloads_PreserveOriginalPublicSignatures()
    {
        var type = typeof(AgentClient);

        AssertMethod(type, nameof(AgentClient.TapAsync), typeof(string));
        AssertMethod(type, nameof(AgentClient.FillAsync), typeof(string), typeof(string));
        AssertMethod(type, nameof(AgentClient.ClearAsync), typeof(string));
        AssertMethod(
            type,
            nameof(AgentClient.ClearResultAsync),
            typeof(string),
            typeof(long?),
            typeof(long?));
        AssertMethod(type, nameof(AgentClient.FocusAsync), typeof(string));
        AssertMethod(
            type,
            nameof(AgentClient.FocusResultAsync),
            typeof(string),
            typeof(long?),
            typeof(long?));
        AssertMethod(type, nameof(AgentClient.KeyAsync), typeof(string), typeof(string), typeof(string));
        AssertMethod(
            type,
            nameof(AgentClient.GestureAsync),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(double?),
            typeof(int?));
        AssertMethod(
            type,
            nameof(AgentClient.GestureResultAsync),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(double?),
            typeof(int?),
            typeof(long?),
            typeof(long?));
        AssertMethod(type, nameof(AgentClient.BatchAsync), typeof(IEnumerable<JsonObject>), typeof(bool));
        AssertMethod(
            type,
            nameof(AgentClient.ScrollAsync),
            typeof(string),
            typeof(double),
            typeof(double),
            typeof(bool),
            typeof(int?),
            typeof(int?),
            typeof(int?),
            typeof(string));
        AssertMethod(
            type,
            nameof(AgentClient.ScreenshotAsync),
            typeof(int?),
            typeof(string),
            typeof(string),
            typeof(int?),
            typeof(string));
        AssertMethod(
            type,
            nameof(AgentClient.ScreenshotResultAsync),
            typeof(int?),
            typeof(string),
            typeof(string),
            typeof(int?),
            typeof(string));
        AssertMethod(
            type,
            nameof(AgentClient.SetPropertyAsync),
            typeof(string),
            typeof(string),
            typeof(string));
        AssertMethod(
            type,
            nameof(AgentClient.SetPropertyResultAsync),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(long?),
            typeof(long?));

        Assert.NotNull(typeof(ScreenshotResult).GetMethod(
            nameof(ScreenshotResult.Ok),
            [typeof(byte[])]));
    }

    private static void AssertMethod(Type type, string name, params Type[] parameterTypes)
        => Assert.NotNull(type.GetMethod(name, parameterTypes));
}
