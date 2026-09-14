using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// Locks the wire shape of the protocol DTOs. The DTOs are defined once, in the portable client,
/// so this file running on both the .NET Framework and modern target families is what guarantees
/// every DevFlow harness serializes and parses the protocol identically.
/// </summary>
public class ProtocolSerializationTests
{
    [Fact]
    public void ElementInfo_RoundTripsThroughProtocolPropertyNames()
    {
        var element = new ElementInfo
        {
            Id = "el-1",
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            AutomationId = "SubmitButton",
            Text = "Submit",
            IsVisible = true,
            IsEnabled = true,
            CaptureEpoch = 7,
            RegistryGeneration = 3,
            Bounds = new BoundsInfo { X = 10, Y = 20, Width = 100, Height = 40 }
        };

        var json = ProtocolJson.SerializeUntyped(element);

        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            Assert.Equal("el-1", root.GetProperty("id").GetString());
            Assert.Equal("Button", root.GetProperty("type").GetString());
            Assert.Equal("Microsoft.Maui.Controls.Button", root.GetProperty("fullType").GetString());
            Assert.Equal("SubmitButton", root.GetProperty("automationId").GetString());
            Assert.Equal(7, root.GetProperty("captureEpoch").GetInt64());
            Assert.Equal(3, root.GetProperty("registryGeneration").GetInt64());
            Assert.Equal(100, root.GetProperty("bounds").GetProperty("width").GetDouble());
        }

        var parsed = ProtocolJson.Deserialize<ElementInfo>(json);

        Assert.NotNull(parsed);
        Assert.Equal(element.Id, parsed!.Id);
        Assert.Equal(element.Type, parsed.Type);
        Assert.Equal(element.AutomationId, parsed.AutomationId);
        Assert.Equal(element.Text, parsed.Text);
        Assert.True(parsed.IsVisible);
        Assert.Equal(40, parsed.Bounds!.Height);
    }

    [Fact]
    public void ElementInfo_UnknownAgentFieldsAreIgnored()
    {
        // Agents newer than the client must not break parsing.
        var parsed = ProtocolJson.Deserialize<ElementInfo>("""
            { "id": "el-1", "type": "Label", "somethingBrandNew": { "nested": [1, 2, 3] } }
            """);

        Assert.NotNull(parsed);
        Assert.Equal("el-1", parsed!.Id);
        Assert.Equal("Label", parsed.Type);
    }

    [Fact]
    public void AgentStatus_ParsesNestedDescriptors()
    {
        var status = ProtocolJson.Deserialize<AgentStatus>("""
            {
              "running": true,
              "agent": { "name": "DevFlow", "version": "0.1.0", "frameworkId": "native", "uiFramework": "appkit" },
              "device": { "platform": "macOS", "idiom": "Desktop" },
              "app": { "name": "Sample" }
            }
            """);

        Assert.NotNull(status);
        Assert.True(status!.Running);
        Assert.Equal("0.1.0", status.Version);
        Assert.Equal("native", status.Agent!.FrameworkId);
        Assert.Equal("macOS", status.Platform);
        Assert.Equal("Desktop", status.Idiom);
        Assert.Equal("Sample", status.AppName);
    }

    [Fact]
    public void NetworkRequest_RoundTripsListPayload()
    {
        var requests = new List<NetworkRequest>
        {
            new NetworkRequest
            {
                Id = "req-1",
                Method = "GET",
                Url = "https://example.com/api",
                StatusCode = 200,
                DurationMs = 42,
                Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
            }
        };

        var json = ProtocolJson.SerializeUntyped(requests);
        var parsed = ProtocolJson.Deserialize<List<NetworkRequest>>(json);

        Assert.NotNull(parsed);
        var request = Assert.Single(parsed!);
        Assert.Equal("req-1", request.Id);
        Assert.Equal("GET", request.Method);
        Assert.Equal(200, request.StatusCode);
        Assert.Equal(42, request.DurationMs);
    }

    [Fact]
    public void ThemeResult_UsesProtocolThemeStrings()
    {
        var result = ProtocolJson.Deserialize<ThemeResult>("""
            { "theme": "dark", "requestedTheme": "light", "source": "system", "success": true }
            """);

        Assert.NotNull(result);
        Assert.Equal(DevFlowTheme.Dark, result!.Theme);
        Assert.Equal(DevFlowTheme.Light, result.RequestedTheme);
        Assert.Equal("system", result.Source);
        Assert.True(result.Success);

        var json = ProtocolJson.SerializeUntyped(result);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("dark", document.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public void RecordingState_RoundTripsRequiredMembers()
    {
        // Exercises `required` and `init` members, which the netstandard2.0 target only compiles
        // because of the polyfilled compiler attributes.
        var state = new RecordingState
        {
            RecordingPid = 4242,
            OutputFile = "/tmp/out.mp4",
            Platform = "android",
            DeviceOutputFile = "/sdcard/out.mp4",
            TimeoutSeconds = 120,
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        };

        var json = ProtocolJson.SerializeUntyped(state);
        var parsed = ProtocolJson.Deserialize<RecordingState>(json);

        Assert.NotNull(parsed);
        Assert.Equal(4242, parsed!.RecordingPid);
        Assert.Equal("/tmp/out.mp4", parsed.OutputFile);
        Assert.Equal("android", parsed.Platform);
        Assert.Equal("/sdcard/out.mp4", parsed.DeviceOutputFile);
        Assert.Equal(120, parsed.TimeoutSeconds);
    }

    [Fact]
    public void ExtensionDescriptor_ParsesToolMetadata()
    {
        var extensions = ProtocolJson.Deserialize<Dictionary<string, ExtensionDescriptor>>("""
            {
              "sample": {
                "version": "1.0.0",
                "description": "Sample extension",
                "tools": [ { "name": "doThing", "description": "Does the thing" } ]
              }
            }
            """);

        Assert.NotNull(extensions);
        var descriptor = extensions!["sample"];
        Assert.Equal("1.0.0", descriptor.Version);
        var tool = Assert.Single(descriptor.Tools);
        Assert.Equal("doThing", tool.Name);
    }

    [Fact]
    public void PrettyPrint_IndentsWithoutChangingValues()
    {
        var pretty = ProtocolJson.PrettyPrint("""{"a":1,"b":[2,3]}""");

        Assert.Contains(Environment.NewLine, pretty);
        using var document = JsonDocument.Parse(pretty);
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("b")[0].GetInt32());
    }
}
