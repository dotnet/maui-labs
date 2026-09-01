using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Approvals;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Testing;
using Spectre.Console;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Drives the real <c>maui devflow approve</c> decision path against a real Inspector behind a
/// broker-shaped proxy. The point is the bodies the command builds: the confirmation digest covers
/// the approved scope and grant length, so a command that re-serialized or re-ordered either would
/// mint a capability it cannot then redeem.
///
/// <para>
/// It is equally important that this command is <b>not</b> an authorization boundary. An agent runs
/// as the same OS user and can read the same broker token, so the tests below assert that the
/// command keeps saying so.
/// </para>
/// </summary>
public sealed class ApprovalCommandsRouteTests
{
    private const string HostToken = "owner-token-for-cli-approval-route-tests";
    private const string AgentId = "agent-a";
    private const string AgentInstanceId = "instance-a";

    [Fact]
    public async Task Approve_SucceedsWithTheOwnerTokenAndIssuesExactlyOneGrant()
    {
        using var fixture = await Fixture.StartAsync();

        var output = new CapturingOutputWriter();
        var failed = false;
        await fixture.ApproveAsync(output, () => failed = true);

        Assert.False(failed, string.Join(" | ", output.Errors));
        Assert.Empty(output.Errors);

        var record = fixture.Sessions
            .GetApprovalRequest(fixture.ApprovalRequestId, includeGrant: true)
            .Request;
        Assert.Equal(MauiTestAgentApprovalStates.Approved, record?.State);
        Assert.NotNull(record?.GrantId);
    }

    [Fact]
    public async Task Approve_WithoutTheOwnerToken_IssuesNoGrant()
    {
        using var fixture = await Fixture.StartAsync();

        var output = new CapturingOutputWriter();
        var failed = false;
        await fixture.ApproveAsync(output, () => failed = true, hostToken: "not-the-owner-token");

        Assert.True(failed);
        Assert.NotEmpty(output.Errors);
        Assert.Equal(
            MauiTestAgentApprovalStates.Pending,
            fixture.Sessions.GetApprovalRequest(fixture.ApprovalRequestId, includeGrant: false).Request?.State);
    }

    [Fact]
    public async Task Reject_SucceedsWithTheOwnerTokenAndIssuesNoGrant()
    {
        using var fixture = await Fixture.StartAsync();

        var output = new CapturingOutputWriter();
        var failed = false;
        await fixture.RejectAsync(output, () => failed = true);

        Assert.False(failed, string.Join(" | ", output.Errors));
        var record = fixture.Sessions
            .GetApprovalRequest(fixture.ApprovalRequestId, includeGrant: true)
            .Request;
        Assert.Equal(MauiTestAgentApprovalStates.Rejected, record?.State);
        Assert.Null(record?.GrantId);
    }

