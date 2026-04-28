using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Device")]
public class DeviceTests : IntegrationTestBase
{
    public DeviceTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task AppInfo_ReturnsValidInfo()
    {
        var json = await Client.GetPlatformInfoAsync("app");

        Assert.True(json.ValueKind != JsonValueKind.Undefined);
        Assert.Contains("name", json.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppInfo_NameMatchesSample()
    {
        var json = await Client.GetPlatformInfoAsync("app");
        var text = json.ToString();

        Assert.True(
            text.Contains("MauiTodo", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("DevFlow", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("mauitodo", StringComparison.OrdinalIgnoreCase),
            $"Expected app name to contain 'MauiTodo' or 'DevFlow', got: {text}");
    }

    [Fact]
    public async Task DeviceInfo_ReturnsPlatformInfo()
    {
        var json = await Client.GetPlatformInfoAsync("info");

        Assert.True(json.ValueKind != JsonValueKind.Undefined);
        Output.WriteLine($"Device info: {json}");
    }

    [Fact]
    public async Task DeviceInfo_HasManufacturer()
    {
        var json = await Client.GetPlatformInfoAsync("info");
        var text = json.ToString();

        if (Platform == "android")
        {
            Assert.True(
                text.Contains("manufacturer", StringComparison.OrdinalIgnoreCase),
                $"Expected manufacturer field in device info, got: {text}");
        }
        else if (Platform == "ios" || Platform == "maccatalyst")
        {
            Assert.Contains("Apple", text, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.True(
                text.Contains("manufacturer", StringComparison.OrdinalIgnoreCase),
                $"Expected manufacturer field in device info, got: {text}");
        }
    }

    [Fact]
    public async Task Display_ReturnsMetrics()
    {
        var json = await Client.GetPlatformInfoAsync("display");
        var text = json.ToString();

        Assert.True(json.ValueKind != JsonValueKind.Undefined);
        Assert.True(
            text.Contains("width", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("density", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("height", StringComparison.OrdinalIgnoreCase),
            $"Display info should contain dimension data, got: {text}");
    }

    [Fact]
    public async Task Battery_ReturnsInfo()
    {
        var json = await Client.GetPlatformInfoAsync("battery");

        Assert.True(json.ValueKind != JsonValueKind.Undefined);
    }

    [Fact]
    public async Task Connectivity_ReturnsState()
    {
        var json = await Client.GetPlatformInfoAsync("connectivity");

        Assert.True(json.ValueKind != JsonValueKind.Undefined);
    }

    [Fact]
    public async Task Permissions_ReturnsList()
    {
        var response = await GetRawAsync("/api/v1/device/permissions");

        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task Permission_SpecificPermission_ReturnsStatus()
    {
        var response = await GetRawAsync("/api/v1/device/permissions/camera");

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Output.WriteLine($"Camera permission: {body}");
        }
    }

    [Fact]
    public async Task Geolocation_ReturnsOrHandlesGracefully()
    {
        try
        {
            var json = await Client.GetGeolocationAsync(accuracy: "Low", timeoutSeconds: 5);
            Output.WriteLine($"Geolocation: {json}");
        }
        catch (HttpRequestException ex)
        {
            Output.WriteLine($"Geolocation not available: {ex.Message}");
        }
    }

    [Fact]
    public async Task Jobs_ReturnsSupportedFlagAndJobArray()
    {
        var json = await Client.GetJobsAsync();

        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.True(json.TryGetProperty("supported", out var supported));
        Assert.True(supported.ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.True(json.TryGetProperty("jobs", out var jobs));
        Assert.Equal(JsonValueKind.Array, jobs.ValueKind);

        if (Platform is "android" or "ios" or "maccatalyst")
            Assert.True(supported.GetBoolean());
    }
}
