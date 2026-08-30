using System.Text.Json;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>
/// Tool-facing request shapes that carry a plan, flow, or repair proposal as raw JSON.
/// </summary>
/// <remarks>
/// <para>
/// The broker contracts are deeply nested, and a tool that binds them directly publishes the whole
/// expansion in its input schema: the plan and flow alone accounted for roughly twenty thousand
/// characters of <c>maui_test_author</c>, and the proposal for most of <c>maui_test_patch</c>.
/// Hosts virtualise tools once the advertised surface grows past a threshold, after which an agent
/// has to discover a tool before it can call one, so the schema size is not merely cosmetic.
/// </para>
/// <para>
/// Nothing is loosened by this. The values are deserialized here into exactly the same contracts
/// the broker already validated, malformed JSON is refused with the field that failed, and every
/// admission, scope, and provenance rule is unchanged. What is lost is inline schema guidance for
/// three members, which the skill's authoring reference documents in a form an agent can actually
/// read.
/// </para>
/// </remarks>
internal static class McpCompactRequests
{
    /// <summary>Deserializes a raw plan, returning the member name when it cannot be read.</summary>
    internal static bool TryReadPlan(JsonElement? source, out MauiTestPlan? plan, out string? error)
        => TryRead(source, "plan", MauiTestingJsonContext.Default.MauiTestPlan, out plan, out error);

    /// <summary>Deserializes a raw flow, returning the member name when it cannot be read.</summary>
    internal static bool TryReadFlow(JsonElement? source, out MauiFlow? flow, out string? error)
        => TryRead(source, "flow", MauiFlowJsonContext.Default.MauiFlow, out flow, out error);

    /// <summary>Deserializes a raw repair proposal, returning the member name when it cannot be read.</summary>
    internal static bool TryReadProposal(JsonElement? source, out MauiFlowRepairProposal? proposal, out string? error)
        => TryRead(source, "proposal", MauiTestingJsonContext.Default.MauiFlowRepairProposal, out proposal, out error);

    private static bool TryRead<T>(
        JsonElement? source,
        string member,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        out T? value,
        out string? error)
        where T : class
    {
        value = null;
        error = null;
        if (source is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"The '{member}' member must be a JSON object.";
            return false;
        }

        try
        {
            value = element.Deserialize(typeInfo);
            return true;
        }
        catch (JsonException ex)
        {
            // Name the member and the reason. A caller told only "invalid request" has to guess
            // which of several nested structures it got wrong.
            error = $"The '{member}' member could not be read: {ex.Message}";
            return false;
        }
    }
}

/// <summary>Authoring request whose plan and flow stay raw JSON until the tool reads them.</summary>
public sealed class McpTestAgentAuthorRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("envelope")]
    public MauiTestAgentRequestEnvelope? Envelope { get; set; }

    [System.ComponentModel.Description(
        "Inert test plan object. Required keys: schema (1), planId, revision, title, goal, " +
        "scenarios[{scenarioId,description,acceptanceCriterionIds[]}], " +
        "acceptanceCriteria[{criterionId,description,required,businessOracleId}], requiredPlatforms[], " +
        "checkpoint{route,window}, sideEffectPolicy. Optional: flow{path}, reset{required,strategy," +
        "resetIdentity,seedFingerprint}, independentBusinessOracles[{oracleId,description,required," +
        "independent,evidenceKind,reference,expect{contains[],absent[]}}], explorationBudget. " +
        "See the maui-devflow-test authoring reference for the full shape.")]
    [System.Text.Json.Serialization.JsonPropertyName("plan")]
    public JsonElement? Plan { get; set; }

    [System.ComponentModel.Description(
        "Inert flow object. Required keys: schema (2), name, app, platform, " +
        "steps[{seq,action,args{selector{automationId},text},label,intent,acceptanceCriterionIds[]," +
        "asserts[{kind,selector{automationId},name,expected,verify}]}]. Actions are tap, fill, " +
        "scroll, navigate, and back. See the maui-devflow-test authoring reference for the full shape.")]
    [System.Text.Json.Serialization.JsonPropertyName("flow")]
    public JsonElement? Flow { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("targetState")]
    public MauiTestAgentTargetState? TargetState { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("durationSeconds")]
    public int? DurationSeconds { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("explorationScope")]
    public MauiTestAgentMutationScope? ExplorationScope { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("approvalKind")]
    public string? ApprovalKind { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("approvalScope")]
    public MauiTestAgentMutationScope? ApprovalScope { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("approvalExpiresAt")]
    public DateTimeOffset? ApprovalExpiresAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("approvalRequestId")]
    public string? ApprovalRequestId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("waitTimeoutSeconds")]
    public int? WaitTimeoutSeconds { get; set; }

    /// <summary>Reads the raw members into the contract the broker validates.</summary>
    internal bool TryToTyped(out MauiTestAgentAuthorRequest? typed, out string? error)
    {
        typed = null;
        if (!McpCompactRequests.TryReadPlan(Plan, out var plan, out error) ||
            !McpCompactRequests.TryReadFlow(Flow, out var flow, out error))
        {
            return false;
        }

        typed = new MauiTestAgentAuthorRequest
        {
            Envelope = Envelope,
            Plan = plan,
            Flow = flow,
            TargetState = TargetState,
            DurationSeconds = DurationSeconds,
            ExplorationScope = ExplorationScope,
            ApprovalKind = ApprovalKind,
            ApprovalScope = ApprovalScope,
            ApprovalExpiresAt = ApprovalExpiresAt,
            ApprovalRequestId = ApprovalRequestId,
            WaitTimeoutSeconds = WaitTimeoutSeconds,
        };
        return true;
    }
}

/// <summary>Patch request whose proposal stays raw JSON until the tool reads it.</summary>
public sealed class McpTestAgentPatchRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("envelope")]
    public MauiTestAgentRequestEnvelope? Envelope { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("operation")]
    public string? Operation { get; set; }

    [System.ComponentModel.Description(
        "Inert selector-only repair proposal. For preview, send only sourceStepId and " +
        "proposedSelector{automationId} to be told the canonical patchDigest. For proposal, send " +
        "sourceRunId, sourceStepId, sourceFailureCode ('locator-not-found'), preDispatch (true), " +
        "baseFlow{path,digest,revision}, oldSelector{automationId}, proposedSelector{automationId}, " +
        "patch, patchDigest copied verbatim from preview, unchangedAssertionsProof{unchanged," +
        "actionsUnchanged,valuesUnchanged,orderUnchanged}, and provenance matching the session actor.")]
    [System.Text.Json.Serialization.JsonPropertyName("proposal")]
    public JsonElement? Proposal { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("proposalId")]
    public string? ProposalId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>Reads the raw proposal into the contract the broker validates.</summary>
    internal bool TryToTyped(out MauiTestAgentPatchRequest? typed, out string? error)
    {
        typed = null;
        if (!McpCompactRequests.TryReadProposal(Proposal, out var proposal, out error))
            return false;

        typed = new MauiTestAgentPatchRequest
        {
            Envelope = Envelope,
            Operation = Operation,
            Proposal = proposal,
            ProposalId = ProposalId,
            Reason = Reason,
        };
        return true;
    }
}
