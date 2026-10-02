using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class EvidenceInspectorRouteTests
{
    [Theory]
    [InlineData("/api/evidence/preview")]
    [InlineData("/api/evidence/capture")]
    public void EvidenceRoutes_RequireTheReadTokenWithoutClaimingMutationAuthority(string path)
    {
        Assert.True(InspectorServer.IsTokenGatedPath(path));
        Assert.False(InspectorServer.IsMutation(path));
        Assert.False(InspectorServer.IsBlockedDuringReplay(path));
    }

    [Theory]
    [InlineData("/api/evidence/preview")]
    [InlineData("/api/evidence/capture")]
    public Task EvidenceRoutes_RejectMissingReadToken(string path)
        => WithInspectorAsync(async (http, agent) =>
        {
            http.DefaultRequestHeaders.Remove("X-DevFlow-Inspector-Token");
            var response = await http.PostAsync(path, Json("{}"));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(agent.Requests);
        });

    [Fact]
    public Task Preview_ReturnsTargetEnvironmentWithoutAHostOutputPathOrScreenshotRead()
        => WithInspectorAsync(async (http, agent) =>
        {
            var response = await http.PostAsync("/api/evidence/preview", Json("""{"elementId":"e1"}"""));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var plan = body.RootElement.GetProperty("plan");
            Assert.Equal("inspector", plan.GetProperty("source").GetString());
            Assert.Equal("e1", plan.GetProperty("selectedElementId").GetString());
            Assert.False(plan.TryGetProperty("outputPath", out _));
            Assert.False(plan.GetProperty("screenshot").GetProperty("requested").GetBoolean());
            Assert.Equal(430, plan.GetProperty("environment").GetProperty("viewport").GetProperty("width").GetDouble());
            Assert.Equal("dark", plan.GetProperty("environment").GetProperty("theme").GetProperty("effective").GetString());
            Assert.Equal(0, agent.ScreenshotRequests);
        });

    [Fact]
    public Task Preview_ListsRequestedAttachmentsButDoesNotCapturePixels()
        => WithInspectorAsync(async (http, agent) =>
        {
            var response = await http.PostAsync("/api/evidence/preview",
                Json("""{"includeScreenshot":true,"includeWorkflow":true,"workflow":"# Repro\n1. Tap"}"""));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var entries = body.RootElement.GetProperty("plan").GetProperty("included").EnumerateArray()
                .Select(entry => entry.GetProperty("name").GetString()).ToList();
            Assert.Contains(EvidenceFormat.WorkflowEntry, entries);
            Assert.Contains(EvidenceFormat.ScreenshotEntry, entries);
            Assert.Equal(0, agent.ScreenshotRequests);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Capture_WritesOnlyExplicitlyRequestedAttachments(bool includeAttachments)
        => WithInspectorAsync(async (http, agent) =>
        {
            var options = includeAttachments
                ? """{"includeScreenshot":true,"workflow":"# Repro\nOpen C:\\Users\\alice\\App.xaml with api_key=zzz-9999"}"""
                : "{}";
            var response = await http.PostAsync("/api/evidence/capture", Json(options));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            var read = EvidenceBundleReader.Read(stream);
            Assert.True(read.Ok, read.Error);
            Assert.Equal(includeAttachments, read.Manifest!.Screenshot.Included);
            Assert.Equal(includeAttachments ? 1 : 0, agent.ScreenshotRequests);
            Assert.Equal(includeAttachments, read.Screenshot is not null);
            Assert.Equal(includeAttachments, read.Workflow is not null);
            if (includeAttachments)
            {
                Assert.DoesNotContain("zzz-9999", read.Workflow!, StringComparison.Ordinal);
                Assert.DoesNotContain(@"C:\Users\alice", read.Workflow!, StringComparison.Ordinal);
            }
            var environment = EvidenceJson.Serialize(read.Environment);
            Assert.DoesNotContain("private device name", environment, StringComparison.Ordinal);
            Assert.DoesNotContain("private network name", environment, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-label-text", EvidenceJson.Serialize(read.Tree), StringComparison.Ordinal);
            Assert.DoesNotContain(agent.Requests, path => path.Contains("permission", StringComparison.Ordinal) ||
                path.Contains("storage", StringComparison.Ordinal) || path.Contains("geolocation", StringComparison.Ordinal));
        });

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"includeScreenshot\":\"true\"}")]
    [InlineData("{\"workflow\":false}")]
    [InlineData("{\"logLimit\":0}")]
    [InlineData("{\"networkLimit\":501}")]
    public Task EvidenceRoutes_RejectMalformedOptionsBeforeReadingTheApp(string json)
        => WithInspectorAsync(async (http, agent) =>
        {
            foreach (var route in new[] { "/api/evidence/preview", "/api/evidence/capture" })
            {
                var response = await http.PostAsync(route, Json(json));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            Assert.Empty(agent.Requests);
        });

    [Fact]
    public Task ExplicitlyDecliningWorkflow_OverridesSuppliedText()
        => WithInspectorAsync(async (http, _) =>
        {
            var response = await http.PostAsync("/api/evidence/capture",
                Json("""{"includeWorkflow":false,"workflow":"private note"}"""));
            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            var read = EvidenceBundleReader.Read(stream);
            Assert.True(read.Ok, read.Error);
            Assert.Null(read.Workflow);
        });

    [Fact]
    public Task UnavailableNetworkCapture_IsAnExclusionRatherThanNoRequests()
        => WithInspectorAsync(async (http, _) =>
        {
            var response = await http.PostAsync("/api/evidence/capture", Json("{}"));
            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            var read = EvidenceBundleReader.Read(stream);
            Assert.True(read.Ok, read.Error);
            Assert.Null(read.Network);
            Assert.Contains(read.Manifest!.Excluded, entry => entry.Name == EvidenceFormat.NetworkEntry);
        }, networkUnavailable: true);

    private static async Task WithInspectorAsync(
        Func<HttpClient, EvidenceAgentFixture, Task> action, bool networkUnavailable = false)
    {
        await using var agent = new EvidenceAgentFixture(networkUnavailable);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            var token = (string)typeof(InspectorServer)
                .GetField("_readToken", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inspector)!;
            http.DefaultRequestHeaders.Add("X-DevFlow-Inspector-Token", token);
            await action(http, agent);
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
}
