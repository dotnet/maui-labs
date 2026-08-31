using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Direct tests for the non-visual approval ceremony. These drive the Inspector's HTTP surface
/// exactly as <c>maui devflow approve</c> does, because that surface — not any UI — is what decides
/// whether an agent request becomes a grant in this layer.
/// </summary>
public sealed class InspectorApprovalConfirmationTests
{
    private const string HostToken = "owner-token-for-approval-confirmation-tests";
    private const string AgentId = "agent-a";
    private const string AgentInstanceId = "instance-a";

    [Fact]
    public async Task ConfirmationIssue_RequiresTheOwnerToken()
    {
        using var fixture = await ApprovalFixture.StartAsync();

        using var missing = await fixture.PostAsync(
            "/api/workbench/approval-confirmations/issue",
            fixture.IssueBody(),
            hostToken: null);
        using var wrong = await fixture.PostAsync(
            "/api/workbench/approval-confirmations/issue",
            fixture.IssueBody(),
            hostToken: HostToken + "x");
        using var browserRead = await fixture.PostAsync(
            "/api/workbench/approval-confirmations/issue",
            fixture.IssueBody(),
            hostToken: null,
            readToken: fixture.ReadToken);

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        // The browser read token reaches the handler but still cannot mint a capability.
        Assert.Equal(HttpStatusCode.Forbidden, browserRead.StatusCode);
        Assert.Equal("trusted-host-required", await CodeAsync(browserRead));
    }

    [Fact]
    public async Task ConfirmationIssue_MintsASingleUseCapabilityBoundToTheExactTarget()
    {
        using var fixture = await ApprovalFixture.StartAsync();

        using var issued = await fixture.PostAsync(
            "/api/workbench/approval-confirmations/issue",
            fixture.IssueBody(),
            HostToken);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);

        using var payload = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var root = payload.RootElement;
        var capability = root.GetProperty("confirmationCapability").GetString();

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.NotNull(capability);
        Assert.Matches("^[A-Za-z0-9_-]{32,}$", capability);
        Assert.Equal(64, root.GetProperty("confirmationDigest").GetString()!.Length);
        Assert.Equal(AgentId, root.GetProperty("target").GetProperty("agentId").GetString());
        Assert.Equal(AgentInstanceId, root.GetProperty("target").GetProperty("agentInstanceId").GetString());