    [Fact]
    public void Command_KeepsLabellingItselfAsConvenienceRatherThanABoundary()
    {
        var notice = (string)typeof(ApprovalCommands)
            .GetField("NotABoundaryNotice", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        Assert.Contains("not an authorization boundary", notice, StringComparison.Ordinal);
        Assert.Contains("same OS user", notice, StringComparison.Ordinal);
    }

    private sealed class CapturingOutputWriter : IDevFlowOutputWriter
    {
        public List<string> Errors { get; } = [];

        public bool ResolveJsonMode(bool jsonFlag, bool noJsonFlag) => true;
        public void WriteResult<T>(T data, bool json, Action<T>? humanFormatter = null) { }
        public void WriteResult<T>(T data, bool json, Action<T, IAnsiConsole> humanFormatter) { }
        public void WriteRawJson(string jsonString) { }
        public void WriteJsonElement(JsonElement element, bool json) { }
        public void WriteActionResult(bool success, string action, string? elementId, bool json, string? humanMessage = null) { }
        public void WriteJsonLine<T>(T data) { }
        public string FormatJson<T>(T data) => "";

        public void WriteError(
            string message,
            bool json,
            string errorType = "RuntimeError",
            bool retryable = false,
            string[]? suggestions = null)
            => Errors.Add($"{errorType}: {message}");
    }

    /// <summary>
    /// A minimal stand-in for the broker's Inspector proxy: it forwards
    /// <c>/inspector/{agentId}{path}</c> to a real <see cref="InspectorServer"/> exactly as the
    /// broker does, including the owner-token header, so the CLI exercises its real route shape.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly HttpClient _http = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly InspectorServer _inspector;
        private Task? _pump;

        private Fixture(
            InspectorServer inspector,
            TestAgentSessionService sessions,
            string approvalRequestId,
            int proxyPort)
        {
            _inspector = inspector;
            Sessions = sessions;
            ApprovalRequestId = approvalRequestId;
            ProxyPort = proxyPort;
        }

        public TestAgentSessionService Sessions { get; }
        public string ApprovalRequestId { get; }
        public int ProxyPort { get; }

        public static async Task<Fixture> StartAsync()
        {
            var sessions = new TestAgentSessionService();
            var begin = sessions.Begin(new MauiTestAgentSessionBeginRequest
            {
                Envelope = Envelope("begin"),
                TargetState = TargetState(),
            });
            Assert.True(begin.Ok, begin.Error?.Message);
            var snapshot = begin.Snapshot!;

            var submitted = sessions.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
            {
                Envelope = Envelope("cli-approve", snapshot),
                Kind = MauiTestAgentApprovalKinds.Commit,
                Scope = new MauiTestAgentMutationScope
                {
                    AllowedActions = [MauiTestAgentActions.AuthorCommit],
                    AllowedRoutes = ["//home"],
                    AllowedSideEffectClasses = ["authoring"],
                    MaxActionCount = 1,
                    MaxValueBytes = 0,
                },
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            Assert.True(submitted.Ok, submitted.Error?.Message);

            var inspector = new InspectorServer(
                FreePort(),
                "127.0.0.1",
                FreePort(),
                embedToken: null,
                agentId: AgentId,
                appName: "CLI Approval Test",
                platform: "android",
                project: "cli-approval-test",
                sessionId: null,
                agentInstanceId: AgentInstanceId,
                testAgentSessions: sessions,
                testAgentTargetStateRefresh: state => Task.FromResult<MauiTestAgentTargetState?>(state),
                previewFlags: MauiPreviewFeatureFlagConfiguration.FromEnvironment(
                    name => name == "DEVFLOW_PREVIEW_AGENT_AUTHORING" ? "true" : null),
                trustedHostApprovalVerifier: supplied =>
                    string.Equals(supplied, HostToken, StringComparison.Ordinal));
            inspector.Start();

            var fixture = new Fixture(inspector, sessions, submitted.Request!.ApprovalRequestId!, FreePort());
            fixture.StartProxy();
            return await Task.FromResult(fixture);
        }

        public async Task ApproveAsync(
            IDevFlowOutputWriter output,
            Action onError,
            string hostToken = HostToken,
            int grantSeconds = 120)
            => await InvokeAsync("ApproveAsync", output, onError, hostToken, grantSeconds);

        public async Task RejectAsync(
            IDevFlowOutputWriter output,
            Action onError,
            string hostToken = HostToken)
            => await InvokeAsync("RejectAsync", output, onError, hostToken, null);

        public void Dispose()
        {
            _cancellation.Cancel();
            try { _listener.Stop(); } catch { }
            _listener.Close();
            _http.Dispose();
            _inspector.Dispose();
            _cancellation.Dispose();
        }

        private async Task InvokeAsync(
            string methodName,
            IDevFlowOutputWriter output,
            Action onError,
            string hostToken,
            int? grantSeconds)
        {
            var record = await FindRecordAsync();
            var context = CreateContext(hostToken);
            var method = typeof(ApprovalCommands)
                .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            var arguments = grantSeconds is { } seconds
                ? new object?[] { context, record, seconds, true, output, true, onError, CancellationToken.None }
                : new object?[] { context, record, "test-rejection", true, output, true, onError, CancellationToken.None };
            await (Task)method.Invoke(null, arguments)!;
        }

        private object CreateContext(string hostToken)
        {
            var contextType = typeof(ApprovalCommands)
                .GetNestedType("ApprovalContext", BindingFlags.NonPublic)!;
            return Activator.CreateInstance(contextType, ProxyPort, AgentId, hostToken)!;
        }

        private async Task<object> FindRecordAsync()
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://127.0.0.1:{ProxyPort}/inspector/{Uri.EscapeDataString(AgentId)}/api/workbench/agent-requests");
            request.Headers.TryAddWithoutValidation("X-DevFlow-Host-Approval-Token", HostToken);
            using var response = await _http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var listing = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var method = typeof(ApprovalCommands)
                .GetMethod("TryFindRequest", BindingFlags.Static | BindingFlags.NonPublic)!;
            var arguments = new object?[] { listing.RootElement.Clone(), ApprovalRequestId, null, null };
            Assert.True((bool)method.Invoke(null, arguments)!);
            return arguments[2]!;
        }

        private void StartProxy()
        {
            _listener.Prefixes.Add($"http://127.0.0.1:{ProxyPort}/");
            _listener.Prefixes.Add($"http://localhost:{ProxyPort}/");
            _listener.Start();
            _pump = Task.Run(async () =>
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); }
                    catch { return; }
                    _ = ForwardAsync(context);
                }
            });
        }

        private async Task ForwardAsync(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url!.AbsolutePath;
                var prefix = $"/inspector/{Uri.EscapeDataString(AgentId)}";
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                using var forwarded = new HttpRequestMessage(
                    new HttpMethod(context.Request.HttpMethod),
                    $"http://127.0.0.1:{_inspector.Port}{path[prefix.Length..]}");
                if (context.Request.HasEntityBody)
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    forwarded.Content = new StringContent(
                        await reader.ReadToEndAsync(),
                        Encoding.UTF8,
                        "application/json");
                }
                var hostToken = context.Request.Headers["X-DevFlow-Host-Approval-Token"];
                if (!string.IsNullOrEmpty(hostToken))
                    forwarded.Headers.TryAddWithoutValidation("X-DevFlow-Host-Approval-Token", hostToken);

                using var response = await _http.SendAsync(forwarded);
                var payload = await response.Content.ReadAsByteArrayAsync();
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
            }
            catch
            {
                try { context.Response.StatusCode = 502; } catch { }
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private static MauiTestAgentRequestEnvelope Envelope(
            string intent,
            MauiTestAgentAuthoringSnapshot? snapshot = null) => new()
        {
            RequestId = "req-" + intent,
            IdempotencyKey = "idem-" + intent,
            ReadCapabilityId = snapshot?.ReadCapabilityId,
            Target = new MauiTestAgentTarget
            {
                AgentId = AgentId,
                AgentInstanceId = AgentInstanceId,
                AppBuildFingerprint = "build-a",
                SeedFingerprint = "seed-a",
            },
            Correlation = snapshot is null
                ? new MauiTestAgentCorrelation()
                : new MauiTestAgentCorrelation
                {
                    AuthoringSessionId = snapshot.SessionId,
                    PlanId = snapshot.Plan!.PlanId,
                    PlanRevision = snapshot.Plan.Revision,
                    PlanDigest = snapshot.PlanDigest,
                    FlowId = snapshot.Plan.Flow!.FlowId,
                    FlowRevision = snapshot.FlowRevision,
                    FlowDigest = snapshot.FlowDigest,
                },
            Intent = intent,
            DeadlineMs = 30_000,
            PolicyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
            Provenance = new MauiActorProvenance
            {
                ActorKind = "agent",
                ActorId = "test-agent",
                Channel = "unit-test",
                Provider = "xunit",
            },
        };

        private static MauiTestAgentTargetState TargetState() => new()
        {
            AgentId = AgentId,
            AgentInstanceId = AgentInstanceId,
            AppBuildFingerprint = "build-a",
            SeedFingerprint = "seed-a",
            Route = "//home",
            Window = "main-window",
        };

        private static int FreePort()
        {
            using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            return port;
        }
    }
}
