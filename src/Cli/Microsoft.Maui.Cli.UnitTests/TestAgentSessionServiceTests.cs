using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class TestAgentSessionServiceTests
{
    [Fact]
    public void ToolInventory_TestAgentProfileHasOnlyRestrictedTools()
    {
        var enabled = new MauiPreviewFeatureFlags { AgentAuthoringEnabled = true };
        var full = McpServerHost.GetToolInventory(McpServerProfile.Full);
        var restricted = McpServerHost.GetToolInventory(McpServerProfile.TestAgent, enabled);

        Assert.Contains("maui_secure_storage_set", full);
        Assert.Contains("maui_cdp_evaluate", full);
        Assert.Contains("maui_test_action", restricted);
        Assert.Contains("maui_test_agents", restricted);
        Assert.Contains("maui_test_status", restricted);
        Assert.Contains("maui_test_explore", restricted);
        Assert.DoesNotContain("maui_secure_storage_set", restricted);
        Assert.DoesNotContain("maui_preferences_set", restricted);
        Assert.DoesNotContain("maui_files_download", restricted);
        Assert.DoesNotContain("maui_network_detail", restricted);
        Assert.DoesNotContain("maui_cdp_evaluate", restricted);
        Assert.DoesNotContain("maui_extension_call", restricted);
        Assert.DoesNotContain("maui_invoke_action", restricted);
        Assert.DoesNotContain("maui_set_property", restricted);
        Assert.DoesNotContain("maui_evidence_capture", restricted);

        // Golden inventory. The restricted profile is an allow-list, so a tool that appears here
        // without a deliberate edit to this list is a policy change, not a refactor.
        Assert.Equal(
            [
                "maui_test_action",
                "maui_test_agents",
                "maui_test_assertion",
                "maui_test_author",
                "maui_test_capabilities",
                "maui_test_explore",
                "maui_test_failure",
                "maui_test_improvements",
                "maui_test_layout_diagnostics",
                "maui_test_patch",
                "maui_test_run",
                "maui_test_status",
                "maui_test_trace",
                "maui_test_validate",
            ],
            restricted);
        Assert.Equal(14, restricted.Count);
    }

    [Fact]
    public async Task ToolInventory_DisabledAgentAuthoring_OmitsAndRefusesTestAgentProfile()
    {
        var disabled = MauiPreviewFeatureFlags.CreateDefault();

        Assert.False(McpServerHost.IsProfileEnabled(McpServerProfile.TestAgent, disabled));
        Assert.Empty(McpServerHost.GetToolInventory(McpServerProfile.TestAgent, disabled));
        var error = await Assert.ThrowsAsync<McpProfileDisabledException>(
            () => McpServerHost.RunAsync(McpServerProfile.TestAgent, disabled));
        // The refusal has to name the exact variable a caller must set; "enable the effective
        // agent-authoring preview flag" left the reader with nothing to act on.
        Assert.Contains(
            McpServerHost.PreviewAgentAuthoringVariable,
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_PREVIEW_AGENT_AUTHORING", error.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<InvalidOperationException>(error);
    }

    [Fact]
    public void ProfileParsing_BlankOrUnsafeRestrictedProfilesFailClosed()
    {
        Assert.False(McpServerHost.TryParseProfile(null, out _));
        Assert.False(McpServerHost.TryParseProfile("   ", out _));
        Assert.False(McpServerHost.IsProfileEnabled(
            McpServerProfile.TestAgent,
            new MauiPreviewFeatureFlags
            {
                AgentAuthoringEnabled = true,
                AutoApplyRepair = true,
            }));
    }

    [Fact]
    public void PreviewKillSwitches_AreCaseInsensitiveBeforeAllowListing()
    {
        var flags = MauiPreviewFeatureFlagConfiguration.FromEnvironment(name => name switch
        {
            "DEVFLOW_PREVIEW_AGENT_AUTHORING" => "true",
            "DEVFLOW_PREVIEW_KILL_SWITCHES" => "AGENT-AUTHORING",
            _ => null,
        });

        Assert.Contains("agent-authoring", flags.KillSwitches);
        Assert.False(flags.IsEnabled("agent-authoring"));
        Assert.False(McpServerHost.IsProfileEnabled(McpServerProfile.TestAgent, flags));
    }

    [Fact]
    public void RestrictedTools_EveryParameterHasDescription()
    {
        // Driven by the authoritative registered-type list rather than a local copy, so a newly
        // registered tool type is covered here the moment it is registered. An MCP agent sees only
        // these descriptions, so an undescribed parameter is an unusable parameter.
        var types = McpServerHost.TestAgentToolTypes;
        Assert.Contains(typeof(TestAgentExplorationTool), types);

        var checkedParameters = 0;
        foreach (var method in types.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                     .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.NotNull(
                    parameter.GetCustomAttribute<DescriptionAttribute>());
                checkedParameters++;
            }
        }

        Assert.True(checkedParameters > 0, "No restricted tool parameters were inspected.");
    }

    [Fact]
    public void ToolSupport_SerializesProtocolResultsWithCamelCaseNames()
    {
        var json = TestAgentToolSupport.Success("req-status", new MauiTestAgentSessionResult
        {
            Ok = true,
            Snapshot = new MauiTestAgentAuthoringSnapshot
            {
                SessionId = "author-1",
                ApprovalRequests =
                [
                    new MauiTestAgentApprovalRecord
                    {
                        ApprovalRequestId = "approval-1",
                        State = MauiTestAgentApprovalStates.Approved,
                        GrantId = "grant-opaque",
                    },
                ],
            },
        });

        Assert.Contains("\"snapshot\"", json, StringComparison.Ordinal);
        Assert.Contains("\"approvalRequests\"", json, StringComparison.Ordinal);
        Assert.Contains("\"grantId\":\"grant-opaque\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Snapshot\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ApprovalRequests\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolSupport_BuildFingerprintMatchesRunnerCheckpointFormat()
    {
        Assert.Equal("1.2.3:42", TestAgentToolSupport.BuildAppFingerprint(new AgentStatus
        {
            App = new AppDescriptor { Version = "1.2.3", Build = "42" },
        }));
        Assert.Equal("unknown:42", TestAgentToolSupport.BuildAppFingerprint(new AgentStatus
        {
            App = new AppDescriptor { Build = "42" },
        }));
        Assert.Null(TestAgentToolSupport.BuildAppFingerprint(new AgentStatus
        {
            App = new AppDescriptor { Version = "unknown", Build = "unknown" },
        }));
    }

    [Fact]
    public void WorkflowRunRequests_AreCoveredByAotJsonMetadata()
    {
        var startJson = TestAgentBrokerClient.SerializeWorkflowRunRequest(
            new WorkflowRunStartRequest
            {
                AgentId = "agent-a",
                AgentInstanceId = "instance-a",
                IdempotencyKey = "run-a",
                Flow = new MauiFlow { Name = "Run A" },
                Plan = new MauiTestPlan { PlanId = "plan-a" },
            },
            DevFlowCliJsonContext.Default.WorkflowRunStartRequest);
        var accessJson = TestAgentBrokerClient.SerializeWorkflowRunRequest(
            new WorkflowRunAccessRequest { CapabilityToken = "run-token" },
            DevFlowCliJsonContext.Default.WorkflowRunAccessRequest);

        Assert.Contains("\"agentId\":\"agent-a\"", startJson, StringComparison.Ordinal);
        Assert.Contains("\"flow\":{\"schema\":2,\"name\":\"Run A\"", startJson, StringComparison.Ordinal);
        Assert.Contains("\"capabilityToken\":\"run-token\"", accessJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RunContext_NonReplayablePlanUsesApprovedOneShotAuthorization()
    {
        var oneShot = TestAgentRunTool.CreateRunContext(new MauiTestPlan
        {
            SideEffectPolicy = MauiFlowSideEffectPolicies.NonReplayable,
        },
        new MauiTestAgentTargetState
        {
            AgentInstanceId = "instance-a",
            AppBuildFingerprint = "build-a",
            Route = "//native",
            Window = "window-0",
        },
        new MauiTestAgentTargetState
        {
            AgentInstanceId = "instance-a",
            AppBuildFingerprint = "build-a",
            Route = "//native",
            Window = "window-0",
        });
        var resettable = TestAgentRunTool.CreateRunContext(new MauiTestPlan
        {
            SideEffectPolicy = MauiFlowSideEffectPolicies.TestTenantResettable,
        });

        Assert.Equal(MauiFlowReplayIntents.OrdinaryReplay, oneShot.Intent);
        Assert.True(oneShot.ManualOneShotAuthorization);
        Assert.Equal("build-a", oneShot.Preconditions?.Expected?.AppBuildFingerprint);
        Assert.Equal("build-a", oneShot.Preconditions?.Observed?.AppBuildFingerprint);
        var admission = MauiFlowReplaySafetyEvaluator.Evaluate(new MauiFlowRunRequest
        {
            Plan = new MauiTestPlan
            {
                PlanId = "one-shot-plan",
                Revision = 1,
                Flow = new MauiFlowReference
                {
                    Path = "flow.md",
                    Digest = new string('a', 64),
                },
                Goal = "Run one explicitly authorized replay.",
                SideEffectPolicy = MauiFlowSideEffectPolicies.NonReplayable,
                Reset = new MauiTestResetRequirement(),
                Provenance = new MauiActorProvenance
                {
                    ActorKind = "human",
                    Channel = "unit-test",
                },
            },
            Context = oneShot,
        });
        Assert.True(
            admission.OrdinaryReplayAllowed,
            string.Join("; ", admission.Reasons.Select(static reason => reason.Code)));
        Assert.Null(resettable.ManualOneShotAuthorization);
    }

    [Fact]
    public void FailureDiagnostic_DriveRejectionPreservesCanonicalFacts()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "run": {
                "state": "failed",
                "report": {
                  "outcome": { "status": "failed" },
                  "failure": {
                    "class": "drive-failed",
                    "code": "drive-failed",
                    "category": "action",
                    "phase": "dispatch",
                    "repairEligible": false,
                    "legacyKind": "drive",
                    "stepId": "1"
                  },
                  "steps": [
                    {
                      "stepId": "1",
                      "sequence": 1,
                      "action": "tap",
                      "selector": {
                        "automationId": "NewTodoEntry",
                        "quality": "durable"
                      },
                      "targetResolution": { "status": "resolved", "matchCount": 1 },
                      "actionability": [
                        { "visible": true, "enabled": true, "hasBounds": true },
                        { "boundsStable": true }
                      ],
                      "dispatch": {
                        "acknowledgementState": "rejected",
                        "completionCertainty": "completed"
                      },
                      "observedCheckpoint": { "route": "//native" }
                    }
                  ]
                }
              }
            }
            """);

        var diagnostic = TestAgentFailureTool.ReadDiagnostic(document.RootElement);

        Assert.Equal(MauiFlowFailureClasses.DriveFailed, diagnostic.Classification.Code);
        Assert.Equal("action", diagnostic.Classification.Category);
        Assert.Null(diagnostic.Facts.CheckpointMatches);
        Assert.Null(diagnostic.Facts.RouteMatches);
        Assert.Equal(1, diagnostic.FailedStep?.MatchCount);
        Assert.Equal("rejected", diagnostic.FailedStep?.AcknowledgementState);
        Assert.Contains("not a selector or route failure", diagnostic.PlainLanguage, StringComparison.Ordinal);
        Assert.False(diagnostic.SelectorRepair.Eligible);
        Assert.Contains("Do not create a selector repair", diagnostic.NextSafeAction, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureDiagnostic_FragileContainerTapRecommendsReviewInsteadOfRepair()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "run": {
                "state": "failed",
                "report": {
                  "outcome": { "status": "failed" },
                  "failure": {
                    "class": "drive-failed",
                    "phase": "dispatch",
                    "legacyKind": "drive",
                    "stepId": "3"
                  },
                  "steps": [
                    {
                      "stepId": "3",
                      "sequence": 3,
                      "action": "tap",
                      "selector": {
                        "typeIndex": { "type": "MainPage", "index": 0 },
                        "quality": "fragile"
                      },
                      "targetResolution": { "status": "resolved", "matchCount": 1 },
                      "actionability": [
                        { "visible": true, "enabled": true, "hasBounds": true, "boundsStable": true }
                      ],
                      "dispatch": {
                        "acknowledgementState": "rejected",
                        "completionCertainty": "completed"
                      }
                    }
                  ]
                }
              }
            }
            """);

        var diagnostic = TestAgentFailureTool.ReadDiagnostic(document.RootElement);

        Assert.True(diagnostic.FailedStep?.FragileSelector);
        Assert.Equal("typeIndex", diagnostic.FailedStep?.SelectorKind);
        Assert.Equal("MainPage", diagnostic.FailedStep?.SelectorType);
        Assert.Contains("recording likely captured the wrong control", diagnostic.PlainLanguage, StringComparison.Ordinal);
        Assert.Contains("durable app-owned selector", diagnostic.NextSafeAction, StringComparison.Ordinal);
        Assert.False(diagnostic.SelectorRepair.Eligible);
    }

    [Fact]
    public void FailureDiagnostic_VerifiedMissingSelectorIsRepairEligible()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "run": {
                "state": "failed",
                "report": {
                  "outcome": { "status": "failed" },
                  "failure": {
                    "class": "locator-not-found",
                    "code": "locator-not-found",
                    "category": "selector",
                    "phase": "resolution",
                    "legacyKind": "not-found",
                    "stepId": "2"
                  },
                  "steps": [
                    {
                      "stepId": "2",
                      "sequence": 2,
                      "action": "tap",
                      "targetResolution": { "status": "not-found", "matchCount": 0 },
                      "expectedCheckpoint": {
                        "agentInstanceId": "instance-a",
                        "route": "//native"
                      },
                      "observedCheckpoint": {
                        "agentInstanceId": "instance-a",
                        "route": "//native"
                      }
                    }
                  ]
                }
              }
            }
            """);

        var diagnostic = TestAgentFailureTool.ReadDiagnostic(document.RootElement);

        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, diagnostic.Classification.Code);
        Assert.True(diagnostic.Facts.CheckpointVerified);
        Assert.True(diagnostic.Facts.CheckpointMatches);
        Assert.True(diagnostic.Facts.RouteMatches);
        Assert.True(diagnostic.SelectorRepair.Eligible);
        Assert.Equal("eligible", diagnostic.SelectorRepair.Status);
    }

    [Fact]
    public void FailureDiagnostic_AdmissionRefusalIsNotReportedAsAWrongFailureShape()
    {
        // A broker-owned run never evaluates the plan's independent business oracles, so its
        // admission withholds repair even when the symptom is exactly the drifted action selector
        // repair exists for. Reporting that as "not a verified pre-dispatch missing-selector
        // failure" blames the classifier and sends the reader to inspect a step that is fine.
        using var document = JsonDocument.Parse(
            """
            {
              "run": {
                "state": "failed",
                "admission": {
                  "sideEffectPolicy": "none",
                  "ordinaryReplayAllowed": true,
                  "repairEligibility": false,
                  "reasons": [
                    { "code": "independent-oracle-missing", "scope": "verification" }
                  ]
                },
                "report": {
                  "outcome": { "status": "failed" },
                  "failure": {
                    "class": "locator-not-found",
                    "code": "locator-not-found",
                    "category": "selector",
                    "phase": "resolution",
                    "legacyKind": "not-found",
                    "stepId": "2"
                  },
                  "steps": [
                    {
                      "stepId": "2",
                      "sequence": 2,
                      "action": "tap",
                      "targetResolution": { "status": "not-found", "matchCount": 0 },
                      "expectedCheckpoint": { "agentInstanceId": "instance-a", "route": "//native" },
                      "observedCheckpoint": { "agentInstanceId": "instance-a", "route": "//native" }
                    }
                  ]
                }
              }
            }
            """);

        var diagnostic = TestAgentFailureTool.ReadDiagnostic(document.RootElement);

        Assert.False(diagnostic.SelectorRepair.Eligible);
        Assert.True(diagnostic.SelectorRepair.ClassifierEligible);
        Assert.False(diagnostic.SelectorRepair.AdmissionEligible);
        Assert.Contains("independent-oracle-missing", diagnostic.SelectorRepair.AdmissionReasonCodes);
        Assert.Contains(
            "not an admissible basis to repair from",
            diagnostic.SelectorRepair.Reason,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not a verified pre-dispatch missing-selector failure",
            diagnostic.SelectorRepair.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FailureDiagnostic_NonReplayableAdmissionNeverRecommendsReplayOrRepair()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "run": {
                "state": "failed",
                "admission": {
                  "sideEffectPolicy": "non-replayable",
                  "ordinaryReplayAllowed": true,
                  "repairEligibility": false
                },
                "report": {
                  "outcome": { "status": "failed" },
                  "failure": {
                    "class": "locator-not-found",
                    "phase": "resolution",
                    "stepId": "step-0001"
                  },
                  "steps": [{
                    "stepId": "step-0001",
                    "sequence": 1,
                    "action": "tap",
                    "targetResolution": { "status": "not-found", "matchCount": 0 },
                    "expectedCheckpoint": { "route": "//native" },
                    "observedCheckpoint": { "route": "//native" }
                  }]
                }
              }
            }
            """);

        var diagnostic = TestAgentFailureTool.ReadDiagnostic(document.RootElement);

        Assert.False(diagnostic.SelectorRepair.Eligible);
        Assert.Contains("does not allow another replay", diagnostic.NextSafeAction, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorRequest_AcceptsSelectorObjectsInApprovalScope()
    {
        const string json =
            """
            {
              "approvalScope": {
                "allowedActions": ["fill", "assert"],
                "allowedSelectors": [
                  { "automationId": "NewTodoEntry" },
                  {
                    "automationId": "TodoCheckBox",
                    "stableItemKey": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "collectionScope": "Todo:List"
                  },
                  { "typeIndex": { "type": "Label", "index": 12 } }
                ],
                "allowedSideEffectClasses": ["authoring"],
                "maxActionCount": 2
              }
            }
            """;

        var request = System.Text.Json.JsonSerializer.Deserialize(
            json,
            MauiTestingJsonContext.Default.MauiTestAgentAuthorRequest);

        Assert.Equal(
            [
                "automationId:NewTodoEntry",
                MauiTestAgentSelectorScopeKey.ScopedItem(
                    "Todo:List",
                    "sha256:" + new string('a', 64),
                    "TodoCheckBox"),
                "typeIndex:Label:12",
            ],
            request!.ApprovalScope!.AllowedSelectors);
    }

    [Fact]
    public void SelectorScopeKey_ScopedFieldsWithDelimiters_DoNotCollide()
    {
        var first = MauiTestAgentSelectorScopeKey.ScopedItem("a:b", "c", "d");
        var second = MauiTestAgentSelectorScopeKey.ScopedItem("a", "b:c", "d");

        Assert.NotEqual(first, second);
        Assert.Contains("%3A", first);
        Assert.Contains("%3A", second);
    }

    [Fact]
    public void RunStartRejection_PreservesBoundedBrokerAdmissionReasons()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """
            {
              "ok": false,
              "error": "Flow replay admission was denied before mutation.",
              "errors": [
                "A non-replayable flow may run only with explicit one-shot human authorization.",
                "A second deterministic reason."
              ]
            }
            """);

        var error = TestAgentRunTool.CreateRunStartRejection(document.RootElement);

        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, error.Code);
        Assert.Equal(MauiTestAgentErrorCategories.State, error.Category);
        Assert.Contains("Flow replay admission was denied", error.Message, StringComparison.Ordinal);
        Assert.Contains("explicit one-shot human authorization", error.Message, StringComparison.Ordinal);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task TestAgentRunTool_BindFailureAfterStartedRunReturnsManualRecoveryWithoutRetry()
    {
        var completionRecorded = false;
        var response = await TestAgentRunTool.BindStartedRunAsync(
            "request-run",
            "run_accepted",
            "capability_accepted",
            () => Task.FromResult(new TestAgentBrokerResponse<MauiTestAgentRunBindingResult>(
                503,
                new MauiTestAgentRunBindingResult
                {
                    Error = TestAgentToolSupport.Error(
                        MauiTestAgentErrorCodes.TargetUnavailable,
                        MauiTestAgentErrorCategories.Transport,
                        "The broker could not persist the run binding.",
                        retryable: true),
                },
                null)),
            () =>
            {
                completionRecorded = true;
                return Task.CompletedTask;
            });

        Assert.True(completionRecorded);
        Assert.NotNull(response);
        using var document = JsonDocument.Parse(response);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(MauiTestAgentErrorCodes.UnknownCompletion, document.RootElement.GetProperty("error").GetProperty("code").GetString());
        var recovery = document.RootElement.GetProperty("recovery");
        Assert.Equal("started-unbound", recovery.GetProperty("state").GetString());
        Assert.Equal("run_accepted", recovery.GetProperty("runId").GetString());
        Assert.Equal("capability_accepted", recovery.GetProperty("runCapabilityToken").GetString());
        Assert.False(recovery.GetProperty("automaticRetryAllowed").GetBoolean());
    }

    [Fact]
    public async Task TestAgentRunTool_TransientBindFailureRetriesBindingWithoutRetryingRunStart()
    {
        var bindAttempts = 0;
        var completionRecorded = false;
        var response = await TestAgentRunTool.BindStartedRunAsync(
            "request-run",
            "run_accepted",
            "capability_accepted",
            () =>
            {
                bindAttempts++;
                return Task.FromResult(bindAttempts == 1
                    ? new TestAgentBrokerResponse<MauiTestAgentRunBindingResult>(
                        503,
                        new MauiTestAgentRunBindingResult
                        {
                            Error = TestAgentToolSupport.Error(
                                MauiTestAgentErrorCodes.TargetUnavailable,
                                MauiTestAgentErrorCategories.Transport,
                                "The broker could not persist the run binding yet.",
                                retryable: true),
                        },
                        null)
                    : new TestAgentBrokerResponse<MauiTestAgentRunBindingResult>(
                        200,
                        new MauiTestAgentRunBindingResult { Ok = true, RunId = "run_accepted" },
                        null));
            },
            () =>
            {
                completionRecorded = true;
                return Task.CompletedTask;
            });

        Assert.Equal(2, bindAttempts);
        Assert.False(completionRecorded);
        Assert.Null(response);
    }

    [Fact]
    public async Task TestAgentActionTool_BindFailureAfterStartedRunReturnsTheSameManualRecoveryShape()
    {
        var completionRecorded = false;
        var response = await TestAgentActionTool.BindStartedActionRunAsync(
            "request-action",
            "run_action",
            "capability_action",
            () => Task.FromResult(new TestAgentBrokerResponse<MauiTestAgentRunBindingResult>(
                409,
                new MauiTestAgentRunBindingResult
                {
                    Error = TestAgentToolSupport.Error(
                        MauiTestAgentErrorCodes.SessionExpired,
                        MauiTestAgentErrorCategories.State,
                        "The authoring session expired before binding.",
                        retryable: false),
                },
                null)),
            () =>
            {
                completionRecorded = true;
                return Task.CompletedTask;
            });

        Assert.True(completionRecorded);
        Assert.NotNull(response);
        using var document = JsonDocument.Parse(response);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(MauiTestAgentErrorCodes.UnknownCompletion, document.RootElement.GetProperty("error").GetProperty("code").GetString());
        var recovery = document.RootElement.GetProperty("recovery");
        Assert.Equal("run_action", recovery.GetProperty("runId").GetString());
        Assert.Equal("capability_action", recovery.GetProperty("runCapabilityToken").GetString());
        Assert.False(recovery.GetProperty("automaticRetryAllowed").GetBoolean());
    }

    [Fact]
    public void Begin_MissingExplicitInstance_IsRejected()
    {
        var service = new TestAgentSessionService();
        var result = service.Begin(new MauiTestAgentSessionBeginRequest
        {
            Envelope = Envelope(new MauiTestAgentTarget { AgentId = "agent-a" }, "begin-no-instance"),
            TargetState = State(),
        });

        Assert.False(result.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, result.Error?.Code);
    }

    [Fact]
    public void Begin_IdenticalIdempotentRetryRecoversTheSameReadCapability()
    {
        var service = new TestAgentSessionService();
        var request = new MauiTestAgentSessionBeginRequest
        {
            Envelope = Envelope(Target(), "recoverable-begin"),
            TargetState = State(),
        };

        var first = service.Begin(request);
        var retry = service.Begin(request);

        Assert.True(first.Ok, first.Error?.Message);
        Assert.True(retry.Ok, retry.Error?.Message);
        Assert.Equal(first.Snapshot!.SessionId, retry.Snapshot!.SessionId);
        Assert.Equal(first.Snapshot.ReadCapabilityId, retry.Snapshot.ReadCapabilityId);
        Assert.False(string.IsNullOrWhiteSpace(retry.Snapshot.ReadCapabilityId));
    }

    [Fact]
    public void ReadCapability_CannotBeReplayedAgainstAnotherTargetOrWithoutEnvelope()
    {
        var fixture = BeginFixture();
        var wrongTarget = fixture.ReadAccess("wrong-target");
        wrongTarget.Envelope!.Target!.AgentInstanceId = "other-instance";

        var substituted = fixture.Service.Status(wrongTarget);
        var missingEnvelope = fixture.Service.Status(new MauiTestAgentSessionAccessRequest
        {
            SessionId = fixture.SessionId,
            ReadCapabilityId = fixture.ReadCapability,
        });

        Assert.False(substituted.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.TargetStale, substituted.Error?.Code);
        Assert.False(missingEnvelope.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, missingEnvelope.Error?.Code);
    }

    [Fact]
    public void Grant_RequiresHumanApprovalAndBindsScopeAndSingleUse()
    {
        var fixture = BeginFixture();

        // There is no direct grant route to deny, so the equivalent assertion is that the decision
        // path itself refuses a decision the agent could have made about its own request.
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("agent-self-approval", grantId: null),
            Kind = MauiTestAgentApprovalKinds.Exploration,
            Scope = Scope(MauiTestAgentActions.Tap),
        });
        Assert.True(submitted.Ok, submitted.Error?.Message);

        var denied = fixture.Service.ApproveApprovalRequest(
            submitted.Request!.ApprovalRequestId,
            approvedScope: null,
            fixture.State,
            new MauiTestAgentHumanApproval
            {
                Approved = true,
                ApprovalChannel = "workbench",
                Actor = new MauiActorProvenance
                {
                    ActorKind = "agent",
                    ActorId = "agent-author",
                    Channel = "mcp",
                    Provider = "host-owned",
                },
            },
            grantExpiresAt: null);
        Assert.False(denied.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.HumanApprovalRequired, denied.Error?.Code);
        Assert.Null(
            fixture.Service.GetApprovalRequest(
                submitted.Request.ApprovalRequestId,
                includeGrant: true).Request!.GrantId);

        var grant = fixture.IssueGrant(Scope(MauiTestAgentActions.Tap));
        Assert.True(grant.Ok);
        Assert.NotNull(grant.GrantId);
        Assert.NotEqual(grant.GrantId, grant.GrantDigest);

        var wrongSelector = fixture.Authorize(
            "tap-wrong-selector",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "other" });
        Assert.False(wrongSelector.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantScopeDenied, wrongSelector.Error?.Code);

        var allowed = fixture.Authorize(
            "tap-allowed",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" });
        Assert.True(allowed.Ok);
        Assert.True(allowed.DispatchAllowed);
        Assert.Equal(0, allowed.RemainingActions);

        var reused = fixture.Authorize(
            "tap-reused",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" });
        Assert.False(reused.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantReused, reused.Error?.Code);
    }

    [Fact]
    public void Grant_RejectsStaleTargetAndValueLimit_WhileIdenticalAuthorizationRetriesReturnPriorAuthorization()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Fill],
            AllowedSelectors = ["automationId:query"],
            AllowedSideEffectClasses = ["ui"],
            MaxActionCount = 2,
            MaxValueBytes = 2,
        });

        var staleState = new MauiTestAgentTargetState
        {
            AgentId = fixture.State.AgentId,
            AgentInstanceId = fixture.State.AgentInstanceId,
            AppBuildFingerprint = "build-b",
            SeedFingerprint = fixture.State.SeedFingerprint,
        };
        var stale = fixture.Authorize(
            "fill-stale",
            grant.GrantId!,
            MauiTestAgentActions.Fill,
            selector: new FlowSelector { AutomationId = "query" },
            value: "a",
            state: staleState);
        Assert.False(stale.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.TargetStale, stale.Error?.Code);

        var oversized = fixture.Authorize(
            "fill-oversized",
            grant.GrantId!,
            MauiTestAgentActions.Fill,
            selector: new FlowSelector { AutomationId = "query" },
            value: "abc");
        Assert.False(oversized.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ValueLimitExceeded, oversized.Error?.Code);

        var allowed = fixture.Authorize(
            "fill-once",
            grant.GrantId!,
            MauiTestAgentActions.Fill,
            selector: new FlowSelector { AutomationId = "query" },
            value: "ok");
        Assert.True(allowed.Ok);

        var replay = fixture.Authorize(
            "fill-once",
            grant.GrantId!,
            MauiTestAgentActions.Fill,
            selector: new FlowSelector { AutomationId = "query" },
            value: "ok");
        Assert.True(replay.Ok, replay.Error?.Message);
        Assert.True(replay.DispatchAllowed);
        Assert.Equal(allowed.AuthorizationId, replay.AuthorizationId);
        Assert.Equal(allowed.RemainingActions, replay.RemainingActions);

        var differentRequest = fixture.Authorize(
            "fill-once",
            grant.GrantId!,
            MauiTestAgentActions.Fill,
            selector: new FlowSelector { AutomationId = "query" },
            value: "o");
        Assert.False(differentRequest.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.IdempotencyReused, differentRequest.Error?.Code);
    }

    [Fact]
    public void MutationAuthorization_RejectsAttestedRouteAndWindowDrift()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(Scope(MauiTestAgentActions.Tap));

        var routeDrift = fixture.Authorize(
            "route-drift",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" },
            state: new MauiTestAgentTargetState
            {
                AgentId = fixture.State.AgentId,
                AgentInstanceId = fixture.State.AgentInstanceId,
                AppBuildFingerprint = fixture.State.AppBuildFingerprint,
                SeedFingerprint = fixture.State.SeedFingerprint,
                BackendStateFingerprint = fixture.State.BackendStateFingerprint,
                Route = "//other",
                Window = fixture.State.Window,
            });
        Assert.False(routeDrift.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.TargetStale, routeDrift.Error?.Code);

        var windowDrift = fixture.Authorize(
            "window-drift",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" },
            state: new MauiTestAgentTargetState
            {
                AgentId = fixture.State.AgentId,
                AgentInstanceId = fixture.State.AgentInstanceId,
                AppBuildFingerprint = fixture.State.AppBuildFingerprint,
                SeedFingerprint = fixture.State.SeedFingerprint,
                BackendStateFingerprint = fixture.State.BackendStateFingerprint,
                Route = fixture.State.Route,
                Window = "secondary-window",
            });
        Assert.False(windowDrift.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.TargetStale, windowDrift.Error?.Code);
    }

    [Fact]
    public void ReadCapabilityRefusal_NamesTheFieldAndWhereItBelongs()
    {
        // Three agent-led sessions stalled repeatedly on this refusal because it named no field,
        // no location, and no source, so the agent had to guess all three. The message is the
        // fix, and it regresses silently unless something asserts on it.
        var fixture = BeginFixture();

        var missing = fixture.Service.ValidateRunBinding(new MauiTestAgentRunBindingRequest
        {
            SessionId = fixture.SessionId,
            ReadCapabilityId = null,
            Envelope = fixture.Envelope("validate-missing-capability", null),
            RunId = "run-any",
            RunCapabilityToken = "token-any",
        });

        Assert.False(missing.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ReadCapabilityRequired, missing.Error?.Code);
        Assert.Contains("readCapabilityId", missing.Error?.Message, StringComparison.Ordinal);
        Assert.Contains("envelope", missing.Error!.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maui_test_author", missing.Error.Message!, StringComparison.Ordinal);

        var wrong = fixture.Service.ValidateRunBinding(new MauiTestAgentRunBindingRequest
        {
            SessionId = fixture.SessionId,
            ReadCapabilityId = "read-capability-from-some-other-session",
            Envelope = fixture.Envelope("validate-wrong-capability", null),
            RunId = "run-any",
            RunCapabilityToken = "token-any",
        });

        // A wrong value is a different mistake from an absent one, and saying so keeps an agent
        // from re-sending the same stale id it already holds.
        Assert.False(wrong.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ReadCapabilityRequired, wrong.Error?.Code);
        Assert.Contains("does not belong to this authoring session", wrong.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunBinding_RejectsAnotherBoundRunsCapabilityToken()
    {
        var fixture = BeginFixture();
        const string firstRunId = "run-first";
        const string firstToken = "run-token-first";
        const string secondRunId = "run-second";
        const string secondToken = "run-token-second";

        Assert.True(fixture.Service.BindRun(new MauiTestAgentRunBindingRequest
        {
            SessionId = fixture.SessionId,
            ReadCapabilityId = fixture.ReadCapability,
            Envelope = fixture.Envelope("bind-first", null),
            RunId = firstRunId,
            RunCapabilityToken = firstToken,
        }).Ok);
        Assert.True(fixture.Service.BindRun(new MauiTestAgentRunBindingRequest
        {
            SessionId = fixture.SessionId,
            ReadCapabilityId = fixture.ReadCapability,
            Envelope = fixture.Envelope("bind-second", null),
            RunId = secondRunId,
            RunCapabilityToken = secondToken,
        }).Ok);

        var substituted = fixture.Service.ValidateRunBinding(new MauiTestAgentRunBindingRequest
        {
            SessionId = fixture.SessionId,
            ReadCapabilityId = fixture.ReadCapability,
            Envelope = fixture.Envelope("validate-substituted", null),
            RunId = firstRunId,
            RunCapabilityToken = secondToken,
        });
        Assert.False(substituted.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ReadCapabilityRequired, substituted.Error?.Code);

        var exact = fixture.Service.ValidateRunBinding(new MauiTestAgentRunBindingRequest
        {
            SessionId = fixture.SessionId,
            ReadCapabilityId = fixture.ReadCapability,
            Envelope = fixture.Envelope("validate-exact", null),
            RunId = firstRunId,
            RunCapabilityToken = firstToken,
        });
        Assert.True(exact.Ok, exact.Error?.Message);
    }

    [Fact]
    public void MutationAuthorization_RejectsExpiredDeadlineBeforeGrantConsumption()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(Scope(MauiTestAgentActions.Tap));
        var envelope = fixture.Envelope("expired-deadline", grant.GrantId!);
        envelope.DeadlineMs = 0;

        var expired = fixture.Service.AuthorizeMutation(new MauiTestAgentMutationAuthorizationRequest
        {
            Envelope = envelope,
            Action = MauiTestAgentActions.Tap,
            Selector = new FlowSelector { AutomationId = "save" },
            SideEffectClass = "ui",
            CurrentTargetState = fixture.State,
        });
        Assert.False(expired.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.DeadlineExpired, expired.Error?.Code);

        var stillUsable = fixture.Authorize(
            "deadline-grant-still-usable",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" });
        Assert.True(stillUsable.Ok, stillUsable.Error?.Message);
    }

    [Fact]
    public void Grant_RejectsMultiUseCommitScope()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.AuthorCommit],
            AllowedSideEffectClasses = ["authoring"],
            MaxActionCount = 2,
            MaxValueBytes = 0,
        });

        Assert.False(grant.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, grant.Error?.Code);
    }

    /// <summary>
    /// A multi-use run scope cannot become a multi-use run grant. Since grants are minted only by
    /// deciding an approval request, the enforcement point is the approval path: it clamps the
    /// request to a single dispatch before a human ever reads it, so what is reviewed and what is
    /// redeemed are the same one-shot authorization.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(64)]
    public void Grant_ClampsAMultiUseRunScopeToASingleDispatch(int maxActionCount)
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Run],
            AllowedSideEffectClasses = ["run"],
            MaxActionCount = maxActionCount,
            MaxValueBytes = 0,
        });

        Assert.True(grant.Ok, grant.Error?.Message);
        Assert.Equal(1, grant.RemainingActions);

        var first = fixture.Authorize(
            "clamped-run-1",
            grant.GrantId!,
            MauiTestAgentActions.Run,
            sideEffectClass: "run");
        var second = fixture.Authorize(
            "clamped-run-2",
            grant.GrantId!,
            MauiTestAgentActions.Run,
            sideEffectClass: "run");

        Assert.True(first.Ok, first.Error?.Message);
        Assert.False(second.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantReused, second.Error?.Code);
    }

    [Fact]
    public void Grant_RejectsRunScopeBundledWithAnotherAction()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Run, MauiTestAgentActions.Cancel],
            AllowedSideEffectClasses = ["run"],
            MaxActionCount = 1,
            MaxValueBytes = 0,
        });

        Assert.False(grant.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, grant.Error?.Code);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(64)]
    public void ApprovalRequest_NormalizesRunScopeToASingleDispatch(int requestedMaxActionCount)
    {
        var fixture = BeginFixture();
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope($"run-single-use-{requestedMaxActionCount}", grantId: null),
            Kind = MauiTestAgentApprovalKinds.Run,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Run],
                AllowedSideEffectClasses = ["run"],
                MaxActionCount = requestedMaxActionCount,
                MaxValueBytes = 0,
            },
        });

        Assert.True(submitted.Ok, submitted.Error?.Message);
        Assert.Equal(1, submitted.Request!.RequestedScope!.MaxActionCount);

        var approved = fixture.Service.ApproveApprovalRequest(
            submitted.Request.ApprovalRequestId,
            approvedScope: new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Run],
                AllowedSideEffectClasses = ["run"],
                MaxActionCount = requestedMaxActionCount,
                MaxValueBytes = 0,
            },
            fixture.State,
            fixture.HumanDecision(approved: true),
            grantExpiresAt: null);

        Assert.True(approved.Ok, approved.Error?.Message);
        Assert.Equal(1, approved.Request!.ApprovedScope!.MaxActionCount);
    }

    [Fact]
    public void RunGrant_AuthorizesExactlyOneDispatch()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Run],
            AllowedSideEffectClasses = ["run"],
            MaxActionCount = 1,
            MaxValueBytes = 0,
        });
        Assert.True(grant.Ok, grant.Error?.Message);
        Assert.Equal(1, grant.RemainingActions);

        var first = fixture.Authorize(
            "run-dispatch-1",
            grant.GrantId!,
            MauiTestAgentActions.Run,
            sideEffectClass: "run");
        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(0, first.RemainingActions);

        var second = fixture.Authorize(
            "run-dispatch-2",
            grant.GrantId!,
            MauiTestAgentActions.Run,
            sideEffectClass: "run");
        Assert.False(second.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantReused, second.Error?.Code);
    }

    [Fact]
    public void RunAuthorization_RefusesAChangedFlowDigest()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Run],
            AllowedSideEffectClasses = ["run"],
            MaxActionCount = 1,
            MaxValueBytes = 0,
        });
        var authorization = fixture.Authorize(
            "run-digest",
            grant.GrantId!,
            MauiTestAgentActions.Run,
            sideEffectClass: "run");
        Assert.True(authorization.Ok, authorization.Error?.Message);

        var allowed = fixture.Service.CanDispatchRunAuthorization(
            authorization.AuthorizationId,
            Target().AgentId,
            Target().AgentInstanceId,
            [],
            new string('0', 64),
            out var error);

        Assert.False(allowed);
        Assert.Contains("flow digest", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("authoring-commit", "commit", MauiTestAgentApprovalKinds.Commit, MauiTestAgentActions.AuthorCommit)]
    [InlineData("test-run", "start-run", MauiTestAgentApprovalKinds.Run, MauiTestAgentActions.Run)]
    [InlineData("assertions", "assert", MauiTestAgentApprovalKinds.Assertion, MauiTestAgentActions.Assert)]
    public void ApprovalRequest_NormalizesCommonAgentAliases(
        string suppliedKind,
        string suppliedAction,
        string expectedKind,
        string expectedAction)
    {
        var fixture = BeginFixture();
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope($"alias-{suppliedKind}", grantId: null),
            Kind = suppliedKind,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [suppliedAction],
                AllowedSideEffectClasses = ["authoring"],
                MaxActionCount = 1,
                MaxValueBytes = 0,
            },
        });

        Assert.True(submitted.Ok, submitted.Error?.Message);
        Assert.Equal(expectedKind, submitted.Request!.Kind);
        Assert.Equal([expectedAction], submitted.Request.RequestedScope!.AllowedActions);
        if (expectedKind == MauiTestAgentApprovalKinds.Run)
            Assert.Equal(["run"], submitted.Request.RequestedScope.AllowedSideEffectClasses);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("execution")]
    [InlineData("app-mutation")]
    [InlineData("workflow-run")]
    public void ApprovalRequest_NormalizesRunSideEffectClass(string? suppliedClass)
    {
        var fixture = BeginFixture();
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope($"run-class-{suppliedClass ?? "empty"}", grantId: null),
            Kind = MauiTestAgentApprovalKinds.Run,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Run],
                AllowedSideEffectClasses = suppliedClass is null ? [] : [suppliedClass],
                MaxActionCount = 1,
                MaxValueBytes = 0,
            },
        });

        Assert.True(submitted.Ok, submitted.Error?.Message);
        Assert.Equal(["run"], submitted.Request!.RequestedScope!.AllowedSideEffectClasses);
    }

    [Theory]
    [InlineData(MauiTestAgentApprovalKinds.DraftChange)]
    [InlineData(MauiTestAgentApprovalKinds.Assertion)]
    [InlineData(MauiTestAgentApprovalKinds.Commit)]
    public void ApprovalRequest_NormalizesAuthoringSideEffectClass(string kind)
    {
        var fixture = BeginFixture();
        var action = kind switch
        {
            MauiTestAgentApprovalKinds.Assertion => MauiTestAgentActions.Assert,
            MauiTestAgentApprovalKinds.Commit => MauiTestAgentActions.AuthorCommit,
            _ => MauiTestAgentActions.Tap,
        };
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope($"normalize-authoring-{kind}", grantId: null),
            Kind = kind,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [action],
                AllowedSelectors = kind == MauiTestAgentApprovalKinds.Commit ? [] : ["automationId:save"],
                AllowedSideEffectClasses = ["non-replayable"],
                MaxActionCount = 1,
                MaxValueBytes = 0,
            },
        });

        Assert.True(submitted.Ok, submitted.Error?.Message);
        Assert.Equal(["authoring"], submitted.Request!.RequestedScope!.AllowedSideEffectClasses);
    }

    [Fact]
    public void DraftOnlyAction_UsesAuthoringScopeRegardlessOfPlanPolicyVocabulary()
    {
        Assert.Equal("authoring", TestAgentActionTool.ResolveSideEffectClass(new MauiTestAgentActionRequest
        {
            Execute = false,
            AppendDraft = true,
            SideEffectClass = "non-replayable",
        }));
        Assert.Equal("ui", TestAgentActionTool.ResolveSideEffectClass(new MauiTestAgentActionRequest
        {
            Execute = true,
            AppendDraft = false,
        }));
    }

    [Fact]
    public void ReadCapability_AllowsStatusButCannotAuthorizeMutation()
    {
        var fixture = BeginFixture();
        var status = fixture.Service.Status(fixture.ReadAccess("status-read-capability"));
        Assert.True(status.Ok);

        var noGrant = fixture.Authorize(
            "tap-without-grant",
            grantId: null,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" });
        Assert.False(noGrant.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantRequired, noGrant.Error?.Code);
    }

    [Fact]
    public void DraftActionsAssertionsAndCommit_RequireSeparateHumanGrants()
    {
        var fixture = BeginFixture();
        var actionGrant = fixture.IssueGrant(Scope(MauiTestAgentActions.DraftAppend));
        var actionAuthorization = fixture.Authorize(
            "draft-tap",
            actionGrant.GrantId!,
            MauiTestAgentActions.DraftAppend,
            selector: new FlowSelector { AutomationId = "save" },
            sideEffectClass: "authoring");
        Assert.True(actionAuthorization.Ok);

        var appended = fixture.Service.AppendAction(
            fixture.SessionId,
            actionAuthorization.AuthorizationId,
            new MauiTestAgentActionRequest
            {
                Envelope = fixture.Envelope("draft-tap", actionGrant.GrantId!),
                AuthorizationId = actionAuthorization.AuthorizationId,
                Action = MauiTestAgentActions.Tap,
                Selector = new FlowSelector { AutomationId = "save" },
                AppendDraft = true,
                Execute = false,
            });
        Assert.True(appended.Ok);
        Assert.Single(appended.Snapshot!.Flow!.Steps);

        var assertionGrant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Assert],
            AllowedSelectors = ["automationId:save"],
            AllowedSideEffectClasses = ["authoring"],
            MaxActionCount = 1,
            MaxValueBytes = 32,
        });
        var assertionAuthorization = fixture.Authorize(
            "draft-assert",
            assertionGrant.GrantId!,
            MauiTestAgentActions.Assert,
            selector: new FlowSelector { AutomationId = "save" },
            sideEffectClass: "authoring");
        var asserted = fixture.Service.AddAssertion(
            fixture.SessionId,
            assertionAuthorization.AuthorizationId,
            new MauiTestAgentAssertionRequest
            {
                Envelope = fixture.Envelope("draft-assert", assertionGrant.GrantId!),
                AuthorizationId = assertionAuthorization.AuthorizationId,
                Assertion = new FlowAssert
                {
                    Kind = "exists",
                    Selector = new FlowSelector { AutomationId = "save" },
                    Verify = true,
                },
            });
        Assert.True(asserted.Ok);

        var commitGrant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.AuthorCommit],
            AllowedSideEffectClasses = ["authoring"],
            MaxActionCount = 1,
            MaxValueBytes = 0,
        });
        var commitAuthorization = fixture.Authorize(
            "draft-commit",
            commitGrant.GrantId!,
            MauiTestAgentActions.AuthorCommit,
            sideEffectClass: "authoring");
        var committed = fixture.Service.Commit(
            new MauiTestAgentSessionAccessRequest
            {
                SessionId = fixture.SessionId,
                AuthorizationId = commitAuthorization.AuthorizationId,
                Envelope = fixture.Envelope("draft-commit", commitGrant.GrantId!),
            },
            commitAuthorization.AuthorizationId);

        Assert.True(committed.Ok, committed.Error?.Message);
        Assert.Equal(2, committed.Snapshot!.FlowRevision);
        Assert.Equal(2, committed.Snapshot.Plan!.Revision);
    }

    [Fact]
    public void AuthorizedDraftAppend_CannotSubstituteActionSelectorOrValue()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(Scope(MauiTestAgentActions.DraftAppend));
        var authorization = fixture.Authorize(
            "approved-tap",
            grant.GrantId!,
            MauiTestAgentActions.DraftAppend,
            selector: new FlowSelector { AutomationId = "save" },
            sideEffectClass: "authoring");

        var substituted = fixture.Service.AppendAction(
            fixture.SessionId,
            authorization.AuthorizationId,
            new MauiTestAgentActionRequest
            {
                Envelope = fixture.Envelope("approved-tap", grant.GrantId!),
                AuthorizationId = authorization.AuthorizationId,
                Action = MauiTestAgentActions.Fill,
                Selector = new FlowSelector { AutomationId = "other" },
                Value = "unapproved",
                AppendDraft = true,
                Execute = false,
            });

        Assert.False(substituted.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantScopeDenied, substituted.Error?.Code);
    }

    [Fact]
    public void AuthorizedDraftAppend_SameAuthorizationCannotMutateTwiceBeforeCompletion()
    {
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(Scope(MauiTestAgentActions.DraftAppend));
        var authorization = fixture.Authorize(
            "single-authorization",
            grant.GrantId!,
            MauiTestAgentActions.DraftAppend,
            selector: new FlowSelector { AutomationId = "save" },
            sideEffectClass: "authoring");
        var request = new MauiTestAgentActionRequest
        {
            Envelope = fixture.Envelope("single-authorization", grant.GrantId!),
            AuthorizationId = authorization.AuthorizationId,
            Action = MauiTestAgentActions.Tap,
            Selector = new FlowSelector { AutomationId = "save" },
            AppendDraft = true,
            Execute = false,
        };

        var first = fixture.Service.AppendAction(
            fixture.SessionId,
            authorization.AuthorizationId,
            request);
        var replay = fixture.Service.AppendAction(
            fixture.SessionId,
            authorization.AuthorizationId,
            request);

        Assert.True(first.Ok, first.Error?.Message);
        Assert.False(replay.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantRequired, replay.Error?.Code);
        Assert.Single(fixture.Service.Status(fixture.ReadAccess("status-after-append")).Snapshot!.Flow!.Steps);
        Assert.True(fixture.Service.CompleteMutation(new MauiTestAgentMutationCompletion
        {
            AuthorizationId = authorization.AuthorizationId,
            Outcome = "completed",
        }).Ok);
    }

    [Fact]
    public void Patch_ApplyApprovalAndRollbackAreForbiddenAndAuditIsBoundedAndRedacted()
    {
        var fixture = BeginFixture(
            options: new TestAgentSessionServiceOptions
            {
                MaxAuditEntries = 3,
                SessionLifetime = TimeSpan.FromHours(1),
                DefaultGrantLifetime = TimeSpan.FromMinutes(5),
                MaximumGrantLifetime = TimeSpan.FromMinutes(5),
                AuditRetention = TimeSpan.FromHours(1),
            });
        var rejected = fixture.Service.Patch(new MauiTestAgentPatchRequest
        {
            Envelope = fixture.Envelope("patch-apply", null),
            Operation = "apply",
        });

        Assert.False(rejected.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.PatchApplyForbidden, rejected.Error?.Code);
        var rollback = fixture.Service.Patch(new MauiTestAgentPatchRequest
        {
            Envelope = fixture.Envelope("patch-rollback", null),
            Operation = "rollback",
        });
        Assert.False(rollback.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.PatchApplyForbidden, rollback.Error?.Code);

        var audit = fixture.Service.Audit(fixture.ReadAccess("audit-patch"));
        Assert.True(audit.Ok);
        Assert.True(audit.Entries.Count <= 3);
        var serialized = System.Text.Json.JsonSerializer.Serialize(audit, MauiTestingJsonContext.Default.MauiTestAgentAuditResult);
        Assert.DoesNotContain("do not expose this prompt", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchProposal_IsRunBoundSelectorOnlyIdempotentAndProvenanceChecked()
    {
        var service = new TestAgentSessionService(new TestAgentSessionServiceOptions
        {
            MaxPatchesPerSession = 1,
        });
        var flow = new MauiFlow
        {
            Name = "repairable",
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    StepId = "save-step",
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "old-save" },
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "old-save" } },
                },
            ],
        };
        var begun = service.Begin(new MauiTestAgentSessionBeginRequest
        {
            Envelope = Envelope(Target(), "patch-session"),
            TargetState = State(),
            Flow = flow,
            Plan = new MauiTestPlan
            {
                PlanId = "plan-patch",
                Revision = 1,
                Flow = new MauiFlowReference { Path = "repairable.md", Revision = 1 },
                Title = "Repairable",
                Goal = "Review one selector-only patch",
                Reset = new MauiTestResetRequirement { Required = false, Strategy = "host-owned" },
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            },
        });
        Assert.True(begun.Ok, begun.Error?.Message);
        var snapshot = begun.Snapshot!;
        MauiTestAgentRequestEnvelope ReadEnvelope(string key) => new()
        {
            RequestId = "req-" + key,
            IdempotencyKey = "idem-" + key,
            Target = snapshot.Target,
            Correlation = new MauiTestAgentCorrelation
            {
                AuthoringSessionId = snapshot.SessionId,
                PlanId = snapshot.Plan!.PlanId,
                PlanRevision = snapshot.Plan.Revision,
                PlanDigest = snapshot.PlanDigest,
                FlowId = snapshot.Plan.Flow!.FlowId,
                FlowRevision = snapshot.FlowRevision,
                FlowDigest = snapshot.FlowDigest,
            },
            Provenance = new MauiActorProvenance
            {
                ActorKind = "agent",
                ActorId = "agent-author",
                Channel = "mcp",
                Provider = "host-owned",
            },
            Intent = "Review an inert selector repair.",
            ReadCapabilityId = snapshot.ReadCapabilityId,
            PolicyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
        };
        Assert.True(service.BindRun(new MauiTestAgentRunBindingRequest
        {
            SessionId = snapshot.SessionId,
            ReadCapabilityId = snapshot.ReadCapabilityId,
            Envelope = ReadEnvelope("bind-patch-run"),
            RunId = "failed-run",
            RunCapabilityToken = "run-capability",
        }).Ok);

        var built = MauiFlowRepairPatchBuilder.Build(
            snapshot.Flow!,
            "save-step",
            new FlowSelector { AutomationId = "new-save" });
        Assert.True(built.Ok, built.Error);
        var proposal = new MauiFlowRepairProposal
        {
            SourceRunId = "failed-run",
            SourceStepId = "save-step",
            SourceFailureId = "failure-1",
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PreDispatch = true,
            BaseFlow = new MauiFlowReference
            {
                Path = snapshot.Plan!.Flow!.Path,
                FlowId = snapshot.Plan.Flow.FlowId,
                Revision = snapshot.FlowRevision,
                Digest = snapshot.FlowDigest,
            },
            OldSelector = new FlowSelector { AutomationId = "old-save" },
            ProposedSelector = new FlowSelector { AutomationId = "new-save" },
            Patch = built.Patch,
            PatchDigest = built.PatchDigest,
            Diff = built.Diff,
            UnchangedAssertionsProof = built.Proof,
            Provenance = ReadEnvelope("proposal-provenance").Provenance,
        };
        var request = new MauiTestAgentPatchRequest
        {
            Envelope = ReadEnvelope("patch-proposal"),
            Operation = "proposal",
            Proposal = proposal,
        };

        var first = service.Patch(request);
        var retry = service.Patch(request);
        Assert.True(first.Ok, first.Error?.Message);
        Assert.True(retry.Ok, retry.Error?.Message);
        Assert.Equal(first.Record!.ProposalId, retry.Record!.ProposalId);
        Assert.Null(first.Record.Proposal!.Candidate);
        Assert.Null(first.Record.Proposal.ExtensionData);

        var secondBuild = MauiFlowRepairPatchBuilder.Build(
            snapshot.Flow!,
            "save-step",
            new FlowSelector { AutomationId = "another-save" });
        var secondNode = JsonSerializer.SerializeToNode(
            proposal,
            MauiTestingJsonContext.Default.MauiFlowRepairProposal)!.AsObject();
        secondNode["proposedSelector"] = JsonSerializer.SerializeToNode(
            new FlowSelector { AutomationId = "another-save" },
            MauiFlowJsonContext.Default.FlowSelector);
        secondNode["patch"] = JsonSerializer.SerializeToNode(
            secondBuild.Patch,
            MauiTestingJsonContext.Default.MauiFlowPatch);
        secondNode["patchDigest"] = secondBuild.PatchDigest;
        secondNode["diff"] = JsonSerializer.SerializeToNode(
            secondBuild.Diff,
            MauiTestingJsonContext.Default.MauiRepairSelectorDiff);
        secondNode["unchangedAssertionsProof"] = JsonSerializer.SerializeToNode(
            secondBuild.Proof,
            MauiTestingJsonContext.Default.MauiRepairAssertionProof);
        var capped = service.Patch(new MauiTestAgentPatchRequest
        {
            Envelope = ReadEnvelope("patch-cap"),
            Operation = "proposal",
            Proposal = secondNode.Deserialize(MauiTestingJsonContext.Default.MauiFlowRepairProposal),
        });
        Assert.False(capped.Ok);
        Assert.Equal(MauiTestAgentErrorCategories.Capability, capped.Error?.Category);

        var hostileNode = JsonSerializer.SerializeToNode(
            proposal,
            MauiTestingJsonContext.Default.MauiFlowRepairProposal)!.AsObject();
        hostileNode["provenance"] = JsonSerializer.SerializeToNode(new MauiActorProvenance
        {
            ActorKind = "agent",
            ActorId = "other-agent",
            Channel = "mcp",
            Provider = "host-owned",
        });
        var rejected = service.Patch(new MauiTestAgentPatchRequest
        {
            Envelope = ReadEnvelope("patch-hostile-provenance"),
            Operation = "proposal",
            Proposal = hostileNode.Deserialize(MauiTestingJsonContext.Default.MauiFlowRepairProposal),
        });
        Assert.False(rejected.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, rejected.Error?.Code);
    }

    [Fact]
    public void PatchRequest_EnforcesBoundedSerializedSizeBeforeRetention()
    {
        var fixture = BeginFixture(options: new TestAgentSessionServiceOptions
        {
            MaxPatchRequestBytes = 128,
        });
        var result = fixture.Service.Patch(new MauiTestAgentPatchRequest
        {
            Envelope = fixture.Envelope("oversized-patch", null),
            Operation = "proposal",
            Reason = new string('x', 512),
        });

        Assert.False(result.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ValueLimitExceeded, result.Error?.Code);
    }

    [Fact]
    public void Session_ExpiresAndAuditRetentionPurgesOldEntries()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromSeconds(1),
                DefaultGrantLifetime = TimeSpan.FromSeconds(1),
                MaximumGrantLifetime = TimeSpan.FromSeconds(1),
                AuditRetention = TimeSpan.FromSeconds(1),
            });
        clock.Advance(TimeSpan.FromSeconds(2));

        var status = fixture.Service.Status(fixture.ReadAccess("status-expired"));
        Assert.False(status.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.SessionExpired, status.Error?.Code);
    }

    [Fact]
    public void Grant_ExpiryIsReportedBeforeDispatch()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(10),
                DefaultGrantLifetime = TimeSpan.FromSeconds(1),
                MaximumGrantLifetime = TimeSpan.FromSeconds(1),
                AuditRetention = TimeSpan.FromMinutes(10),
            });
        var grant = fixture.IssueGrant(Scope(MauiTestAgentActions.Tap));

        clock.Advance(TimeSpan.FromSeconds(2));
        var expired = fixture.Authorize(
            "expired-tap",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" });

        Assert.False(expired.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantExpired, expired.Error?.Code);
        Assert.False(expired.DispatchAllowed);
    }

    [Fact]
    public void Grant_ExpiryPastSessionEndIsClampedRatherThanRejected()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new MutableTimeProvider(start);
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(10),
                DefaultGrantLifetime = TimeSpan.FromMinutes(10),
                MaximumGrantLifetime = TimeSpan.FromMinutes(15),
                AuditRetention = TimeSpan.FromMinutes(30),
            });

        clock.Advance(TimeSpan.FromMinutes(9));
        var grant = fixture.IssueGrant(
            Scope(MauiTestAgentActions.Tap),
            expiresAt: clock.GetUtcNow().AddMinutes(10));

        Assert.True(grant.Ok, grant.Error?.Message);
        Assert.Equal(start.AddMinutes(10), grant.ExpiresAt);
    }

    [Fact]
    public void ApprovalRequest_LateHumanApprovalStillIssuesGrant()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new MutableTimeProvider(start);
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(10),
                DefaultGrantLifetime = TimeSpan.FromMinutes(10),
                MaximumGrantLifetime = TimeSpan.FromMinutes(15),
                ApprovalRequestLifetime = TimeSpan.FromMinutes(10),
                AuditRetention = TimeSpan.FromMinutes(30),
            });

        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("late-human-approval", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = Scope(MauiTestAgentActions.Tap),
        });
        Assert.True(submitted.Ok, submitted.Error?.Message);

        // The reviewer spends six minutes reading the scope, then approves with the
        // Inspector's default grant duration. That grant would end past the session,
        // which previously made a considered human approval impossible to issue.
        clock.Advance(TimeSpan.FromMinutes(6));

        var approved = fixture.Service.ApproveApprovalRequest(
            submitted.Request!.ApprovalRequestId,
            approvedScope: null,
            fixture.State,
            fixture.HumanDecision(approved: true),
            grantExpiresAt: clock.GetUtcNow().AddMinutes(10));

        Assert.True(approved.Ok, approved.Error?.Message);
        Assert.Equal(MauiTestAgentApprovalStates.Approved, approved.Request!.State);

        var status = fixture.Service.Status(fixture.ReadAccess("status-late-approval"));
        var delivered = Assert.Single(status.Snapshot!.ApprovalRequests);
        Assert.False(string.IsNullOrWhiteSpace(delivered.GrantId));
    }

    [Fact]
    public void Grant_FarFutureExpiryIsStillRejectedEvenWhenTheSessionEndsSoon()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new MutableTimeProvider(start);
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(2),
                DefaultGrantLifetime = TimeSpan.FromMinutes(1),
                MaximumGrantLifetime = TimeSpan.FromMinutes(5),
                AuditRetention = TimeSpan.FromMinutes(30),
            });

        // The session ends in two minutes, so a naive clamp-then-validate would
        // normalise this year-3000 request into a valid two-minute grant. Policy
        // refuses far-future values outright, so the clamp must not launder them.
        var grant = fixture.IssueGrant(
            Scope(MauiTestAgentActions.Tap),
            expiresAt: DateTimeOffset.Parse("3000-01-01T00:00:00Z"));

        Assert.False(grant.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, grant.Error?.Code);
    }

    [Fact]
    public void ApprovalRequest_FarFutureExpiryIsStillRejectedEvenWhenTheSessionEndsSoon()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new MutableTimeProvider(start);
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(2),
                DefaultGrantLifetime = TimeSpan.FromMinutes(1),
                MaximumGrantLifetime = TimeSpan.FromMinutes(5),
                ApprovalRequestLifetime = TimeSpan.FromMinutes(5),
                AuditRetention = TimeSpan.FromMinutes(30),
            });

        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("far-future-request", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = Scope(MauiTestAgentActions.Tap),
            ExpiresAt = DateTimeOffset.Parse("3000-01-01T00:00:00Z"),
        });

        Assert.False(submitted.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, submitted.Error?.Code);
    }

    [Fact]
    public void ExplorationGrant_DecrementsAtomicallyAndAuditRetentionIsBounded()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromHours(1),
                DefaultGrantLifetime = TimeSpan.FromMinutes(5),
                MaximumGrantLifetime = TimeSpan.FromMinutes(5),
                AuditRetention = TimeSpan.FromSeconds(1),
                MaxAuditEntries = 32,
            });
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Tap],
            AllowedSelectors = ["automationId:save"],
            AllowedSideEffectClasses = ["ui"],
            MaxActionCount = 2,
            MaxValueBytes = 0,
        });

        var first = fixture.Authorize(
            "explore-1",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" });
        var second = fixture.Authorize(
            "explore-2",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" });
        var third = fixture.Authorize(
            "explore-3",
            grant.GrantId!,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" });

        Assert.True(first.Ok);
        Assert.Equal(1, first.RemainingActions);
        Assert.True(second.Ok);
        Assert.Equal(0, second.RemainingActions);
        Assert.False(third.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantReused, third.Error?.Code);

        clock.Advance(TimeSpan.FromSeconds(2));
        var audit = fixture.Service.Audit(fixture.ReadAccess("audit-expired"));
        Assert.True(audit.Ok);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public void ApprovalRequest_WorkbenchApprovalDeliversGrantOnlyThroughSessionStatus()
    {
        var fixture = BeginFixture();
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-draft-change", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = Scope(MauiTestAgentActions.Tap),
        });

        Assert.True(submitted.Ok, submitted.Error?.Message);
        Assert.Equal(MauiTestAgentApprovalStates.Pending, submitted.Request!.State);
        Assert.Null(submitted.Request.GrantId);

        var listed = fixture.Service.ListApprovalRequests(
            fixture.State.AgentId,
            fixture.State.AgentInstanceId,
            includeGrant: false);
        var pending = Assert.Single(listed);
        Assert.Null(pending.GrantId);

        var approved = fixture.Service.ApproveApprovalRequest(
            pending.ApprovalRequestId,
            approvedScope: null,
            fixture.State,
            fixture.HumanDecision(approved: true),
            grantExpiresAt: null);
        Assert.True(approved.Ok, approved.Error?.Message);
        Assert.Equal(MauiTestAgentApprovalStates.Approved, approved.Request!.State);
        Assert.Null(approved.Request.GrantId);

        var status = fixture.Service.Status(fixture.ReadAccess("status-approved"));
        var delivered = Assert.Single(status.Snapshot!.ApprovalRequests);
        Assert.Equal(MauiTestAgentApprovalStates.Approved, delivered.State);
        Assert.False(string.IsNullOrWhiteSpace(delivered.GrantId));

        var authorization = fixture.Authorize(
            "approved-request-tap",
            delivered.GrantId,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" },
            sideEffectClass: "authoring");
        Assert.True(authorization.Ok, authorization.Error?.Message);

        var consumedStatus = fixture.Service.Status(fixture.ReadAccess("status-consumed"));
        var consumed = Assert.Single(consumedStatus.Snapshot!.ApprovalRequests);
        Assert.Equal(MauiTestAgentApprovalStates.Consumed, consumed.State);
        Assert.Null(consumed.GrantId);
    }

    [Fact]
    public void ApprovalRequest_RecordsWhichIssuerDecidedWithoutNamingTheOperatingSystemUser()
    {
        var fixture = BeginFixture();
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-decided-by", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = Scope(MauiTestAgentActions.Tap),
        });
        Assert.True(submitted.Ok, submitted.Error?.Message);

        var decision = fixture.HumanDecision(approved: true);
        decision.ApprovalChannel = "host";
        decision.Actor = new MauiActorProvenance
        {
            ActorKind = "host",
            ActorId = "maui-cli-operator",
            Channel = "cli",
            Provider = "maui-cli",
        };

        var approved = fixture.Service.ApproveApprovalRequest(
            submitted.Request!.ApprovalRequestId,
            approvedScope: null,
            fixture.State,
            decision,
            grantExpiresAt: null);

        Assert.True(approved.Ok, approved.Error?.Message);
        Assert.Equal("cli/maui-cli", approved.Request!.DecidedBy);

        var status = fixture.Service.Status(fixture.ReadAccess("status-decided-by"));
        Assert.Equal("cli/maui-cli", Assert.Single(status.Snapshot!.ApprovalRequests).DecidedBy);

        var audit = fixture.Service.Audit(fixture.ReadAccess("audit-decided-by"));
        Assert.True(audit.Ok);
        var entry = Assert.Single(audit.Entries, candidate => candidate.Kind == "approval-approved");
        Assert.Equal("cli/maui-cli", entry.DecidedBy);
    }

    [Fact]
    public void ApprovalRequest_RecordsTheWorkbenchIssuerOnARejection()
    {
        var fixture = BeginFixture();
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-rejected-by", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = Scope(MauiTestAgentActions.Tap),
        });
        Assert.True(submitted.Ok, submitted.Error?.Message);

        var rejected = fixture.Service.RejectApprovalRequest(
            submitted.Request!.ApprovalRequestId,
            fixture.HumanDecision(approved: false),
            reasonCode: "scope-too-broad");

        Assert.True(rejected.Ok, rejected.Error?.Message);
        Assert.Equal(MauiTestAgentApprovalStates.Rejected, rejected.Request!.State);
        Assert.Equal("workbench/host-owned", rejected.Request.DecidedBy);

        var audit = fixture.Service.Audit(fixture.ReadAccess("audit-rejected-by"));
        var entry = Assert.Single(audit.Entries, candidate => candidate.Kind == "approval-rejected");
        Assert.Equal("workbench/host-owned", entry.DecidedBy);
    }

    [Fact]
    public void ApprovalRequest_DefaultDecisionWindowAllowsTenMinutesForHumanReview()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var fixture = BeginFixture(clock);
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-default-window", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = Scope(MauiTestAgentActions.Tap),
        });

        Assert.True(submitted.Ok, submitted.Error?.Message);
        Assert.Equal(TimeSpan.FromMinutes(10), submitted.Request!.ExpiresAt - submitted.Request.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void ApprovalRequest_NullValueLimitCanRemainNullOrNarrowToZero(int? approvedMaxValueBytes)
    {
        var fixture = BeginFixture();
        var requestedScope = Scope(MauiTestAgentActions.Tap);
        requestedScope.MaxValueBytes = null;
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope($"request-null-value-limit-{approvedMaxValueBytes}", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = requestedScope,
        });

        var approvedScope = Scope(MauiTestAgentActions.Tap);
        approvedScope.MaxValueBytes = approvedMaxValueBytes;
        var approved = fixture.Service.ApproveApprovalRequest(
            submitted.Request!.ApprovalRequestId,
            approvedScope,
            fixture.State,
            fixture.HumanDecision(approved: true),
            grantExpiresAt: null);

        Assert.True(approved.Ok, approved.Error?.Message);
        Assert.Equal(MauiTestAgentApprovalStates.Approved, approved.Request!.State);
    }

    [Fact]
    public void ApprovalGrant_RemainsBoundedAcrossItsOwnDraftDigestChanges()
    {
        var fixture = BeginFixture();
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-two-draft-actions", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.DraftAppend],
                AllowedSelectors = ["automationId:save"],
                AllowedSideEffectClasses = ["authoring"],
                MaxActionCount = 2,
                MaxValueBytes = 0,
            },
        });
        var approved = fixture.Service.ApproveApprovalRequest(
            submitted.Request!.ApprovalRequestId,
            approvedScope: null,
            fixture.State,
            fixture.HumanDecision(approved: true),
            grantExpiresAt: null);
        Assert.True(approved.Ok, approved.Error?.Message);
        var grantId = Assert.Single(
            fixture.Service.Status(fixture.ReadAccess("status-grant")).Snapshot!.ApprovalRequests).GrantId!;
        var originalCorrelation = fixture.Correlation();

        MauiTestAgentSessionResult Append(string key)
        {
            var envelope = fixture.Envelope(key, grantId);
            envelope.Correlation = new MauiTestAgentCorrelation
            {
                AuthoringSessionId = originalCorrelation.AuthoringSessionId,
                PlanId = originalCorrelation.PlanId,
                PlanRevision = originalCorrelation.PlanRevision,
                PlanDigest = originalCorrelation.PlanDigest,
                FlowId = originalCorrelation.FlowId,
                FlowRevision = originalCorrelation.FlowRevision,
                FlowDigest = originalCorrelation.FlowDigest,
            };
            var authorization = fixture.Service.AuthorizeMutation(new MauiTestAgentMutationAuthorizationRequest
            {
                Envelope = envelope,
                Action = MauiTestAgentActions.DraftAppend,
                Selector = new FlowSelector { AutomationId = "save" },
                SideEffectClass = "authoring",
                CurrentTargetState = fixture.State,
            });
            Assert.True(authorization.Ok, authorization.Error?.Message);
            return fixture.Service.AppendAction(
                fixture.SessionId,
                authorization.AuthorizationId,
                new MauiTestAgentActionRequest
                {
                    Envelope = envelope,
                    AuthorizationId = authorization.AuthorizationId,
                    Action = MauiTestAgentActions.Tap,
                    Selector = new FlowSelector { AutomationId = "save" },
                    AppendDraft = true,
                    Execute = false,
                });
        }

        Assert.True(Append("approved-step-1").Ok);
        var second = Append("approved-step-2");
        Assert.True(second.Ok, second.Error?.Message);
        Assert.Equal(2, second.Snapshot!.Flow!.Steps.Count);
        var consumed = Assert.Single(second.Snapshot.ApprovalRequests);
        Assert.Equal(MauiTestAgentApprovalStates.Consumed, consumed.State);
        Assert.Null(consumed.GrantId);
    }

    [Fact]
    public void ApprovalRequest_CannotBeBroadenedAndStaleTargetFailsClosed()
    {
        var fixture = BeginFixture();
        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-bounded", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = Scope(MauiTestAgentActions.Tap),
        });
        Assert.True(submitted.Ok, submitted.Error?.Message);

        var broadened = fixture.Service.ApproveApprovalRequest(
            submitted.Request!.ApprovalRequestId,
            new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Tap, MauiTestAgentActions.Fill],
                AllowedSelectors = ["automationId:save"],
                AllowedSideEffectClasses = ["ui"],
                MaxActionCount = 2,
                MaxValueBytes = 64,
            },
            fixture.State,
            fixture.HumanDecision(approved: true),
            grantExpiresAt: null);
        Assert.False(broadened.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ApprovalRequestScopeDenied, broadened.Error?.Code);

        var stale = fixture.Service.ApproveApprovalRequest(
            submitted.Request.ApprovalRequestId,
            approvedScope: null,
            new MauiTestAgentTargetState
            {
                AgentId = fixture.State.AgentId,
                AgentInstanceId = fixture.State.AgentInstanceId,
                AppBuildFingerprint = "different-build",
                SeedFingerprint = fixture.State.SeedFingerprint,
            },
            fixture.HumanDecision(approved: true),
            grantExpiresAt: null);
        Assert.False(stale.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantStale, stale.Error?.Code);
        Assert.Equal(
            MauiTestAgentApprovalStates.Stale,
            fixture.Service.GetApprovalRequest(submitted.Request.ApprovalRequestId, includeGrant: true).Request!.State);
    }

    [Fact]
    public void ApprovalRequest_DefaultExpiryClampsToRemainingSessionLifetime()
    {
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new MutableTimeProvider(startedAt);
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(10),
                ApprovalRequestLifetime = TimeSpan.FromMinutes(5),
            });
        clock.Advance(TimeSpan.FromMinutes(8));

        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-clamped-expiry", grantId: null),
            Kind = MauiTestAgentApprovalKinds.Commit,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.AuthorCommit],
                AllowedSideEffectClasses = ["authoring"],
                MaxActionCount = 1,
                MaxValueBytes = 0,
            },
        });

        Assert.True(submitted.Ok);
        Assert.Equal(startedAt.AddMinutes(10), submitted.Request!.ExpiresAt);
    }

    [Fact]
    public void ApprovalRequest_RejectAndExpiryNeverIssueGrant()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var fixture = BeginFixture(
            clock,
            new TestAgentSessionServiceOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(10),
                ApprovalRequestLifetime = TimeSpan.FromSeconds(1),
                ApprovalRetention = TimeSpan.FromMinutes(1),
            });
        var rejected = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-reject", grantId: null),
            Kind = MauiTestAgentApprovalKinds.Assertion,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Assert],
                AllowedSelectors = ["automationId:save"],
                AllowedSideEffectClasses = ["authoring"],
                MaxActionCount = 1,
                MaxValueBytes = 0,
            },
        });
        var rejection = fixture.Service.RejectApprovalRequest(
            rejected.Request!.ApprovalRequestId,
            fixture.HumanDecision(approved: false),
            "not-this-test");
        Assert.True(rejection.Ok);
        Assert.Equal(MauiTestAgentApprovalStates.Rejected, rejection.Request!.State);
        Assert.Null(rejection.Request.GrantId);

        var expiring = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("request-expire", grantId: null),
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            Scope = Scope(MauiTestAgentActions.Tap),
        });
        clock.Advance(TimeSpan.FromSeconds(2));
        var expired = fixture.Service.GetApprovalRequest(expiring.Request!.ApprovalRequestId, includeGrant: true);
        Assert.Equal(MauiTestAgentApprovalStates.Expired, expired.Request!.State);
        Assert.Null(expired.Request.GrantId);
    }

    [Fact]
    public void SelectorProjection_ReturnsValueFreeDurableAndAmbiguousIdentities()
    {
        var projection = TestAgentImprovementsTool.BuildSelectorProjection(
        [
            new MauiSelectorObservationElement
            {
                Id = "entry",
                Type = "Entry",
                AutomationId = "NewTodoEntry",
                IsVisible = true,
                IsEnabled = true,
            },
            new MauiSelectorObservationElement
            {
                Id = "add",
                Type = "Button",
                AutomationId = "AddButton",
                IsVisible = true,
                IsEnabled = true,
            },
            new MauiSelectorObservationElement
            {
                Id = "check-1",
                Type = "CheckBox",
                AutomationId = "TodoCheckBox",
                IsVisible = true,
                IsEnabled = true,
            },
            new MauiSelectorObservationElement
            {
                Id = "check-2",
                Type = "CheckBox",
                AutomationId = "TodoCheckBox",
                IsVisible = true,
                IsEnabled = true,
            },
        ]);

        Assert.Equal(4, projection.Count);
        Assert.Contains(projection, item =>
            item.Selector == "automationId:NewTodoEntry" &&
            item.Quality == "durable" &&
            item.MatchCount == 1);
        Assert.Equal(
            2,
            projection.Count(item =>
                item.Selector == "automationId:TodoCheckBox" &&
                item.Quality == "ambiguous" &&
                item.MatchCount == 2));
    }

    [Fact]
    public void SelectorProjection_DuplicateScopedItemKeysRemainAmbiguous()
    {
        var digest = "sha256:" + new string('a', 64);
        var projection = TestAgentImprovementsTool.BuildSelectorProjection(
        [
            new MauiSelectorObservationElement
            {
                Id = "check-1",
                Type = "CheckBox",
                AutomationId = "TodoCheckBox",
                StableItemKey = digest,
                CollectionScope = "TodoList",
                IsVisible = true,
                IsEnabled = true,
            },
            new MauiSelectorObservationElement
            {
                Id = "check-2",
                Type = "CheckBox",
                AutomationId = "TodoCheckBox",
                StableItemKey = digest,
                CollectionScope = "TodoList",
                IsVisible = true,
                IsEnabled = true,
            },
        ]);

        Assert.All(projection, item =>
        {
            Assert.Equal(2, item.MatchCount);
            Assert.Equal("ambiguous", item.Quality);
            Assert.Equal(digest, item.StableItemKeyDigest);
        });
    }

    [Fact]
    public void Exploration_WithoutApprovedPlanBudget_FailsClosed()
    {
        // No explorationBudget in the plan means no exploration, even with a valid grant. The
        // budget is the human-approved allowance; absent it there is nothing to spend.
        var fixture = BeginFixture();
        var grant = fixture.IssueGrant(ExplorationScope());
        Assert.True(grant.Ok, grant.Error?.Message);

        var explore = fixture.Explore("explore-1", grant.GrantId, selector: new FlowSelector { AutomationId = "save" });

        Assert.False(explore.Ok);
        Assert.False(explore.DispatchAllowed == true);
        Assert.Null(explore.AuthorizationId);
        Assert.Equal(MauiTestAgentErrorCodes.ExplorationBudgetRequired, explore.Error?.Code);
        Assert.False(fixture.Budget().Declared);
    }

    [Fact]
    public void Exploration_ScopeOutsideApprovedAllowedScopes_IsDenied()
    {
        var fixture = BeginExplorationFixture(allowedScopes: ["settings-path"]);
        var grant = fixture.IssueGrant(ExplorationScope());
        Assert.True(grant.Ok, grant.Error?.Message);

        var explore = fixture.Explore(
            "explore-1",
            grant.GrantId,
            scope: "checkout-path",
            selector: new FlowSelector { AutomationId = "save" });

        Assert.False(explore.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ExplorationScopeDenied, explore.Error?.Code);
        // A denied scope must not be charged: the allowance still reads full.
        Assert.Equal(2, fixture.Budget().RemainingActions);
    }

    [Fact]
    public void Exploration_ActionBudgetIsCountedDownByBrokerEvenWhenTheGrantHasHeadroom()
    {
        // The grant permits four actions; the approved plan permits two exploration steps. The
        // budget is a separate server-side counter, so the third step is refused while the grant
        // itself is still live. This is the difference between an enforced and a declarative budget.
        var fixture = BeginExplorationFixture(maxActions: 2);
        var grant = fixture.IssueGrant(ExplorationScope(maxActionCount: 4));
        Assert.True(grant.Ok, grant.Error?.Message);
        var selector = new FlowSelector { AutomationId = "save" };

        Assert.Equal(2, fixture.Budget().RemainingActions);

        var first = fixture.Explore("explore-1", grant.GrantId, selector: selector);
        Assert.True(first.Ok, first.Error?.Message);
        Assert.True(first.DispatchAllowed);
        Assert.Equal(1, first.ExplorationBudget?.RemainingActions);
        Assert.Equal(1, fixture.Budget().RemainingActions);

        var second = fixture.Explore("explore-2", grant.GrantId, action: MauiTestAgentActions.Back);
        Assert.True(second.Ok, second.Error?.Message);
        Assert.Equal(0, second.ExplorationBudget?.RemainingActions);
        Assert.True(second.ExplorationBudget?.Exhausted);

        var third = fixture.Explore("explore-3", grant.GrantId, selector: selector);
        Assert.False(third.Ok);
        Assert.Null(third.AuthorizationId);
        Assert.Equal(MauiTestAgentErrorCodes.ExplorationBudgetExhausted, third.Error?.Code);
        Assert.Equal(0, third.ExplorationBudget?.RemainingActions);

        var budget = fixture.Budget();
        Assert.Equal(2, budget.MaxActions);
        Assert.Equal(2, budget.UsedActions);
        Assert.Equal(0, budget.RemainingActions);
        Assert.True(budget.Exhausted);
        Assert.Equal(["settings-path"], budget.AllowedScopes);

        // The refused step did not consume the grant's own action allowance, but the grant is
        // still not spendable on the ordinary action route, even when the caller names the
        // exploration side-effect class itself. Otherwise maui_test_action would be a budget
        // bypass: it would spend an exploration grant without touching the counter.
        var bypass = fixture.Authorize(
            "ordinary",
            grant.GrantId,
            MauiTestAgentActions.Tap,
            selector: selector,
            sideEffectClass: TestAgentSessionService.ExplorationSideEffectClass);
        Assert.False(bypass.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantScopeDenied, bypass.Error?.Code);
        Assert.Equal(2, fixture.Budget().UsedActions);
    }

    [Fact]
    public void Exploration_GrantCannotBeSpentOnTheOrdinaryActionRouteAndViceVersa()
    {
        // The two grant families are disjoint by side-effect class, which is what keeps the budget
        // counter on the only path that can spend an exploration approval.
        var fixture = BeginExplorationFixture(maxActions: 4);
        var selector = new FlowSelector { AutomationId = "save" };

        var explorationGrant = fixture.IssueGrant(ExplorationScope());
        Assert.True(explorationGrant.Ok, explorationGrant.Error?.Message);

        foreach (var sideEffectClass in new string?[] { null, "ui", TestAgentSessionService.ExplorationSideEffectClass })
        {
            var denied = fixture.Authorize(
                $"ordinary-{sideEffectClass ?? "none"}",
                explorationGrant.GrantId,
                MauiTestAgentActions.Tap,
                selector: selector,
                sideEffectClass: sideEffectClass);
            Assert.False(denied.Ok);
            Assert.Equal(MauiTestAgentErrorCodes.MutationGrantScopeDenied, denied.Error?.Code);
        }

        // An ordinary UI grant is likewise unusable for exploration, so exploration cannot be
        // performed against an approval a human granted for a single named tap.
        var uiGrant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Tap],
            AllowedSelectors = ["automationId:save"],
            AllowedSideEffectClasses = ["ui"],
            MaxActionCount = 4,
            MaxValueBytes = 0,
        });
        Assert.True(uiGrant.Ok, uiGrant.Error?.Message);

        var explorationDenied = fixture.Explore("explore-with-ui-grant", uiGrant.GrantId, selector: selector);
        Assert.False(explorationDenied.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantScopeDenied, explorationDenied.Error?.Code);
        Assert.Equal(0, fixture.Budget().UsedActions);
    }

    [Fact]
    public void Exploration_AuthorizationDispatchesOnlyTheOneNavigationStepItAuthorized()
    {
        // One bounded navigation step must not buy an arbitrary flow. The budget has to bound what
        // the agent may do, not merely how often it may dispatch, so the authorization is pinned to
        // a single-step flow that reduces to the exact action, selector, and route it approved.
        var fixture = BeginExplorationFixture(maxActions: 2);
        var grant = fixture.IssueGrant(ExplorationScope());
        Assert.True(grant.Ok, grant.Error?.Message);

        var step = fixture.Explore("explore-1", grant.GrantId, selector: new FlowSelector { AutomationId = "save" });
        Assert.True(step.Ok, step.Error?.Message);

        static FlowStep Step(string action, string? automationId = null, string? route = null, FlowSelector? argsSelector = null) => new()
        {
            Seq = 1,
            Action = action,
            Target = automationId is null ? null : new FlowSelector { AutomationId = automationId },
            Value = route,
            Args = route is null && argsSelector is null
                ? null
                : new FlowStepArgs { Route = route, Selector = argsSelector },
        };

        void Refused(IReadOnlyList<FlowStep>? steps, string because)
        {
            Assert.False(
                fixture.Service.CanDispatchRunAuthorization(
                    step.AuthorizationId,
                    Target().AgentId,
                    Target().AgentInstanceId,
                    steps,
                    fixture.Correlation().FlowDigest,
                    out var error),
                because);
            Assert.Contains("exactly the one navigation step", error, StringComparison.Ordinal);
        }

        Refused([Step("tap", "save"), Step("fill", "name")], "a multi-step flow is not one step");
        Refused([Step("fill", "save")], "a different action is not the authorized step");
        Refused([Step("tap", "delete-account")], "a different element is not the authorized step");
        Refused([Step("tap")], "dropping the selector is not the authorized step");
        Refused(null, "an unreadable flow must fail closed");
        Refused([], "an empty flow is not the authorized step");

        // A step carries the selector twice and the runner drives args.selector, so binding to
        // target would let a forged step be approved for "save" and executed against something
        // else. Both the disagreement itself and the substituted element must be refused.
        Refused(
            [Step("tap", "save", argsSelector: new FlowSelector { AutomationId = "delete-account" })],
            "args.selector is what the runner drives, so it cannot disagree with the approved target");
        Refused(
            [Step("tap", argsSelector: new FlowSelector { AutomationId = "delete-account" })],
            "moving the substitution into args.selector alone is still a different element");

        // An unkeyed selector has no durable identity, so it must not collide with the digest of
        // an authorization that named a real element.
        Refused(
            [Step("tap", argsSelector: new FlowSelector { Text = "Delete account" })],
            "a text-only selector has no scope key and cannot stand in for an approved element");

        // The honest producer sets both copies to the same selector; that must keep working.
        Assert.True(
            fixture.Service.CanDispatchRunAuthorization(
                step.AuthorizationId,
                Target().AgentId,
                Target().AgentInstanceId,
                [Step("tap", "save", argsSelector: new FlowSelector { AutomationId = "save" })],
                fixture.Correlation().FlowDigest,
                out _));

        // The step it actually authorized still dispatches, exactly once.
        Assert.True(
            fixture.Service.TryConsumeRunDispatchAuthorization(
                step.AuthorizationId,
                Target().AgentId,
                Target().AgentInstanceId,
                [Step("tap", "save")],
                fixture.Correlation().FlowDigest,
                out var allowed),
            allowed);
        Assert.False(fixture.Service.TryConsumeRunDispatchAuthorization(
            step.AuthorizationId,
            Target().AgentId,
            Target().AgentInstanceId,
            [Step("tap", "save")],
            fixture.Correlation().FlowDigest,
            out var reused));
        Assert.Contains("already used to start a workflow run", reused, StringComparison.Ordinal);
    }

    [Fact]
    public void Exploration_StepWithNoDurableIdentityIsRefusedBeforeAnyBudgetIsSpent()
    {
        // The step digest is the only thing binding one budget unit to one dispatched step, so a
        // step that cannot be pinned must never be minted. A text-only selector has no scope key,
        // and hashing it as "no selector" would let one approved tap stand in for a tap on any
        // other unkeyed element.
        var fixture = BeginExplorationFixture(maxActions: 3);
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Tap, MauiTestAgentActions.Scroll, MauiTestAgentActions.Navigate, MauiTestAgentActions.Back],
            AllowedSelectors = ["automationId:save"],
            AllowedRoutes = ["//settings"],
            AllowedSideEffectClasses = [TestAgentSessionService.ExplorationSideEffectClass],
            MaxActionCount = 8,
            MaxValueBytes = 0,
        });
        Assert.True(grant.Ok, grant.Error?.Message);

        void Refused(string because, string key, string action, FlowSelector? selector = null, string? route = null)
        {
            var result = fixture.Explore(key, grant.GrantId, action: action, selector: selector, route: route);
            Assert.False(result.Ok, because);
            Assert.Equal(MauiTestAgentErrorCodes.ExplorationScopeDenied, result.Error?.Code);
            Assert.Null(result.AuthorizationId);
        }

        Refused("a tap with no selector names nothing", "explore-1", MauiTestAgentActions.Tap);
        Refused(
            "a text-only selector has no durable key",
            "explore-2",
            MauiTestAgentActions.Tap,
            new FlowSelector { Text = "Delete account" });
        Refused("a scroll with no selector names nothing", "explore-3", MauiTestAgentActions.Scroll);
        Refused("a navigation with no route names nothing", "explore-4", MauiTestAgentActions.Navigate);
        Refused(
            "a selector on a navigation would go unrecorded",
            "explore-5",
            MauiTestAgentActions.Navigate,
            new FlowSelector { AutomationId = "save" },
            route: "//settings");

        // None of those refusals may be charged.
        Assert.Equal(3, fixture.Budget().RemainingActions);

        // A step that can be pinned still works, so the guard is not simply refusing everything.
        var keyed = fixture.Explore("explore-ok", grant.GrantId, selector: new FlowSelector { AutomationId = "save" });
        Assert.True(keyed.Ok, keyed.Error?.Message);
        var navigated = fixture.Explore("explore-nav", grant.GrantId, action: MauiTestAgentActions.Navigate, route: "//settings");
        Assert.True(navigated.Ok, navigated.Error?.Message);
        Assert.Equal(1, fixture.Budget().RemainingActions);
    }

    [Fact]
    public void Exploration_ReplayingAnIdempotencyKeyUnderADifferentScopeIsRefused()
    {
        // One idempotency key names one step. Reusing it for a different approved scope is a new
        // request, and silently returning the first authorization would misattribute the step.
        var fixture = BeginExplorationFixture(maxActions: 4, allowedScopes: ["settings-path", "cart-path"]);
        var grant = fixture.IssueGrant(ExplorationScope());
        Assert.True(grant.Ok, grant.Error?.Message);
        var selector = new FlowSelector { AutomationId = "save" };

        var first = fixture.Explore("explore-1", grant.GrantId, selector: selector);
        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(1, fixture.Budget().UsedActions);

        var reused = fixture.Explore("explore-1", grant.GrantId, scope: "cart-path", selector: selector);
        Assert.False(reused.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.IdempotencyReused, reused.Error?.Code);
        Assert.Equal(1, fixture.Budget().UsedActions);
    }

    [Fact]
    public void Exploration_ApprovalRequestScopePreservesWhatTheHumanWillRead()
    {
        // The approval path is where a human sees the request. An exploration approval keeps the
        // side-effect classes the agent asked for, because rewriting them would hand back a grant
        // that no longer matches the text that was approved. Asking for "exploration" is therefore
        // a deliberate, visible act — and it is what makes the grant spendable only on the
        // budget-enforcing route.
        var fixture = BeginFixture();

        var ordinary = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("approval-ui", grantId: null),
            Kind = MauiTestAgentApprovalKinds.Exploration,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Tap, MauiTestAgentActions.Fill],
                AllowedSelectors = ["automationId:save"],
                AllowedSideEffectClasses = ["ui"],
                MaxActionCount = 2,
                MaxValueBytes = 16,
            },
        });

        // An ordinary typed-action approval still works: exploration did not take over this kind.
        Assert.True(ordinary.Ok, ordinary.Error?.Message);
        Assert.Equal(["ui"], ordinary.Request!.RequestedScope!.AllowedSideEffectClasses);

        var exploration = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("approval-explore", grantId: null),
            Kind = MauiTestAgentApprovalKinds.Exploration,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Tap, MauiTestAgentActions.Back],
                AllowedSelectors = ["automationId:save"],
                AllowedSideEffectClasses = [TestAgentSessionService.ExplorationSideEffectClass],
                MaxActionCount = 2,
                MaxValueBytes = 0,
            },
        });
        Assert.True(exploration.Ok, exploration.Error?.Message);
        Assert.Equal(
            [TestAgentSessionService.ExplorationSideEffectClass],
            exploration.Request!.RequestedScope!.AllowedSideEffectClasses);
    }

    [Fact]
    public void Exploration_DoesNotTakeOverTheOnlyApprovalKindThatAuthorizesOrdinaryTypedActions()
    {
        // Regression guard. The exploration kind is the only approval kind that yields a "ui"
        // grant, so pinning its side-effect classes to "exploration" would have left maui_test_action
        // with no route to a human-approved executable grant at all — a silent, permanent denial
        // that burns a human decision. Approving one end to end proves the ordinary path survives.
        var fixture = BeginFixture();

        var submitted = fixture.Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
        {
            Envelope = fixture.Envelope("approval-typed-action", grantId: null),
            Kind = MauiTestAgentApprovalKinds.Exploration,
            Scope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Tap],
                AllowedSelectors = ["automationId:save"],
                AllowedSideEffectClasses = ["ui"],
                MaxActionCount = 1,
                MaxValueBytes = 0,
            },
        });
        Assert.True(submitted.Ok, submitted.Error?.Message);

        var approved = fixture.Service.ApproveApprovalRequest(
            submitted.Request!.ApprovalRequestId,
            approvedScope: null,
            fixture.State,
            fixture.HumanDecision(approved: true),
            grantExpiresAt: null);
        Assert.True(approved.Ok, approved.Error?.Message);

        var delivered = Assert.Single(fixture.Service.Status(fixture.ReadAccess("status-typed")).Snapshot!.ApprovalRequests);
        var authorization = fixture.Authorize(
            "typed-action-tap",
            delivered.GrantId,
            MauiTestAgentActions.Tap,
            selector: new FlowSelector { AutomationId = "save" },
            sideEffectClass: "ui");
        Assert.True(authorization.Ok, authorization.Error?.Message);
    }

    [Fact]
    public void Exploration_PlanBudgetScopeListIsBoundedAtNormalization()
    {
        // The scope list is echoed on every session snapshot, so an oversized plan is trimmed
        // rather than retained verbatim.
        var fixture = BeginExplorationFixture(
            maxActions: 2,
            allowedScopes: Enumerable.Range(0, 80).Select(i => $"scope-{i}"));

        var budget = fixture.Budget();
        Assert.Equal(32, budget.AllowedScopes.Count);
        Assert.Equal("scope-0", budget.AllowedScopes[0]);
        Assert.DoesNotContain("scope-40", budget.AllowedScopes);
    }

    [Fact]
    public void Exploration_DurationBudgetElapses_DeniesFurtherStepsWhileActionsRemain()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var fixture = BeginExplorationFixture(maxActions: 5, maxDurationSeconds: 60, clock: clock);
        var grant = fixture.IssueGrant(ExplorationScope());
        Assert.True(grant.Ok, grant.Error?.Message);
        var selector = new FlowSelector { AutomationId = "save" };

        // The window opens on the first authorized step, not at session begin.
        Assert.Null(fixture.Budget().StartedAt);
        var first = fixture.Explore("explore-1", grant.GrantId, selector: selector);
        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(60, first.ExplorationBudget?.RemainingSeconds);
        Assert.NotNull(fixture.Budget().StartedAt);

        clock.Advance(TimeSpan.FromSeconds(61));

        var second = fixture.Explore("explore-2", grant.GrantId, action: MauiTestAgentActions.Back);
        Assert.False(second.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ExplorationBudgetExhausted, second.Error?.Code);
        Assert.Equal(0, second.ExplorationBudget?.RemainingSeconds);
        // Actions were still available; time alone closed the door.
        Assert.Equal(4, second.ExplorationBudget?.RemainingActions);
    }

    [Fact]
    public void Exploration_RejectsNonNavigationActionsAndNonExplorationGrants()
    {
        var fixture = BeginExplorationFixture(maxActions: 4);
        var explorationGrant = fixture.IssueGrant(ExplorationScope());
        Assert.True(explorationGrant.Ok, explorationGrant.Error?.Message);

        // Data entry is not exploration, whatever the grant says.
        var fill = fixture.Explore("explore-fill", explorationGrant.GrantId, action: MauiTestAgentActions.Fill);
        Assert.False(fill.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.UnsupportedOperation, fill.Error?.Code);

        // A grant that also permits drafting is not an exploration grant and cannot be spent here,
        // even for a navigation action.
        var draftGrant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Tap, MauiTestAgentActions.DraftAppend],
            AllowedSelectors = ["automationId:save"],
            AllowedSideEffectClasses = [TestAgentSessionService.ExplorationSideEffectClass],
            MaxActionCount = 4,
            MaxValueBytes = 0,
        });
        Assert.True(draftGrant.Ok, draftGrant.Error?.Message);
        var drafting = fixture.Explore(
            "explore-draft",
            draftGrant.GrantId,
            selector: new FlowSelector { AutomationId = "save" });
        Assert.False(drafting.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantScopeDenied, drafting.Error?.Code);

        // A run grant forbids exploration outright: ApprovalKindAllowsScope excludes run and cancel.
        var runGrant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Run],
            AllowedSideEffectClasses = ["run"],
            MaxActionCount = 1,
            MaxValueBytes = 0,
        });
        Assert.True(runGrant.Ok, runGrant.Error?.Message);
        var running = fixture.Explore("explore-run", runGrant.GrantId);
        Assert.False(running.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantScopeDenied, running.Error?.Code);

        // None of the refusals were charged to the allowance.
        Assert.Equal(4, fixture.Budget().RemainingActions);
    }

    [Fact]
    public void Exploration_RequiresGrantThatApprovedTheExplorationSideEffectClass()
    {
        // An ordinary UI grant cannot be repurposed for exploration: the broker, not the caller,
        // sets the side-effect class, so a grant that never approved "exploration" is refused.
        var fixture = BeginExplorationFixture(maxActions: 3);
        var grant = fixture.IssueGrant(new MauiTestAgentMutationScope
        {
            AllowedActions = [MauiTestAgentActions.Tap],
            AllowedSelectors = ["automationId:save"],
            AllowedSideEffectClasses = ["ui"],
            MaxActionCount = 3,
            MaxValueBytes = 0,
        });
        Assert.True(grant.Ok, grant.Error?.Message);

        var explore = fixture.Explore(
            "explore-1",
            grant.GrantId,
            selector: new FlowSelector { AutomationId = "save" });

        Assert.False(explore.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.MutationGrantScopeDenied, explore.Error?.Code);
        Assert.Equal(3, fixture.Budget().RemainingActions);
    }

    [Fact]
    public void Exploration_ReplayedRequestReturnsPriorAuthorizationWithoutDoubleCharging()
    {
        var fixture = BeginExplorationFixture(maxActions: 1);
        var grant = fixture.IssueGrant(ExplorationScope());
        Assert.True(grant.Ok, grant.Error?.Message);
        var selector = new FlowSelector { AutomationId = "save" };

        var first = fixture.Explore("explore-1", grant.GrantId, selector: selector);
        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(0, first.ExplorationBudget?.RemainingActions);

        // The same idempotency key and request replays the prior authorization. A retry of the last
        // allowed step must neither be refused as exhausted nor charged a second time.
        var replay = fixture.Explore("explore-1", grant.GrantId, selector: selector);
        Assert.True(replay.Ok, replay.Error?.Message);
        Assert.Equal(first.AuthorizationId, replay.AuthorizationId);
        Assert.Equal(1, fixture.Budget().UsedActions);
    }

    [Fact]
    public void Exploration_BrokerCapClampsAnOverlyGenerousPlanBudget()
    {
        // The plan is agent-authored input. Even an approved plan cannot vote itself a larger
        // allowance than broker policy permits.
        var fixture = BeginExplorationFixture(
            maxActions: 1_000,
            options: new TestAgentSessionServiceOptions { MaxExplorationActions = 1 });
        var grant = fixture.IssueGrant(ExplorationScope());
        Assert.True(grant.Ok, grant.Error?.Message);

        Assert.Equal(1, fixture.Budget().MaxActions);
        var first = fixture.Explore("explore-1", grant.GrantId, selector: new FlowSelector { AutomationId = "save" });
        Assert.True(first.Ok, first.Error?.Message);

        var second = fixture.Explore("explore-2", grant.GrantId, action: MauiTestAgentActions.Back);
        Assert.False(second.Ok);
        Assert.Equal(MauiTestAgentErrorCodes.ExplorationBudgetExhausted, second.Error?.Code);
    }

    private static Fixture BeginFixture(
        MutableTimeProvider? clock = null,
        TestAgentSessionServiceOptions? options = null,
        MauiFlow? flow = null)
    {
        var service = new TestAgentSessionService(options, clock);
        var state = State();
        var begin = service.Begin(new MauiTestAgentSessionBeginRequest
        {
            Envelope = Envelope(Target(), "begin"),
            TargetState = state,
            Flow = flow,
        });
        Assert.True(begin.Ok, begin.Error?.Message);
        return new Fixture(service, state, begin.Snapshot!);
    }

    private static Fixture BeginExplorationFixture(
        int? maxActions = 2,
        int? maxDurationSeconds = 120,
        IEnumerable<string>? allowedScopes = null,
        MutableTimeProvider? clock = null,
        TestAgentSessionServiceOptions? options = null)
    {
        var service = new TestAgentSessionService(options, clock);
        var state = State();
        var begin = service.Begin(new MauiTestAgentSessionBeginRequest
        {
            Envelope = Envelope(Target(), "begin"),
            TargetState = state,
            Plan = new MauiTestPlan
            {
                Title = "Exploration draft",
                ExplorationBudget = new MauiExplorationBudget
                {
                    MaxActions = maxActions,
                    MaxDurationSeconds = maxDurationSeconds,
                    AllowedScopes = [.. allowedScopes ?? ["settings-path"]],
                },
            },
        });
        Assert.True(begin.Ok, begin.Error?.Message);
        return new Fixture(service, state, begin.Snapshot!);
    }

    private static MauiTestAgentMutationScope ExplorationScope(int maxActionCount = 4) => new()
    {
        AllowedActions = [MauiTestAgentActions.Tap, MauiTestAgentActions.Back],
        AllowedSelectors = ["automationId:save"],
        AllowedSideEffectClasses = [TestAgentSessionService.ExplorationSideEffectClass],
        MaxActionCount = maxActionCount,
        MaxValueBytes = 0,
    };

    private static MauiTestAgentTarget Target() => new()
    {
        AgentId = "agent-a",
        AgentInstanceId = "instance-a",
        AppBuildFingerprint = "build-a",
        SeedFingerprint = "seed-a",
    };

    private static MauiTestAgentTargetState State() => new()
    {
        AgentId = "agent-a",
        AgentInstanceId = "instance-a",
        AppBuildFingerprint = "build-a",
        SeedFingerprint = "seed-a",
        Route = "//home",
        Window = "main-window",
    };

    private static MauiTestAgentRequestEnvelope Envelope(MauiTestAgentTarget target, string key) => new()
    {
        RequestId = "req-" + key,
        IdempotencyKey = "idem-" + key,
        Target = target,
        Correlation = new MauiTestAgentCorrelation(),
        Provenance = new MauiActorProvenance
        {
            ActorKind = "agent",
            ActorId = "agent-author",
            Channel = "mcp",
            Provider = "host-owned",
        },
        Intent = "Test authoring intent; do not expose this prompt.",
        DeadlineMs = 30_000,
        PolicyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
    };

    private static MauiTestAgentMutationScope Scope(string action) => new()
    {
        AllowedActions = [action],
        AllowedSelectors = ["automationId:save"],
        AllowedSideEffectClasses =
            [action == MauiTestAgentActions.DraftAppend ? "authoring" : "ui"],
        MaxActionCount = 1,
        MaxValueBytes = 64,
    };

    private sealed class Fixture
    {
        private int _grantSequence;

        public Fixture(TestAgentSessionService service, MauiTestAgentTargetState state, MauiTestAgentAuthoringSnapshot snapshot)
        {
            Service = service;
            State = state;
            Snapshot = snapshot;
        }

        public TestAgentSessionService Service { get; }
        public MauiTestAgentTargetState State { get; }
        public MauiTestAgentAuthoringSnapshot Snapshot { get; }
        public string SessionId => Snapshot.SessionId!;
        public string ReadCapability => Snapshot.ReadCapabilityId!;

        private MauiTestAgentAuthoringSnapshot CurrentSnapshot()
            => Service.Status(ReadAccess("current-snapshot")).Snapshot!;

        public MauiTestAgentSessionAccessRequest ReadAccess(string key) => new()
        {
            SessionId = SessionId,
            ReadCapabilityId = ReadCapability,
            Envelope = new MauiTestAgentRequestEnvelope
            {
                RequestId = "req-" + key,
                IdempotencyKey = "idem-" + key,
                Target = Target(),
                Correlation = new MauiTestAgentCorrelation
                {
                    AuthoringSessionId = SessionId,
                },
                Provenance = new MauiActorProvenance
                {
                    ActorKind = "agent",
                    ActorId = "agent-author",
                    Channel = "mcp",
                    Provider = "host-owned",
                },
                Intent = "Read the canonical authoring session.",
                ReadCapabilityId = ReadCapability,
                PolicyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
            },
        };

        public MauiTestAgentCorrelation Correlation()
        {
            var current = CurrentSnapshot();
            return new MauiTestAgentCorrelation
            {
                AuthoringSessionId = SessionId,
                PlanId = current.Plan!.PlanId,
                PlanRevision = current.Plan.Revision,
                PlanDigest = current.PlanDigest,
                FlowId = current.Plan.Flow!.FlowId,
                FlowRevision = current.FlowRevision,
                FlowDigest = current.FlowDigest,
            };
        }

        public MauiTestAgentRequestEnvelope Envelope(string key, string? grantId) => new()
        {
            RequestId = "req-" + key,
            IdempotencyKey = "idem-" + key,
            Target = Target(),
            Correlation = Correlation(),
            Provenance = new MauiActorProvenance
            {
                ActorKind = "agent",
                ActorId = "agent-author",
                Channel = "mcp",
                Provider = "host-owned",
            },
            Intent = "Test authoring intent; do not expose this prompt.",
            ApprovalGrantId = grantId,
            ReadCapabilityId = ReadCapability,
            DeadlineMs = 30_000,
            PolicyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
        };

        /// <summary>
        /// Mints a grant the only way the broker allows: submit a bounded approval request, then
        /// decide it as an owner-token approval host. There is no direct grant path to call.
        /// </summary>
        /// <remarks>
        /// The approval kind is derived from the scope so that <see cref="TestAgentSessionService"/>'s
        /// own scope normalization is a no-op and the grant carries exactly the scope the test
        /// declared. Run and commit scopes have dedicated kinds; every other scope is requested as
        /// an exploration-kind approval, the one kind that is documented to leave a reviewed
        /// side-effect class list untouched. Redemption never looks at the kind — only at the
        /// scope — so this preserves what each test is actually asserting.
        /// </remarks>
        public MauiTestAgentGrantIssueResult IssueGrant(
            MauiTestAgentMutationScope scope,
            DateTimeOffset? expiresAt = null)
        {
            ArgumentNullException.ThrowIfNull(scope);
            var key = $"grant-{Interlocked.Increment(ref _grantSequence)}";
            var submitted = Service.SubmitApprovalRequest(new MauiTestAgentApprovalSubmitRequest
            {
                Envelope = Envelope(key, grantId: null),
                Kind = ApprovalKindFor(scope),
                Scope = CloneScope(scope),
            });
            if (submitted.Ok != true || submitted.Request is null)
                return new MauiTestAgentGrantIssueResult { Error = submitted.Error };

            var approved = Service.ApproveApprovalRequest(
                submitted.Request.ApprovalRequestId,
                CloneScope(scope),
                State,
                HumanDecision(approved: true),
                expiresAt);
            if (approved.Ok != true || approved.Request is null)
                return new MauiTestAgentGrantIssueResult { Error = approved.Error };

            var issued = Service.GetApprovalRequest(
                submitted.Request.ApprovalRequestId,
                includeGrant: true).Request!;
            return new MauiTestAgentGrantIssueResult
            {
                Ok = true,
                GrantId = issued.GrantId,
                GrantDigest = Digest(issued.GrantId!),
                RemainingActions = issued.ApprovedScope!.MaxActionCount,
                ExpiresAt = issued.GrantExpiresAt,
            };
        }

        /// <summary>The digest the broker records for a grant, recomputed here to keep it test-visible.</summary>
        private static string Digest(string value)
            => Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant();

        private static string ApprovalKindFor(MauiTestAgentMutationScope scope)
        {
            if (scope.AllowedActions.Contains(MauiTestAgentActions.AuthorCommit, StringComparer.Ordinal))
                return MauiTestAgentApprovalKinds.Commit;
            if (scope.AllowedActions.Contains(MauiTestAgentActions.Run, StringComparer.Ordinal) ||
                scope.AllowedActions.Contains(MauiTestAgentActions.Cancel, StringComparer.Ordinal))
                return MauiTestAgentApprovalKinds.Run;
            return MauiTestAgentApprovalKinds.Exploration;
        }

        private static MauiTestAgentMutationScope CloneScope(MauiTestAgentMutationScope source) => new()
        {
            AllowedActions = [.. source.AllowedActions],
            AllowedSelectors = [.. source.AllowedSelectors],
            AllowedRoutes = [.. source.AllowedRoutes],
            AllowedSideEffectClasses = [.. source.AllowedSideEffectClasses],
            MaxActionCount = source.MaxActionCount,
            MaxValueBytes = source.MaxValueBytes,
        };

        public MauiTestAgentHumanApproval HumanDecision(bool approved) => new()
        {
            Approved = approved,
            ApprovalChannel = "workbench",
            Actor = new MauiActorProvenance
            {
                ActorKind = "human",
                ActorId = "reviewer",
                Channel = "workbench",
                Provider = "host-owned",
            },
        };

        public MauiTestAgentMutationAuthorizationResult Authorize(
            string key,
            string? grantId,
            string action,
            FlowSelector? selector = null,
            string? value = null,
            string? sideEffectClass = "ui",
            MauiTestAgentTargetState? state = null)
            => Service.AuthorizeMutation(new MauiTestAgentMutationAuthorizationRequest
            {
                Envelope = Envelope(key, grantId),
                Action = action,
                Selector = selector,
                SideEffectClass = sideEffectClass,
                ValueLength = value is null ? null : System.Text.Encoding.UTF8.GetByteCount(value),
                ValueDigest = value is null ? null : "super-secret",
                CurrentTargetState = state ?? State,
            });

        public MauiTestAgentExplorationResult Explore(
            string key,
            string? grantId,
            string action = MauiTestAgentActions.Tap,
            string? scope = "settings-path",
            FlowSelector? selector = null,
            string? route = null,
            MauiTestAgentTargetState? state = null)
            => Service.AuthorizeExploration(new MauiTestAgentExplorationRequest
            {
                Envelope = Envelope(key, grantId),
                Action = action,
                Scope = scope,
                Selector = selector,
                Route = route,
                CurrentTargetState = state ?? State,
            });

        public MauiTestAgentExplorationBudgetState Budget()
            => Service.Status(ReadAccess("budget-" + Guid.NewGuid().ToString("N"))).Snapshot!.ExplorationBudget!;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
