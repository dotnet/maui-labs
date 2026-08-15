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
        Output.WriteLine($"jobs payload: {json}");

        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.True(json.TryGetProperty("supported", out var supported));
        Assert.True(supported.ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.True(json.TryGetProperty("jobs", out var jobs));
        Assert.Equal(JsonValueKind.Array, jobs.ValueKind);

        // BGTaskScheduler ships with the OS and the sample references WorkManager, so both
        // platforms must report the capability as present.
        if (Platform is "ios" or "maccatalyst" or "android")
            Assert.True(supported.GetBoolean());

        // An empty list must never be the result of a failed query — if the query could not
        // complete, the payload has to say so rather than looking like an idle queue.
        if (json.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            Assert.Fail($"Jobs query reported an error: {error.GetString()}");
    }

    [Fact]
    public async Task Jobs_Android_ListsTheSampleWorker()
    {
        if (Platform != "android")
            return;

        // The sample enqueues SampleSyncWorker on every launch, so a working WorkManager query
        // must return it. This is the assertion that actually exercises the reflection path —
        // shape-only checks passed for months while the query silently returned nothing.
        var json = await Client.GetJobsAsync();
        Output.WriteLine($"jobs payload: {json}");

        var jobs = json.GetProperty("jobs").EnumerateArray().ToList();
        Assert.NotEmpty(jobs);

        var sampleJob = jobs.FirstOrDefault(j =>
            j.TryGetProperty("tags", out var tags) &&
            tags.EnumerateArray().Any(t => t.GetString() == "devflow-sample-sync"));

        Assert.True(sampleJob.ValueKind == JsonValueKind.Object,
            "No job tagged 'devflow-sample-sync' was returned by WorkManager.");

        // A real WorkInfo carries a UUID and a state; empty strings mean the reflected
        // accessors silently returned nothing.
        Assert.False(string.IsNullOrWhiteSpace(sampleJob.GetProperty("identifier").GetString()));
        Assert.Contains(sampleJob.GetProperty("state").GetString(),
            new[] { "ENQUEUED", "RUNNING", "SUCCEEDED", "FAILED", "BLOCKED", "CANCELLED" });
    }

    [Fact]
    public async Task Jobs_UnsupportedCapability_IsReportedConsistently()
    {
        // The jobs list and the capabilities document must not disagree about whether
        // background jobs are available, or a caller feature-detecting via capabilities
        // gets a different answer than one reading the list.
        var jobs = await Client.GetJobsAsync();
        var caps = await GetJsonAsync("/api/v1/agent/capabilities");

        var listSupported = jobs.GetProperty("supported").GetBoolean();
        var capsSupported = caps.GetProperty("jobs").GetProperty("supported").GetBoolean();

        Assert.Equal(listSupported, capsSupported);
    }
}
