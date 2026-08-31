using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class BrokerWorkflowRunEndpointTests
{
    [Fact]
    public async Task WorkflowRunEndpoints_RequireExactInstanceAndCapabilityToken()
    {
        var port = FreePort();
        using var broker = new BrokerServer(port, TimeSpan.FromMinutes(1), requireWorkflowRunAuthorization: false);
        using var brokerCancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(brokerCancellation.Token);
        await WaitForBrokerAsync(port);

        var agentPort = FreePort();
        using var agentServer = new AgentHttpServer(agentPort);
        agentServer.MapPost(
            "/api/v1/agent/workflow-runs",
            _ => Task.FromResult(HttpResponse.Json(new { ok = true })),
            requiresMutationLease: false);
        agentServer.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/agent"), CancellationToken.None);
        await SendAsync(socket, $$"""
            {"type":"register","project":"endpoint-test","tfm":"net10.0","platform":"test","appName":"Endpoint Test","currentPort":{{agentPort}}}
            """);
        await ReceiveAsync(socket);

        using var http = new HttpClient();
        var agents = await ReadJsonAsync(await http.GetAsync($"http://127.0.0.1:{port}/api/agents"));
        var agent = agents.RootElement[0];
        var agentId = agent.GetProperty("id").GetString();
        var instanceId = agent.GetProperty("instanceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(instanceId));

        var capabilities = await http.GetAsync($"http://127.0.0.1:{port}/api/workflow-runs/capabilities");
        Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
        using (var body = await ReadJsonAsync(capabilities))
        {
            Assert.True(body.RootElement.GetProperty("supported").GetBoolean());
            Assert.True(body.RootElement.GetProperty("requiresExplicitAgentInstance").GetBoolean());
            Assert.True(body.RootElement.GetProperty("workflowCommandLedger").GetBoolean());
        }

        var empty = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/workflow-runs/start",
            Json(new
            {
                agentId,
                agentInstanceId = instanceId,
                idempotencyKey = "empty-key",
                markdown = FlowMarkdown.Serialize(new MauiFlow { Name = "empty" })
            }));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains(
            "at least one step",
            await empty.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        var markdown = FlowMarkdown.Serialize(AssertOnlyFlow());
        var stale = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/workflow-runs/start",
            Json(new
            {
                agentId,
                agentInstanceId = "stale",
                idempotencyKey = "stale-key",
                markdown
            }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var start = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/workflow-runs/start",
            Json(new
            {
                agentId,
                agentInstanceId = instanceId,
                idempotencyKey = "endpoint-key",
                markdown
            }));
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startBody = await ReadJsonAsync(start);
        var runId = startBody.RootElement.GetProperty("run").GetProperty("runId").GetString();
        var capabilityToken = startBody.RootElement.GetProperty("capabilityToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));
        Assert.False(string.IsNullOrWhiteSpace(capabilityToken));

        var duplicate = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/workflow-runs/start",
            Json(new
            {
                agentId,
                agentInstanceId = instanceId,
                idempotencyKey = "endpoint-key",
                markdown
            }));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using (var duplicateBody = await ReadJsonAsync(duplicate))
        {
            Assert.True(duplicateBody.RootElement.GetProperty("existing").GetBoolean());
            Assert.Equal(runId, duplicateBody.RootElement.GetProperty("run").GetProperty("runId").GetString());
            Assert.Equal(capabilityToken, duplicateBody.RootElement.GetProperty("capabilityToken").GetString());
        }

        var differentFlow = AssertOnlyFlow();
        differentFlow.Name = "different";
        var conflicting = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/workflow-runs/start",
            Json(new
            {
                agentId,
                agentInstanceId = instanceId,
                idempotencyKey = "endpoint-key",
                markdown = FlowMarkdown.Serialize(differentFlow)
            }));
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        var denied = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/workflow-runs/{runId}/status",
            Json(new { }));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        JsonDocument? status = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            status?.Dispose();
            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/workflow-runs/{runId}/status",
                Json(new { capabilityToken }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            status = await ReadJsonAsync(response);
            if (status.RootElement.GetProperty("run").GetProperty("terminal").GetBoolean())
                break;
            await Task.Delay(25);
        }
        using (status)
        {
            Assert.NotNull(status);
            Assert.True(status!.RootElement.GetProperty("run").GetProperty("terminal").GetBoolean());
            var run = status.RootElement.GetProperty("run");
            Assert.True(
                string.Equals("passed", run.GetProperty("state").GetString(), StringComparison.Ordinal),
                $"Expected a passing run but received '{run.GetProperty("state").GetString()}': {run.GetProperty("message").GetString()}");
            var admission = run.GetProperty("admission");
            Assert.Equal("unspecified", admission.GetProperty("sideEffectPolicy").GetString());
            Assert.False(admission.GetProperty("repairEligibility").GetBoolean());
            var report = run.GetProperty("report");
            Assert.Equal("unspecified", report.GetProperty("sideEffectPolicy").GetString());
            Assert.False(report.GetProperty("replayEligibility").GetProperty("repairEligibility").GetBoolean());
        }

        var cancelTerminal = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/workflow-runs/{runId}/cancel",
            Json(new { capabilityToken }));
        Assert.Equal(HttpStatusCode.OK, cancelTerminal.StatusCode);
        using (var cancelBody = await ReadJsonAsync(cancelTerminal))
            Assert.True(cancelBody.RootElement.GetProperty("alreadyTerminal").GetBoolean());

        brokerCancellation.Cancel();
        broker.Dispose();
        await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WorkflowRunStart_WithoutHumanAuthorization_IsRejected()
    {
        var port = FreePort();
        using var broker = new BrokerServer(port, TimeSpan.FromMinutes(1));
        using var brokerCancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(brokerCancellation.Token);
        await WaitForBrokerAsync(port);

        var agentPort = FreePort();
        using var agentServer = new AgentHttpServer(agentPort);
        agentServer.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/agent"), CancellationToken.None);
        await SendAsync(socket, $$"""
            {"type":"register","project":"auth-test","tfm":"net10.0","platform":"test","appName":"Auth Test","currentPort":{{agentPort}}}
            """);
        await ReceiveAsync(socket);

        using var http = new HttpClient();
        using var agents = await ReadJsonAsync(await http.GetAsync($"http://127.0.0.1:{port}/api/agents"));
        var agent = agents.RootElement[0];
        var agentId = agent.GetProperty("id").GetString();
        var instanceId = agent.GetProperty("instanceId").GetString();

        var unauthorized = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/workflow-runs/start",
            Json(new
            {
                agentId,
                agentInstanceId = instanceId,
                idempotencyKey = "unauthorized-key",
                markdown = FlowMarkdown.Serialize(AssertOnlyFlow())
            }));

        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        Assert.Contains(
            "authorization is required",
            await unauthorized.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        brokerCancellation.Cancel();
        await Task.WhenAny(brokerTask, Task.Delay(TimeSpan.FromSeconds(5)));
    }

    private static MauiFlow AssertOnlyFlow() => new()
    {
        Name = "endpoint",
        Steps =
        {
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Assert,
                Asserts = new()
                {
                    new FlowAssert
                    {
                        Kind = "exists",
                        Verify = false,
                        Selector = new FlowSelector { AutomationId = "label" }
                    }
                }
            }
        }
    };

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

    private static async Task SendAsync(ClientWebSocket socket, string text)
        => await socket.SendAsync(
            Encoding.UTF8.GetBytes(text),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

    private static async Task<string> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static StringContent Json(object value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static int FreePort() => TestPorts.Reserve();
}
