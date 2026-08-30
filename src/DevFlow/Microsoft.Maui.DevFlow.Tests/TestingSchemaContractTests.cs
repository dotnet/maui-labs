using System.Text.Json;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class TestingSchemaContractTests
{
    [Fact]
    public void TestAgentProtocol_RoundTripsEnvelopeAndTypedErrorWithSourceGeneratedContext()
    {
        const string json = """
            {
              "schema": 1,
              "requestId": "req_1",
              "idempotencyKey": "idem_1",
              "target": {
                "agentId": "agent-a",
                "agentInstanceId": "instance-a",
                "appBuildFingerprint": "build-a",
                "seedFingerprint": "seed-a"
              },
              "correlation": {
                "authoringSessionId": "author_1",
                "planId": "plan_1",
                "planRevision": 2,
                "flowId": "flow_1",
                "flowRevision": 3,
                "runId": "run_1"
              },
              "provenance": {
                "actorKind": "agent",
                "actorId": "test-agent",
                "channel": "mcp",
                "provider": "host-owned"
              },
              "intent": "Verify the saved profile",
              "approvalGrantId": "grant_opaque",
              "deadlineMs": 30000,
              "policyVersion": "test-agent-policy-v1",
              "x-envelope-extension": true
            }
            """;

        var envelope = JsonSerializer.Deserialize(
            json,
            MauiTestingJsonContext.Default.MauiTestAgentRequestEnvelope);
        Assert.NotNull(envelope);
        Assert.Equal("agent-a", envelope!.Target!.AgentId);
        Assert.Equal("instance-a", envelope.Target.AgentInstanceId);
        Assert.Equal("run_1", envelope.Correlation!.RunId);
        Assert.True(envelope.ExtensionData!.ContainsKey("x-envelope-extension"));

        var serialized = JsonSerializer.Serialize(
            envelope,
            MauiTestingJsonContext.Default.MauiTestAgentRequestEnvelope);
        AssertExtensionRoundTrips(serialized, "x-envelope-extension");

        var error = new MauiTestAgentError
        {
            Code = MauiTestAgentErrorCodes.MutationGrantReused,
            Category = MauiTestAgentErrorCategories.Authorization,
            Retryable = false,
        };
        var errorJson = JsonSerializer.Serialize(error, MauiTestingJsonContext.Default.MauiTestAgentError);
        Assert.Contains("\"mutation-grant-reused\"", errorJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TestAgentApprovalRequest_RoundTripsPendingDecisionAndGrantDelivery()
    {
        var request = new MauiTestAgentApprovalRecord
        {
            ApprovalRequestId = "approval_1",
            Kind = MauiTestAgentApprovalKinds.DraftChange,
            State = MauiTestAgentApprovalStates.Approved,
            SessionId = "author_1",
            Intent = "Add one reviewed todo",
            RequestedScope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Fill, MauiTestAgentActions.Tap],
                AllowedSelectors = ["automationId:NewTodoEntry", "automationId:AddButton"],
                AllowedSideEffectClasses = ["ui"],
                MaxActionCount = 2,
                MaxValueBytes = 64,
            },
            ApprovedScope = new MauiTestAgentMutationScope
            {
                AllowedActions = [MauiTestAgentActions.Tap],
                AllowedSelectors = ["automationId:AddButton"],
                AllowedSideEffectClasses = ["ui"],
                MaxActionCount = 1,
                MaxValueBytes = 0,
            },
            GrantId = "grant_opaque",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["x-approval-extension"] = JsonDocument.Parse("true").RootElement.Clone(),
            },
        };

        var json = JsonSerializer.Serialize(
            request,
            MauiTestingJsonContext.Default.MauiTestAgentApprovalRecord);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            MauiTestingJsonContext.Default.MauiTestAgentApprovalRecord);

        Assert.NotNull(roundTrip);
        Assert.Equal(MauiTestAgentApprovalStates.Approved, roundTrip!.State);
        Assert.Equal("grant_opaque", roundTrip.GrantId);
        Assert.Single(roundTrip.ApprovedScope!.AllowedActions);
        Assert.True(roundTrip.ExtensionData!.ContainsKey("x-approval-extension"));
    }

    [Fact]
    public void TestingContracts_RoundTripExtensionFieldsWithSourceGeneratedContext()
    {
        const string planJson = """
            {
              "schema": 1,
              "planId": "plan-login",
              "goal": "Sign in safely",
              "sideEffectPolicy": "none",
              "x-plan-extension": { "retained": true }
            }

            """;
        const string reportJson = """
            {
              "schema": 1,
              "runId": "run-1",
              "x-report-extension": [ "retained" ]
            }
            """;
        const string proposalJson = """
            {
              "schema": 1,
              "proposalId": "repair-1",
              "baseFlow": { "digest": "abc" },
              "oldSelector": { "automationId": "old" },
              "proposedSelector": { "automationId": "new" },
              "x-proposal-extension": { "retained": true }
            }
            """;
        const string outcomeJson = """
            {
              "schema": 1,
              "proposalId": "repair-1",
              "state": "verified",
              "x-outcome-extension": "retained"
            }
            """;

        var plan = JsonSerializer.Deserialize(planJson, MauiTestingJsonContext.Default.MauiTestPlan);
        var report = JsonSerializer.Deserialize(reportJson, MauiTestingJsonContext.Default.MauiFlowRunReport);
        var proposal = JsonSerializer.Deserialize(proposalJson, MauiTestingJsonContext.Default.MauiFlowRepairProposal);
        var outcome = JsonSerializer.Deserialize(outcomeJson, MauiTestingJsonContext.Default.MauiFlowRepairOutcome);

        Assert.NotNull(plan);
        Assert.NotNull(report);
        Assert.NotNull(proposal);
        Assert.NotNull(outcome);
        Assert.True(plan.ExtensionData!.ContainsKey("x-plan-extension"));
        Assert.True(report.ExtensionData!.ContainsKey("x-report-extension"));
        Assert.True(proposal.ExtensionData!.ContainsKey("x-proposal-extension"));
        Assert.True(outcome.ExtensionData!.ContainsKey("x-outcome-extension"));

        AssertExtensionRoundTrips(
            JsonSerializer.Serialize(plan, MauiTestingJsonContext.Default.MauiTestPlan),
            "x-plan-extension");
        AssertExtensionRoundTrips(
            JsonSerializer.Serialize(report, MauiTestingJsonContext.Default.MauiFlowRunReport),
            "x-report-extension");
        AssertExtensionRoundTrips(
            JsonSerializer.Serialize(proposal, MauiTestingJsonContext.Default.MauiFlowRepairProposal),
            "x-proposal-extension");
        AssertExtensionRoundTrips(
            JsonSerializer.Serialize(outcome, MauiTestingJsonContext.Default.MauiFlowRepairOutcome),
            "x-outcome-extension");
    }

    [Fact]
    public void XamlSourceProposalContract_RoundTripsSourceSpecificApprovalFields()
    {
        const string json = """
            {
              "schema": 1,
              "proposalId": "xamlproposal_1",
              "state": "awaiting-host-apply",
              "operation": {
                "operationId": "xamlop_1",
                "kind": "add-literal-automation-id",
                "fileRelativePath": "Views/MainPage.xaml",
                "sourceHash": "0123456789abcdef",
                "sourceAnchor": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "attribute": "AutomationId",
                "newLiteral": "SaveButton"
              },
              "element": {
                "elementType": "Button",
                "line": 12,
                "column": 5,
                "sourceAnchor": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
              },
              "baseContentDigest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "patch": {
                "format": "text-replace-v1",
                "beforeDigest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "afterDigest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                "start": 42,
                "length": 0,
                "replacement": " AutomationId=\"SaveButton\""
              },
              "patchDigest": "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
              "affectedFlows": [{ "flowPath": "maui-tests/save.md", "requiresSeparateApproval": true }],
              "approval": { "state": "approved", "grantDigest": "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" },
              "x-source-extension": true
            }
            """;

        var proposal = JsonSerializer.Deserialize(json, MauiTestingJsonContext.Default.MauiXamlSourceProposal);

        Assert.NotNull(proposal);
        Assert.Equal("Views/MainPage.xaml", proposal!.Operation.FileRelativePath);
        Assert.Equal("SaveButton", proposal.Operation.NewLiteral);
        Assert.True(Assert.Single(proposal.AffectedFlows).RequiresSeparateApproval);
        Assert.True(proposal.ExtensionData!.ContainsKey("x-source-extension"));
        AssertExtensionRoundTrips(
            JsonSerializer.Serialize(proposal, MauiTestingJsonContext.Default.MauiXamlSourceProposal),
            "x-source-extension");
    }

    [Fact]
    public void CSharpSourceProposalContract_RoundTripsIDEHandoffFields()
    {
        const string json = """
            {
              "schema": 1,
              "language": "CSharp",
              "proposalId": "csharpproposal_1",
              "state": "awaiting-host-apply",
              "operation": {
                "operationId": "csharpop_1",
                "kind": "add-literal-automation-id",
                "fileRelativePath": "Views/MainPage.cs",
                "sourceHash": "0123456789abcdef",
                "sourceAnchor": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "symbolId": "global::Page.save",
                "semanticType": "global::Microsoft.Maui.Controls.Button",
                "attribute": "AutomationId",
                "newLiteral": "SaveButton",
                "spanStart": 42,
                "spanLength": 18
              },
              "element": {
                "elementType": "Button",
                "path": "Views/MainPage.cs",
                "sourceAnchor": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "spanStart": 42,
                "spanLength": 18,
                "symbolId": "global::Page.save",
                "semanticType": "global::Microsoft.Maui.Controls.Button"
              },
              "baseContentDigest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "patch": {
                "format": "text-replace-v1",
                "beforeDigest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "afterDigest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                "start": 42,
                "length": 0,
                "replacement": " AutomationId = \"SaveButton\""
              },
              "rollbackPatch": {
                "format": "text-replace-v1",
                "beforeDigest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                "afterDigest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "start": 42,
                "length": 28,
                "replacement": ""
              },
              "patchDigest": "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
              "rollbackPatchDigest": "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
              "apply": {
                "state": "awaiting-host-apply",
                "hostKind": "vscode",
                "preContentDigest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "patchDigest": "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
              },
              "x-csharp-source-extension": true
            }
            """;

        var proposal = JsonSerializer.Deserialize(json, MauiTestingJsonContext.Default.MauiCSharpSourceProposal);

        Assert.NotNull(proposal);
        Assert.Equal("CSharp", proposal!.Language);
        Assert.Equal("Views/MainPage.cs", proposal.Operation.FileRelativePath);
        Assert.True(proposal.ExtensionData!.ContainsKey("x-csharp-source-extension"));
        AssertExtensionRoundTrips(
            JsonSerializer.Serialize(proposal, MauiTestingJsonContext.Default.MauiCSharpSourceProposal),
            "x-csharp-source-extension");
    }

    [Fact]
    public void ArtifactTrustContracts_RoundTripWithSourceGeneratedContext()
    {
        const string json = """
            {
              "identity": { "namespace": "imported-artifact", "id": "iat_0123456789abcdef0123456789abcdef" },
              "artifactKind": "flow-run",
              "verification": {
                "state": "untrusted",
                "reasons": [{ "code": "default-untrusted" }],
                "x-verification-extension": true
              },
              "projection": {
                "kind": "flow-run",
                "embeddedIdentifierDigests": ["sha256:abc"],
                "x-projection-extension": "retained"
              },
              "x-trust-extension": { "retained": true }
            }
            """;

        var record = JsonSerializer.Deserialize(json, MauiTestingJsonContext.Default.MauiArtifactTrustRecord);

        Assert.NotNull(record);
        Assert.True(record!.Identity!.IsValid);
        Assert.Equal(MauiArtifactTrustStates.Untrusted, record.Verification.State);
        Assert.True(record.ExtensionData!.ContainsKey("x-trust-extension"));
        Assert.True(record.Verification.ExtensionData!.ContainsKey("x-verification-extension"));
        Assert.True(record.Projection!.ExtensionData!.ContainsKey("x-projection-extension"));

        var roundTrip = JsonSerializer.Serialize(record, MauiTestingJsonContext.Default.MauiArtifactTrustRecord);
        AssertExtensionRoundTrips(roundTrip, "x-trust-extension");
        using var document = JsonDocument.Parse(roundTrip);
        Assert.True(
            document.RootElement.GetProperty("verification").TryGetProperty("x-verification-extension", out _));
        Assert.True(
            document.RootElement.GetProperty("projection").TryGetProperty("x-projection-extension", out _));
    }

    [Fact]
    public void MigrationPreview_Schema1ToSchema2PreservesUnknownFieldsWithoutInventingFacts()
    {
        const string markdown = """
            # Scenario: legacy

            ```json maui-test
            {
              "schema": 1,
              "name": "legacy",
              "x-root-extension": { "keep": true },
              "steps": [
                {
                  "seq": 1,
                  "action": "tap",
                  "args": {
                    "selector": { "automationId": "save" },
                    "x-args-extension": "keep"
                  },
                  "x-step-extension": [ 1, 2, 3 ]
                }

              ]
            }
            ```
            """;

        var parsed = FlowMarkdown.Parse(markdown);

        Assert.True(parsed.Ok, parsed.Error);
        var preview = MauiFlowMigration.Preview(parsed.Flow!);

        Assert.Equal(1, preview.SourceSchema);
        Assert.Equal(2, preview.TargetSchema);
        Assert.True(preview.CanWrite);
        Assert.True(preview.WriteRequired);
        Assert.Single(preview.Changes);
        Assert.Equal(2, preview.NormalizedFlow!.Schema);
        Assert.DoesNotContain("flowId", preview.NormalizedFlow.ExtensionData!.Keys);
        Assert.True(preview.NormalizedFlow.ExtensionData.ContainsKey("x-root-extension"));
        Assert.True(preview.NormalizedFlow.Steps[0].ExtensionData!.ContainsKey("x-step-extension"));
        Assert.True(preview.NormalizedFlow.Steps[0].Args!.ExtensionData!.ContainsKey("x-args-extension"));

        var serialized = FlowMarkdown.Serialize(preview.NormalizedFlow);
        Assert.Contains("\"x-root-extension\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"x-step-extension\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"x-args-extension\"", serialized, StringComparison.Ordinal);

        var serializedPreview = JsonSerializer.Serialize(
            preview,
            MauiTestingJsonContext.Default.MauiFlowMigrationResult);
        Assert.Contains("\"sourceSchema\": 1", serializedPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void FlowMarkdown_RoundTripsHumanReviewMetadata()
    {
        var flow = new MauiFlow
        {
            Name = "login",
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    StepId = "submit-credentials",
                    Action = FlowActions.Tap,
                    Label = "Submit credentials",
                    Intent = "Complete the sign-in action.",
                    AcceptanceCriterionIds = ["session-created"],
                    Target = new FlowSelector { AutomationId = "sign-in" },
                },
            ],
        };

        var parsed = FlowMarkdown.Parse(FlowMarkdown.Serialize(flow));

        Assert.True(parsed.Ok, parsed.Error);
        var step = Assert.Single(parsed.Flow!.Steps);
        Assert.Equal("submit-credentials", step.StepId);
        Assert.Equal("Submit credentials", step.Label);
        Assert.Equal("Complete the sign-in action.", step.Intent);
        Assert.Equal("session-created", Assert.Single(step.AcceptanceCriterionIds!));
    }

    [Fact]
    public void FlowMarkdown_LegacyFlowWithoutStepId_PreservesSequenceFallback()
    {
        const string markdown = """
            ```json maui-test
            {
              "schema": 2,
              "name": "legacy-step-identity",
              "steps": [
                {
                  "seq": 7,
                  "action": "assert",
                  "asserts": []
                }
              ]
            }
            ```
            """;

        var parsed = FlowMarkdown.Parse(markdown);

        Assert.True(parsed.Ok, parsed.Error);
        var step = Assert.Single(parsed.Flow!.Steps);
        Assert.Null(step.StepId);
        Assert.Equal(7, step.Seq);
        Assert.DoesNotContain("\"stepId\"", FlowMarkdown.Serialize(parsed.Flow), StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationPreview_Schema2DoesNotRequireWrite()
    {
        var preview = MauiFlowMigration.Preview(new MauiFlow
        {
            Schema = 2,
            Name = "current",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Assert,
                },
            },
        });

        Assert.Equal(2, preview.SourceSchema);
        Assert.Equal(2, preview.TargetSchema);
        Assert.True(preview.CanWrite);
        Assert.False(preview.WriteRequired);
        Assert.Empty(preview.Changes);
    }

    [Fact]
    public void MigrationPreview_RejectsSchema3WithoutGeneratingAFlow()
    {
        var preview = MauiFlowMigration.Preview(new MauiFlow { Schema = 2 }, targetSchema: 3);

        Assert.Equal(2, preview.SourceSchema);
        Assert.Equal(3, preview.TargetSchema);
        Assert.False(preview.CanWrite);
        Assert.False(preview.WriteRequired);
        Assert.Null(preview.NormalizedFlow);
        Assert.Contains(preview.Warnings, warning => warning.Contains("not a supported migration target"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FlowMarkdown_ParsesAndValidatesCurrentAndLegacySchemas(int schema)
    {
        var markdown = $$"""
            ```json maui-test
            {
              "schema": {{schema}},
              "name": "compatibility",
              "steps": [
                {
                  "seq": 1,
                  "action": "tap",
                  "args": { "selector": { "automationId": "save" } }
                }
              ]
            }
            ```
            """;

        var parsed = FlowMarkdown.Parse(markdown);

        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(schema, parsed.Flow!.Schema);
        Assert.True(FlowValidator.Validate(parsed.Flow).Ok);
    }

    [Fact]
    public void RequirementValidation_UnsupportedRequiredSemanticFailsClosed()
    {
        var requirements = new MauiFlowRequirements
        {
            RequiredSemantics =
            [
                new MauiRequiredSemantic
                {
                    Name = "future.checkpoint.v9",
                    Required = true,
                },
            ],
        };

        var validation = MauiFlowRequirementValidator.Validate(requirements, new MauiFlowCapabilitySet());

        Assert.False(validation.IsValid);
        var error = Assert.Single(validation.Errors);
        Assert.Equal("required-semantics-unsupported", error.Code);
        Assert.Contains("future.checkpoint.v9", error.Message, StringComparison.Ordinal);

        var serialized = JsonSerializer.Serialize(
            validation,
            MauiTestingJsonContext.Default.MauiFlowRequirementValidation);
        Assert.Contains("\"required-semantics-unsupported\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyContracts_RoundTripWithSourceGeneratedContext()
    {
        const string requestJson = """
            {
              "plan": {
                "schema": 1,
                "planId": "plan-safety",
                "sideEffectPolicy": "test-tenant-resettable",
                "checkpoint": {
                  "appBuildFingerprint": "build-1",
                  "appStateSeed": { "fingerprint": "app-seed" },
                  "backendTestDataSeed": { "fingerprint": "backend-seed" },
                  "route": "/home",
                  "window": "main",
                  "modal": "none",
                  "locale": "en-US",
                  "theme": "light",
                  "orientation": "portrait",
                  "displayProfile": "phone",
                  "collectionItemKey": "item-1"
                },
                "independentBusinessOracles": [
                  { "oracleId": "order-recorded", "required": true, "independent": true }
                ]
              },
              "context": {
                "intent": "ordinary-replay",
                "preconditions": {
                  "expected": { "appBuildFingerprint": "build-1", "route": "/home" },
                  "observed": { "appBuildFingerprint": "build-1", "route": "/home" }
                },
                "reset": {
                  "succeeded": true,
                  "appStateSucceeded": true,
                  "backendTestDataSucceeded": true,
                  "appStateSeed": { "fingerprint": "app-seed" },
                  "backendTestDataSeed": { "fingerprint": "backend-seed" }
                },
                "businessOracles": [
                  { "oracleId": "order-recorded", "succeeded": true, "independent": true }
                ]
              }
            }
            """;

        var request = JsonSerializer.Deserialize(requestJson, MauiTestingJsonContext.Default.MauiFlowRunRequest);

        Assert.NotNull(request);
        Assert.Equal(MauiFlowSideEffectPolicy.TestTenantResettable, request!.Plan!.ParsedSideEffectPolicy);
        Assert.Equal("app-seed", request.Context!.Reset!.AppStateSeed!.Fingerprint);
        Assert.Single(request.Context.BusinessOracles);
        var json = JsonSerializer.Serialize(request, MauiTestingJsonContext.Default.MauiFlowRunRequest);
        Assert.Contains("\"sideEffectPolicy\": \"test-tenant-resettable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"independentBusinessOracles\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SideEffectPolicy_UsesStableLowerCaseWireValues()
    {
        var json = JsonSerializer.Serialize(
            MauiFlowSideEffectPolicy.TestTenantResettable,
            MauiTestingJsonContext.Default.MauiFlowSideEffectPolicy);
        var parsed = JsonSerializer.Deserialize(
            "\"non-replayable\"",
            MauiTestingJsonContext.Default.MauiFlowSideEffectPolicy);

        Assert.Equal("\"test-tenant-resettable\"", json);
        Assert.Equal(MauiFlowSideEffectPolicy.NonReplayable, parsed);
    }

    [Theory]
    // Regression: a plan whose optional list fields are omitted or explicitly null used to
    // deserialize with null collections, which then reached ArgumentNullException.ThrowIfNull deep
    // in platform-tag parsing and surfaced as `unexpected-execution-error` / class
    // `infrastructure`. An absent optional field is a valid, unconstrained plan.
    [InlineData("""{"planId":"p","revision":1}""")]
    [InlineData("""{"planId":"p","revision":1,"requiredPlatforms":null}""")]
    [InlineData("""{"planId":"p","revision":1,"requiredPlatforms":null,"acceptanceCriteria":null,"risks":null}""")]
    public void MauiTestPlan_OmittedOrNullOptionalLists_DeserializeToEmptyNotNull(string json)
    {
        var plan = JsonSerializer.Deserialize(json, MauiTestingJsonContext.Default.MauiTestPlan);

        Assert.NotNull(plan);
        Assert.NotNull(plan.RequiredPlatforms);
        Assert.Empty(plan.RequiredPlatforms);
        Assert.NotNull(plan.AcceptanceCriteria);
        Assert.NotNull(plan.Risks);
        Assert.NotNull(plan.Scenarios);
        Assert.NotNull(plan.Approvals);
        Assert.NotNull(plan.Assumptions);
        Assert.NotNull(plan.Preconditions);
        Assert.NotNull(plan.IndependentBusinessOracles);
    }

    [Fact]
    public void MauiTestPlan_ExplicitNullAssignedToAList_IsCoercedToEmpty()
    {
        // Object initializers are the other way user code reaches these fields.
        var plan = new MauiTestPlan { RequiredPlatforms = null! };

        Assert.NotNull(plan.RequiredPlatforms);
        Assert.Empty(plan.RequiredPlatforms);
    }

    private static void AssertExtensionRoundTrips(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty(name, out _), $"Missing extension field '{name}'.");
    }
}
