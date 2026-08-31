using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class InspectorWorkbenchBrokerRouteTests
{
    [Fact]
    public async Task WorkbenchPreflight_UsesBrokerObservedCheckpointFactsAndRequirementCapabilities()
    {
        var brokerPort = FreePort();
        var agentPort = FreePort();
        using var broker = new BrokerServer(
            brokerPort,
            TimeSpan.FromMinutes(1),
            previewFlags: PreviewTestFeatures.AllEnabled());
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(brokerPort);

        using var agent = new AgentHttpServer(agentPort);
        agent.MapGet("/api/v1/agent/status", _ => Task.FromResult(HttpResponse.Json(new
        {
            running = true,
            agent = new { name = "DevFlow", instanceId = "preflight-instance", version = "1" },
            app = new { name = "Preflight Test", build = "build-2", packageId = "com.example.preflight", version = "1.0" },
            device = new { platform = "android", deviceType = "emulator", idiom = "phone" },
            capabilities = new { ui = true, logs = true, mutations = true },
            route = "/checkout",
            window = "main",
            modal = "sheet",
            locale = "fr-FR",
            theme = "dark",
            orientation = "landscape",
            displayProfile = "tablet",
        })));
        agent.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{brokerPort}/ws/agent"), CancellationToken.None);
        await SendAsync(socket, $$"""
            {"type":"register","project":"workbench-preflight-test","tfm":"net10.0","platform":"android","appName":"Preflight Test","currentPort":{{agentPort}}}
            """);
        await ReceiveAsync(socket);

        try
        {
            using var http = new HttpClient();
            using var agents = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{brokerPort}/api/agents"));
            var registration = agents.RootElement[0];
            var agentId = registration.GetProperty("id").GetString()!;
            var instanceId = registration.GetProperty("instanceId").GetString()!;
            var inspectorBase = $"http://127.0.0.1:{brokerPort}/inspector/{Uri.EscapeDataString(agentId)}";

            _ = await http.GetAsync($"{inspectorBase}/");
            var inspector = await GetInspectorAsync(broker, agentId);
            var inspectorToken = (string)typeof(InspectorServer)
                .GetField("_readToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(inspector)!;
            http.DefaultRequestHeaders.Add("X-DevFlow-Inspector-Token", inspectorToken);

            using var target = JsonDocument.Parse(await http.GetStringAsync($"{inspectorBase}/api/workbench/target"));
            var observed = target.RootElement.GetProperty("target").GetProperty("observedCheckpoint");
            Assert.Equal("build-2", observed.GetProperty("appBuildFingerprint").GetString());
            Assert.Equal("/checkout", observed.GetProperty("route").GetString());
            Assert.Equal("main", observed.GetProperty("window").GetString());
            Assert.Equal("sheet", observed.GetProperty("modal").GetString());
            Assert.Equal("fr-FR", observed.GetProperty("locale").GetString());
            Assert.Equal("dark", observed.GetProperty("theme").GetString());
            Assert.Equal("landscape", observed.GetProperty("orientation").GetString());
            Assert.Equal("tablet", observed.GetProperty("displayProfile").GetString());

            var preflightFlow = AssertOnlyFlow();
            var preflightFlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(preflightFlow);
            using var preflight = await PostJsonAsync(
                http,
                $"{inspectorBase}/api/workbench/run/preflight",
                new
                {
                    run = new
                    {
                        agentId,
                        agentInstanceId = instanceId,
                        idempotencyKey = "workbench-preflight-checkpoint-key",
                        markdown = FlowMarkdown.Serialize(preflightFlow),
                        plan = new
                        {
                            schema = 1,
                            planId = "plan-workbench-checkpoint",
                            revision = 1,
                            flow = new { path = "checkpoint.md", revision = 1, digest = preflightFlowDigest },
                            goal = "Validate observed checkpoint and target requirements",
                            reset = new { required = false, strategy = "host-owned" },
                            provenance = new
                            {
                                actorKind = "human",
                                actorId = "test",
                                channel = "unit-test",
                                provider = "xunit",
                            },
                            sideEffectPolicy = "none",
                            requiredPlatforms = new[] { "android" },
                            checkpoint = new
                            {
                                appBuildFingerprint = "build-2",
                                route = "/checkout",
                                window = "main",
                                modal = "sheet",
                                locale = "fr-FR",
                                theme = "dark",
                                orientation = "landscape",
                                displayProfile = "tablet",
                            },
                            requirements = new
                            {
                                requiredCapabilities = new[]
                                {
                                    new { name = "logs", required = true },
                                },
                                requiredSemantics = Array.Empty<object>(),
                            },
                        },
                    },
                    evidence = new { includeScreenshot = false, includeWorkflow = false },
                });
            Assert.Equal(HttpStatusCode.OK, preflight.StatusCode);
            using var preflightBody = JsonDocument.Parse(await preflight.Content.ReadAsStringAsync());
            Assert.True(preflightBody.RootElement.GetProperty("ok").GetBoolean());
            if (preflightBody.RootElement.TryGetProperty("errors", out var errors))
            {
                Assert.True(
                    errors.ValueKind == JsonValueKind.Null ||
                    !errors.EnumerateArray().Any());
            }
        }
        finally
        {
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task WorkbenchPreflight_RecognizesCanonicalWorkflowCommandLedgerCapability()
    {
        var brokerPort = FreePort();
        var agentPort = FreePort();
        using var broker = new BrokerServer(
            brokerPort,
            TimeSpan.FromMinutes(1),
            previewFlags: PreviewTestFeatures.AllEnabled());
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(brokerPort);

        using var agent = new AgentHttpServer(agentPort);
        agent.MapGet("/api/v1/agent/status", _ => Task.FromResult(HttpResponse.Json(new
        {
            running = true,
            agent = new { name = "DevFlow", instanceId = "ledger-instance", version = "1" },
            app = new { name = "Ledger Test", build = "build-ledger", packageId = "com.example.ledger", version = "1.0" },
            device = new { platform = "android", deviceType = "emulator", idiom = "phone" },
            capabilities = new { ui = true, mutations = true, workflowCommandLedger = true },
            route = "/ledger",
        })));
        agent.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{brokerPort}/ws/agent"), CancellationToken.None);
        await SendAsync(socket, $$"""
            {"type":"register","project":"workbench-ledger-capability-test","tfm":"net10.0","platform":"android","appName":"Ledger Test","currentPort":{{agentPort}}}
            """);
        await ReceiveAsync(socket);

        try
        {
            using var http = new HttpClient();
            using var agents = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{brokerPort}/api/agents"));
            var registration = agents.RootElement[0];
            var agentId = registration.GetProperty("id").GetString()!;
            var instanceId = registration.GetProperty("instanceId").GetString()!;
            var inspectorBase = $"http://127.0.0.1:{brokerPort}/inspector/{Uri.EscapeDataString(agentId)}";

            _ = await http.GetAsync($"{inspectorBase}/");
            var inspector = await GetInspectorAsync(broker, agentId);
            var inspectorToken = (string)typeof(InspectorServer)
                .GetField("_readToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(inspector)!;
            http.DefaultRequestHeaders.Add("X-DevFlow-Inspector-Token", inspectorToken);

            var preflightFlow = AssertOnlyFlow();
            var preflightFlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(preflightFlow);
            using var preflight = await PostJsonAsync(
                http,
                $"{inspectorBase}/api/workbench/run/preflight",
                new
                {
                    run = new
                    {
                        agentId,
                        agentInstanceId = instanceId,
                        idempotencyKey = "workbench-preflight-ledger-key",
                        markdown = FlowMarkdown.Serialize(preflightFlow),
                        plan = new
                        {
                            schema = 1,
                            planId = "plan-workbench-ledger",
                            revision = 1,
                            flow = new { path = "ledger.md", revision = 1, digest = preflightFlowDigest },
                            goal = "Validate workflow command ledger capability",
                            reset = new { required = false, strategy = "host-owned" },
                            provenance = new
                            {
                                actorKind = "human",
                                actorId = "test",
                                channel = "unit-test",
                                provider = "xunit",
                            },
                            sideEffectPolicy = "none",
                            checkpoint = new
                            {
                                route = "/ledger",
                            },
                            requirements = new
                            {
                                requiredCapabilities = new[]
                                {
                                    new { name = "agent.workflowCommandLedger", required = true },
                                },
                                requiredSemantics = Array.Empty<object>(),
                            },
                        },
                    },
                    evidence = new { includeScreenshot = false, includeWorkflow = false },
                });
            Assert.Equal(HttpStatusCode.OK, preflight.StatusCode);
            using var preflightBody = JsonDocument.Parse(await preflight.Content.ReadAsStringAsync());
            Assert.True(preflightBody.RootElement.GetProperty("ok").GetBoolean());
            if (preflightBody.RootElement.TryGetProperty("errors", out var errors))
                Assert.True(errors.ValueKind == JsonValueKind.Null || !errors.EnumerateArray().Any());
        }
        finally
        {
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static MauiFlow AssertOnlyFlow() => new()
    {
        Name = "inspector-workbench",
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
                        Selector = new FlowSelector { AutomationId = "label" },
                    },
                },
            },
        },
    };

    private static MauiFlow FailingTapFlow() => new()
    {
        Name = "inspector-workbench-failure",
        Steps =
        {
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Tap,
                Target = new FlowSelector { AutomationId = "missing-control" },
            },
        },
    };

    private static string FlowRunJson(string secret)
        => $$"""
            {
              "schema": 1,
              "runId": "foreign-run",
              "flowDigest": "flow",
              "outcome": { "status": "failed" },
              "failure": {
                "class": "assertion-failed",
                "code": "assertion-failed",
                "message": "{{secret}}"
              },
              "steps": [],
              "events": [],
              "artifacts": []
            }
            """;

    /// <summary>
    /// The approval ceremony has to work through the broker's <c>/inspector/{agentId}</c> proxy,
    /// because that is the only path <c>maui devflow approve</c> ever takes. A suite that only
    /// drives the Inspector's own listener would pass while production could not approve anything:
    /// the proxy rebuilds the header map, so an owner token it forgets to copy is invisible until
    /// somebody tries to approve a real request.
    /// </summary>
    [Fact]
    public async Task WorkbenchApproval_CompletesTheOwnerTokenCeremonyThroughTheBrokerProxy()
    {
        var brokerPort = FreePort();
        var agentPort = FreePort();
        const string hostToken = "owner-token-through-the-broker-proxy";
        using var broker = new BrokerServer(
            brokerPort,
            TimeSpan.FromMinutes(1),
            previewFlags: PreviewTestFeatures.AllEnabled(),
            log: null,
            trustedHostApprovalVerifier: supplied =>
                string.Equals(supplied, hostToken, StringComparison.Ordinal));
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(brokerPort);

        using var agent = new AgentHttpServer(agentPort);
        agent.MapGet("/api/v1/agent/status", _ => Task.FromResult(HttpResponse.Json(new
        {
            running = true,
            agent = new { name = "DevFlow", instanceId = "approval-instance", version = "1" },
            app = new { name = "Approval Test", build = "build-approve", packageId = "com.example.approve", version = "1.0" },
            device = new { platform = "android", deviceType = "emulator", idiom = "phone" },
            capabilities = new { ui = true, logs = true, mutations = true },
            route = "//home",
            window = "main-window",
            modal = "none",
            locale = "en-US",
            theme = "light",
            orientation = "portrait",
            displayProfile = "phone",
        })));
        agent.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{brokerPort}/ws/agent"), CancellationToken.None);
        await SendAsync(socket, $$"""
            {"type":"register","project":"workbench-approval-test","tfm":"net10.0","platform":"android","appName":"Approval Test","currentPort":{{agentPort}}}
            """);
        await ReceiveAsync(socket);

        try
        {
            using var http = new HttpClient();
            using var agents = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{brokerPort}/api/agents"));
            var registration = agents.RootElement[0];
            var agentId = registration.GetProperty("id").GetString()!;
            var instanceId = registration.GetProperty("instanceId").GetString()!;
            var inspectorBase = $"http://127.0.0.1:{brokerPort}/inspector/{Uri.EscapeDataString(agentId)}";
            _ = await http.GetAsync($"{inspectorBase}/");
            _ = await GetInspectorAsync(broker, agentId);

            var approvalRequestId = await QueueApprovalRequestAsync(
                http,
                brokerPort,
                agentId,
                instanceId);

            var scope = new
            {
                allowedActions = new[] { MauiTestAgentActions.AuthorCommit },
                allowedSelectors = Array.Empty<string>(),
                allowedRoutes = Array.Empty<string>(),
                allowedSideEffectClasses = new[] { "authoring" },
                maxActionCount = 1,
                maxValueBytes = 0,
            };

            // The owner token is the only thing that can mint a capability, and the proxy has to
            // carry it through untouched.
            using var listed = await GetTrustedAsync(
                http,
                $"{inspectorBase}/api/workbench/agent-requests",
                hostToken);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

            using var issued = await PostTrustedJsonAsync(
                http,
                $"{inspectorBase}/api/workbench/approval-confirmations/issue",
                hostToken,
                new
                {
                    action = "agent-request-approve",
                    subjectId = approvalRequestId,
                    approvedScope = scope,
                    grantDurationSeconds = 120,
                });
            Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
            using var issuedBody = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
            var capability = issuedBody.RootElement.GetProperty("confirmationCapability").GetString();
            Assert.False(string.IsNullOrWhiteSpace(capability));

            using var approved = await PostTrustedJsonAsync(
                http,
                $"{inspectorBase}/api/workbench/agent-requests/{Uri.EscapeDataString(approvalRequestId)}/approve",
                hostToken,
                new
                {
                    humanConfirmed = true,
                    confirmationCapability = capability,
                    approvedScope = scope,
                    grantDurationSeconds = 120,
                    decidedBy = new { actorId = "maui-cli-operator", channel = "cli", provider = "maui-cli" },
                });
            Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
            using var approvedBody = JsonDocument.Parse(await approved.Content.ReadAsStringAsync());
            Assert.True(approvedBody.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                MauiTestAgentApprovalStates.Approved,
                approvedBody.RootElement.GetProperty("request").GetProperty("state").GetString());

            // Same capability, second attempt: it was consumed, so no second grant exists.
            using var replay = await PostTrustedJsonAsync(
                http,
                $"{inspectorBase}/api/workbench/agent-requests/{Uri.EscapeDataString(approvalRequestId)}/approve",
                hostToken,
                new
                {
                    humanConfirmed = true,
                    confirmationCapability = capability,
                    approvedScope = scope,
                    grantDurationSeconds = 120,
                });
            Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        }
        finally
        {
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task<string> QueueApprovalRequestAsync(
        HttpClient client,
        int brokerPort,
        string agentId,
        string instanceId)
    {
        var target = new
        {
            agentId,
            agentInstanceId = instanceId,
            appBuildFingerprint = "build-approve",
            seedFingerprint = "seed-approve",
        };
        var targetState = new
        {
            agentId,
            agentInstanceId = instanceId,
            appBuildFingerprint = "build-approve",
            seedFingerprint = "seed-approve",
            route = "//home",
            window = "main-window",
        };
        var provenance = new
        {
            actorKind = "agent",
            actorId = "test-agent",
            channel = "unit-test",
            provider = "xunit",
        };

        using var begun = await PostJsonAsync(
            client,
            $"http://127.0.0.1:{brokerPort}/api/test-agent/sessions/begin",
            new
            {
                envelope = new
                {
                    requestId = "req-broker-approve-begin",
                    idempotencyKey = "idem-broker-approve-begin",
                    target,
                    correlation = new { },
                    provenance,
                    intent = "begin",
                    deadlineMs = 30_000,
                    policyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
                },
                targetState,
            });
        Assert.True(begun.IsSuccessStatusCode, await begun.Content.ReadAsStringAsync());
        using var begunBody = JsonDocument.Parse(await begun.Content.ReadAsStringAsync());
        var snapshot = begunBody.RootElement.GetProperty("snapshot");
        var plan = snapshot.GetProperty("plan");
        var canonicalTarget = snapshot.GetProperty("target");

        using var submitted = await PostJsonAsync(
            client,
            $"http://127.0.0.1:{brokerPort}/api/test-agent/approvals/request",
            new
            {
                envelope = new
                {
                    requestId = "req-broker-approve",
                    idempotencyKey = "idem-broker-approve",
                    readCapabilityId = snapshot.GetProperty("readCapabilityId").GetString(),
                    target = new
                    {
                        agentId = canonicalTarget.GetProperty("agentId").GetString(),
                        agentInstanceId = canonicalTarget.GetProperty("agentInstanceId").GetString(),
                        appBuildFingerprint = canonicalTarget.GetProperty("appBuildFingerprint").GetString(),
                        seedFingerprint = canonicalTarget.TryGetProperty("seedFingerprint", out var seed)
                            ? seed.GetString()
                            : null,
                    },
                    correlation = new
                    {
                        authoringSessionId = snapshot.GetProperty("sessionId").GetString(),
                        planId = plan.GetProperty("planId").GetString(),
                        planRevision = plan.GetProperty("revision").GetInt32(),
                        planDigest = snapshot.GetProperty("planDigest").GetString(),
                        flowId = plan.GetProperty("flow").GetProperty("flowId").GetString(),
                        flowRevision = snapshot.GetProperty("flowRevision").GetInt32(),
                        flowDigest = snapshot.GetProperty("flowDigest").GetString(),
                    },
                    provenance,
                    intent = "commit the reviewed flow",
                    deadlineMs = 30_000,
                    policyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
                },
                kind = MauiTestAgentApprovalKinds.Commit,
                scope = new
                {
                    allowedActions = new[] { MauiTestAgentActions.AuthorCommit },
                    allowedSideEffectClasses = new[] { "authoring" },
                    maxActionCount = 1,
                    maxValueBytes = 0,
                },
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
        Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync());
        using var submittedBody = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        Assert.True(submittedBody.RootElement.GetProperty("ok").GetBoolean());
        return submittedBody.RootElement.GetProperty("request").GetProperty("approvalRequestId").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object value)
        => await client.PostAsync(url, new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"));

    private static async Task<HttpResponseMessage> PostTrustedJsonAsync(
        HttpClient client,
        string url,
        string hostApprovalToken,
        object value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("X-DevFlow-Host-Approval-Token", hostApprovalToken);
        request.Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetTrustedAsync(
        HttpClient client,
        string url,
        string hostApprovalToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-DevFlow-Host-Approval-Token", hostApprovalToken);
        return await client.SendAsync(request);
    }

    private static async Task<InspectorServer> GetInspectorAsync(BrokerServer broker, string agentId)
    {
        var field = typeof(BrokerServer).GetField("_inspectors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var inspectors = (IReadOnlyDictionary<string, InspectorServer>)field.GetValue(broker)!;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (inspectors.TryGetValue(agentId, out var inspector))
                return inspector;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Broker did not create the Inspector.");
    }

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
        => await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<string> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static int FreePort() => TestPorts.Reserve();
}