        using var second = await fixture.PostAsync(
            "/api/workbench/approval-confirmations/issue",
            fixture.IssueBody(),
            HostToken);
        using var secondPayload = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.NotEqual(
            capability,
            secondPayload.RootElement.GetProperty("confirmationCapability").GetString());
    }

    [Fact]
    public async Task ConfirmationIssue_RefusesAnUnknownActionOrForeignSubject()
    {
        using var fixture = await ApprovalFixture.StartAsync();

        using var unknownAction = await fixture.PostAsync(
            "/api/workbench/approval-confirmations/issue",
            fixture.IssueBody(action: "repair-grant"),
            HostToken);
        using var foreignSubject = await fixture.PostAsync(
            "/api/workbench/approval-confirmations/issue",
            fixture.IssueBody(subjectId: "approval_not_queued_here"),
            HostToken);

        Assert.Equal(HttpStatusCode.BadRequest, unknownAction.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignSubject.StatusCode);
    }

    [Fact]
    public async Task Approve_WithHumanConfirmedAndBrowserTokenButNoCapability_IssuesNoGrant()
    {
        using var fixture = await ApprovalFixture.StartAsync();

        using var response = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/approve",
            fixture.ApproveBody(capability: null),
            hostToken: null,
            readToken: fixture.ReadToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("trusted-host-required", await CodeAsync(response));
        Assert.Equal(
            MauiTestAgentApprovalStates.Pending,
            fixture.CurrentApprovalState());
        Assert.Null(fixture.CurrentGrantId());
    }

    [Fact]
    public async Task Approve_WithAForgedCapability_IssuesNoGrant()
    {
        using var fixture = await ApprovalFixture.StartAsync();

        using var response = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/approve",
            fixture.ApproveBody(capability: "forged-capability-value-000000000000"),
            HostToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("approval-confirmation-invalid", await CodeAsync(response));
        Assert.Equal(MauiTestAgentApprovalStates.Pending, fixture.CurrentApprovalState());
    }

    [Fact]
    public async Task Approve_WithACapabilityForADifferentScope_IssuesNoGrant()
    {
        using var fixture = await ApprovalFixture.StartAsync();
        var capability = await fixture.IssueCapabilityAsync();

        using var response = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/approve",
            fixture.ApproveBody(capability, allowedActions: ["tap", "fill"]),
            HostToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("approval-confirmation-invalid", await CodeAsync(response));
        Assert.Equal(MauiTestAgentApprovalStates.Pending, fixture.CurrentApprovalState());
    }

    [Fact]
    public async Task Approve_WithACapabilityForADifferentGrantLength_IssuesNoGrant()
    {
        using var fixture = await ApprovalFixture.StartAsync();
        var capability = await fixture.IssueCapabilityAsync(grantDurationSeconds: 60);

        using var response = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/approve",
            fixture.ApproveBody(capability, grantDurationSeconds: 900),
            HostToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("approval-confirmation-invalid", await CodeAsync(response));
        Assert.Equal(MauiTestAgentApprovalStates.Pending, fixture.CurrentApprovalState());
    }

    [Fact]
    public async Task Approve_WithACapabilityMintedForAnotherRequest_IssuesNoGrant()
    {
        using var fixture = await ApprovalFixture.StartAsync();
        var otherRequestId = fixture.QueueSecondApprovalRequest();
        var capability = await fixture.IssueCapabilityAsync(subjectId: otherRequestId);

        using var response = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/approve",
            fixture.ApproveBody(capability),
            HostToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("approval-confirmation-invalid", await CodeAsync(response));
        Assert.Equal(MauiTestAgentApprovalStates.Pending, fixture.CurrentApprovalState());
    }

    /// <summary>
    /// This is the exact two-call sequence <c>maui devflow approve</c> performs: mint with the
    /// owner token, then redeem once. The command is operator convenience, so what the test proves
    /// is that the capability — not the command, the channel, or <c>humanConfirmed</c> — is what
    /// carries authority, and that it cannot be replayed.
    /// </summary>
    [Fact]
    public async Task ApproveCeremony_SucceedsOnceWithTheOwnerTokenAndCannotBeReplayed()
    {
        using var fixture = await ApprovalFixture.StartAsync();
        var capability = await fixture.IssueCapabilityAsync();

        using var approved = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/approve",
            fixture.ApproveBody(capability),
            HostToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using var payload = JsonDocument.Parse(await approved.Content.ReadAsStringAsync());
        Assert.True(payload.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(MauiTestAgentApprovalStates.Approved, fixture.CurrentApprovalState());
        Assert.NotNull(fixture.CurrentGrantId());

        // The audit records which surface says it decided. That label is self-asserted provenance
        // and is never consulted by an authorization check.
        Assert.NotNull(fixture.CurrentDecidedBy());

        using var replay = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/approve",
            fixture.ApproveBody(capability),
            HostToken);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal("approval-confirmation-invalid", await CodeAsync(replay));
    }

    [Fact]
    public async Task Reject_RequiresATokenAndNeverMintsAGrant()
    {
        using var fixture = await ApprovalFixture.StartAsync();

        using var browserOnly = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/reject",
            "{\"humanConfirmed\":true,\"reasonCode\":\"host-rejected\"}",
            hostToken: null);
        Assert.Equal(HttpStatusCode.Forbidden, browserOnly.StatusCode);

        using var rejected = await fixture.PostAsync(
            $"/api/workbench/agent-requests/{fixture.ApprovalRequestId}/reject",
            "{\"humanConfirmed\":true,\"reasonCode\":\"host-rejected\"}",
            HostToken);

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(MauiTestAgentApprovalStates.Rejected, fixture.CurrentApprovalState());
        Assert.Null(fixture.CurrentGrantId());
    }

    [Fact]
    public async Task ConfirmationIssue_ReportsApprovalUnavailableWhenNoOwnerTokenIsConfigured()
    {
        using var fixture = await ApprovalFixture.StartAsync(withHostApproval: false);

        using var response = await fixture.PostAsync(
            "/api/workbench/approval-confirmations/issue",
            fixture.IssueBody(),
            hostToken: null,
            readToken: fixture.ReadToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("native-host-approval-unavailable", await CodeAsync(response));
    }

    /// <summary>
    /// The confirmation digest is what binds a decision to the exact scope that was reviewed, so
    /// two different scopes must never canonicalize to the same bytes. Joining a list with a
    /// separator and length-prefixing only the joined field is not enough: one entry containing the
    /// separator produces the same field as several entries that do not, which would let a scope be
    /// reshaped between review and redemption under an unchanged digest.
    /// </summary>
    [Theory]
    [InlineData("allowedActions")]
    [InlineData("allowedSelectors")]
    [InlineData("allowedRoutes")]
    [InlineData("allowedSideEffectClasses")]
    public void CanonicalScope_SeparatesOneEmbeddedNewlineEntryFromTwoEntries(string list)
    {
        var single = InspectorServer.CanonicalScope(ScopeWith(list, ["a\nb"]));
        var split = InspectorServer.CanonicalScope(ScopeWith(list, ["a", "b"]));

        Assert.NotEqual(single, split);
    }

    [Fact]
    public void CanonicalScope_SeparatesListBoundariesAcrossFields()
    {
        var actionsCarryBoth = new MauiTestAgentMutationScope
        {
            AllowedActions = ["tap", "scroll"],
            AllowedSelectors = [],
            MaxActionCount = 1,
            MaxValueBytes = 0,
        };
        var splitAcrossFields = new MauiTestAgentMutationScope
        {
            AllowedActions = ["tap"],
            AllowedSelectors = ["scroll"],
            MaxActionCount = 1,
            MaxValueBytes = 0,
        };

        Assert.NotEqual(
            InspectorServer.CanonicalScope(actionsCarryBoth),
            InspectorServer.CanonicalScope(splitAcrossFields));
    }

    [Fact]
    public void CanonicalScope_IsOrderIndependentAndValueSensitive()
    {
        var ascending = ScopeWith("allowedSelectors", ["automationId:a", "automationId:b"]);
        var descending = ScopeWith("allowedSelectors", ["automationId:b", "automationId:a"]);
        var widened = ScopeWith("allowedSelectors", ["automationId:a", "automationId:b", "automationId:c"]);

        Assert.Equal(
            InspectorServer.CanonicalScope(ascending),
            InspectorServer.CanonicalScope(descending));
        Assert.NotEqual(
            InspectorServer.CanonicalScope(ascending),
            InspectorServer.CanonicalScope(widened));
    }

    private static MauiTestAgentMutationScope ScopeWith(string list, string[] values)
    {
        var scope = new MauiTestAgentMutationScope
        {
            MaxActionCount = 1,
            MaxValueBytes = 0,
        };
        switch (list)
        {
            case "allowedActions":
                scope.AllowedActions = [.. values];
                break;
            case "allowedSelectors":
                scope.AllowedSelectors = [.. values];
                break;
            case "allowedRoutes":
                scope.AllowedRoutes = [.. values];
                break;
            case "allowedSideEffectClasses":
                scope.AllowedSideEffectClasses = [.. values];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(list), list, "Unknown scope list.");
        }
        return scope;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed class ApprovalFixture : IDisposable
    {
        private readonly HttpClient _http = new();

        private ApprovalFixture(
            InspectorServer inspector,
            TestAgentSessionService sessions,
            string approvalRequestId,
            string readToken)
        {
            Inspector = inspector;
            Sessions = sessions;
            ApprovalRequestId = approvalRequestId;
            ReadToken = readToken;
        }

        public InspectorServer Inspector { get; }
        public TestAgentSessionService Sessions { get; }
        public string ApprovalRequestId { get; }
        public string ReadToken { get; }
        public MauiTestAgentAuthoringSnapshot? Snapshot { get; init; }

        public static async Task<ApprovalFixture> StartAsync(bool withHostApproval = true)
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
                Envelope = Envelope("approve-ceremony", snapshot),
                Kind = MauiTestAgentApprovalKinds.Commit,
                Scope = Scope(),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            Assert.True(submitted.Ok, submitted.Error?.Message);

            var inspector = new InspectorServer(
                TestPorts.Reserve(),
                "127.0.0.1",
                TestPorts.Reserve(),
                embedToken: null,
                agentId: AgentId,
                appName: "Approval Test",
                platform: "android",
                project: "approval-test",
                sessionId: null,
                agentInstanceId: AgentInstanceId,
                testAgentSessions: sessions,
                testAgentTargetStateRefresh: state => Task.FromResult<MauiTestAgentTargetState?>(state),
                previewFlags: PreviewTestFeatures.AllEnabled(),
                trustedHostApprovalVerifier: withHostApproval
                    ? supplied => string.Equals(supplied, HostToken, StringComparison.Ordinal)
                    : null);
            inspector.Start();

            var readToken = (string)typeof(InspectorServer)
                .GetField("_readToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(inspector)!;

            var fixture = new ApprovalFixture(
                inspector,
                sessions,
                submitted.Request!.ApprovalRequestId!,
                readToken)
            {
                Snapshot = snapshot,
            };
            await fixture.WaitForListenerAsync();
            return fixture;
        }

        public string QueueSecondApprovalRequest()
        {
            var submitted = Sessions.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
            {
                Envelope = Envelope("second-approve-ceremony", Snapshot!),
                Kind = MauiTestAgentApprovalKinds.Commit,
                Scope = Scope(),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            Assert.True(submitted.Ok, submitted.Error?.Message);
            return submitted.Request!.ApprovalRequestId!;
        }

        public string IssueBody(
            string action = "agent-request-approve",
            string? subjectId = null,
            int grantDurationSeconds = 120)
            => "{\"action\":\"" + action + "\",\"subjectId\":\"" + (subjectId ?? ApprovalRequestId) +
               "\",\"approvedScope\":" + ScopeJson(["author-commit"]) +
               ",\"grantDurationSeconds\":" + grantDurationSeconds + "}";

        public string ApproveBody(
            string? capability,
            string[]? allowedActions = null,
            int grantDurationSeconds = 120)
        {
            var capabilityJson = capability is null ? "null" : $"\"{capability}\"";
            return "{\"humanConfirmed\":true,\"confirmationCapability\":" + capabilityJson +
                   ",\"approvedScope\":" + ScopeJson(allowedActions ?? ["author-commit"]) +
                   ",\"grantDurationSeconds\":" + grantDurationSeconds +
                   ",\"decidedBy\":{\"actorId\":\"maui-cli-operator\",\"channel\":\"cli\"," +
                   "\"provider\":\"maui-cli\"}}";
        }

        public async Task<string> IssueCapabilityAsync(
            string? subjectId = null,
            int grantDurationSeconds = 120)
        {
            using var issued = await PostAsync(
                "/api/workbench/approval-confirmations/issue",
                IssueBody(subjectId: subjectId, grantDurationSeconds: grantDurationSeconds),
                HostToken);
            Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
            using var payload = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
            return payload.RootElement.GetProperty("confirmationCapability").GetString()!;
        }

        public async Task<HttpResponseMessage> PostAsync(
            string path,
            string body,
            string? hostToken,
            string? readToken = null)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{Inspector.Port}{path}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (hostToken is not null)
                request.Headers.TryAddWithoutValidation("X-DevFlow-Host-Approval-Token", hostToken);
            if (readToken is not null)
                request.Headers.TryAddWithoutValidation("X-DevFlow-Inspector-Token", readToken);
            return await _http.SendAsync(request);
        }

        public string? CurrentApprovalState()
            => Sessions.GetApprovalRequest(ApprovalRequestId, includeGrant: false).Request?.State;

        public string? CurrentGrantId()
            => Sessions.GetApprovalRequest(ApprovalRequestId, includeGrant: true).Request?.GrantId;

        public string? CurrentDecidedBy()
            => Sessions.GetApprovalRequest(ApprovalRequestId, includeGrant: false).Request?.DecidedBy;

        public void Dispose()
        {
            _http.Dispose();
            Inspector.Dispose();
        }

        private async Task WaitForListenerAsync()
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using var probe = await _http.GetAsync(
                        $"http://127.0.0.1:{Inspector.Port}/api/workbench/agent-requests");
                    return;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(25);
                }
            }
            throw new InvalidOperationException("The Inspector did not start listening.");
        }

        private static string ScopeJson(string[] allowedActions)
        {
            var actions = string.Join(",", allowedActions.Select(action => $"\"{action}\""));
            return "{\"allowedActions\":[" + actions + "],\"allowedSelectors\":[]," +
                "\"allowedRoutes\":[\"//home\"],\"allowedSideEffectClasses\":[\"commit\"]," +
                "\"maxActionCount\":1,\"maxValueBytes\":0}";
        }

        private static MauiTestAgentMutationScope Scope() => new()
        {
            AllowedActions = [MauiTestAgentActions.AuthorCommit],
            AllowedRoutes = ["//home"],
            AllowedSideEffectClasses = ["commit"],
            MaxActionCount = 1,
            MaxValueBytes = 0,
        };

        private static MauiTestAgentRequestEnvelope Envelope(
            string intent,
            MauiTestAgentAuthoringSnapshot? snapshot = null) => new()
        {
            ReadCapabilityId = snapshot?.ReadCapabilityId,
            RequestId = "req-" + intent,
            IdempotencyKey = "idem-" + intent,
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
    }
}
