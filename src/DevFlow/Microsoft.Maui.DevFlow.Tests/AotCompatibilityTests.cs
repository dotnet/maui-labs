using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class AotCompatibilityTests
{
    [Fact]
    public void CliJson_Serializes_And_Deserializes_CommandDescriptions()
    {
        var commands = new List<CommandDescription>
        {
            new("MAUI status", "Check agent connection and app info", false)
        };

        var json = CliJson.SerializeUntyped(commands, indented: false);
        var deserialized = CliJson.Deserialize<List<CommandDescription>>(json);

        Assert.NotNull(deserialized);
        var command = Assert.Single(deserialized);
        Assert.Equal("MAUI status", command.Command);
        Assert.Equal("Check agent connection and app info", command.Description);
        Assert.False(command.Mutating);
    }

    [Fact]
    public void CliJson_Serializes_JsonNode_Payloads()
    {
        var payload = new JsonObject
        {
            ["status"] = "ok",
            ["agents"] = 2
        };

        var json = CliJson.SerializeUntyped(payload, indented: false);

        Assert.Equal("{\"status\":\"ok\",\"agents\":2}", json);
    }

    [Fact]
    public void DriverJson_Serializes_And_Deserializes_NetworkRequests()
    {
        var requests = new List<NetworkRequest>
        {
            new()
            {
                Id = "req-1",
                Method = "GET",
                Url = "https://example.com/api",
                Host = "example.com",
                Path = "/api",
                StatusCode = 200,
                DurationMs = 42,
                Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
            }
        };

        var json = DriverJson.SerializeUntyped(requests);
        var deserialized = DriverJson.Deserialize<List<NetworkRequest>>(json);

        Assert.NotNull(deserialized);
        var request = Assert.Single(deserialized);
        Assert.Equal("req-1", request.Id);
        Assert.Equal("GET", request.Method);
        Assert.Equal("https://example.com/api", request.Url);
        Assert.Equal(200, request.StatusCode);
        Assert.Equal(42, request.DurationMs);
    }
}
