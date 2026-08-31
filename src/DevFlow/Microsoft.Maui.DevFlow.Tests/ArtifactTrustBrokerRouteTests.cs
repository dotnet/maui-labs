using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.DevFlow.Tests;

public class ArtifactTrustBrokerRouteTests
{
    [Fact]
    public async Task ArtifactTrustRoutes_RequireCapabilityAndNeverExposeRawImportedContent()
    {
        const string secret = "route-import-secret";
        var port = FreePort();
        using var broker = new BrokerServer(port, TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(port);

        try
        {
            using var http = new HttpClient();
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(FlowRunJson(secret)));
            var imported = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/artifact-trust/import?kind=flow-run",
                content);
            Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
            var importText = await imported.Content.ReadAsStringAsync();
            Assert.DoesNotContain(secret, importText, StringComparison.Ordinal);
            using var importJson = JsonDocument.Parse(importText);
            var status = importJson.RootElement.GetProperty("status");
            var artifactId = status.GetProperty("identity").GetProperty("id").GetString();
            var capability = importJson.RootElement.GetProperty("capabilityToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(artifactId));
            Assert.False(string.IsNullOrWhiteSpace(capability));
            Assert.Equal("untrusted", status.GetProperty("verification").GetProperty("state").GetString());
            Assert.False(status.GetProperty("rawContentRetained").GetBoolean());

            var denied = await http.GetAsync(
                $"http://127.0.0.1:{port}/api/artifact-trust/{artifactId}/status");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            using var statusRequest = AuthorizedGet(
                $"http://127.0.0.1:{port}/api/artifact-trust/{artifactId}/status",
                capability!);
            var allowed = await http.SendAsync(statusRequest);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

            using var projectionRequest = AuthorizedGet(
                $"http://127.0.0.1:{port}/api/artifact-trust/{artifactId}/projection",
                capability!);
            var projection = await http.SendAsync(projectionRequest);
            Assert.Equal(HttpStatusCode.OK, projection.StatusCode);
            Assert.DoesNotContain(secret, await projection.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            using var rawRequest = AuthorizedGet(
                $"http://127.0.0.1:{port}/api/artifact-trust/{artifactId}/raw",
                capability!);
            var raw = await http.SendAsync(rawRequest);
            Assert.Equal(HttpStatusCode.NotFound, raw.StatusCode);

            using var bindRequest = AuthorizedPost(
                $"http://127.0.0.1:{port}/api/artifact-trust/{artifactId}/bind-local-reproduction",
                new
                {
                    localRunId = "run_not_executed",
                    current = new
                    {
                        flowDigest = "flow",
                        appBuildFingerprint = "build",
                        appSourceFingerprint = "source",
                        packageDigest = "package",
                        platform = "android",
                        deviceProfile = "pixel"
                    }
                },
                capability!);
            var bind = await http.SendAsync(bindRequest);
            Assert.Equal(HttpStatusCode.Conflict, bind.StatusCode);
        }
        finally
        {
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static HttpRequestMessage AuthorizedGet(string url, string capability)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Maui-Artifact-Capability", capability);
        return request;
    }

    private static HttpRequestMessage AuthorizedPost(string url, object value, string capability)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Maui-Artifact-Capability", capability);
        return request;
    }

    private static string FlowRunJson(string secret)
        => $$"""
            {
              "schema": 1,
              "runId": "embedded-run-id",
              "flowDigest": "flow",
              "target": {
                "appBuildFingerprint": "build",
                "appSourceFingerprint": "source",
                "packageDigest": "package",
                "platform": "android",
                "deviceProfile": "pixel"
              },
              "outcome": { "status": "failed" },
              "failure": {
                "failureId": "embedded-failure",
                "code": "locator-not-found",
                "class": "locator-not-found",
                "stepId": "tap-save",
                "message": "{{secret}}"
              },
              "steps": [],
              "events": [],
              "artifacts": []
            }
            """;

    private static async Task WaitForBrokerAsync(int port)
    {
        using var http = new HttpClient();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException("Broker did not start.");
    }

    private static int FreePort() => TestPorts.Reserve();
}
