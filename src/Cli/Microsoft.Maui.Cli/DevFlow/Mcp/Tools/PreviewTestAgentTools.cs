using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Testing;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class PreviewTestAgentTools
{
    [McpServerTool(Name = "maui_test_validate"), Description(
        "Validate supplied schema-1 flow JSON offline. No app access, file writes, approvals, runs or repair authority. " +
        "Only the initial inert-draft contract is supported; the supplied target is not observed.")]
    public static string Validate(
        McpAgentSession session,
        [Description("Complete schema-1 executable flow JSON, not a path; at most 1 MiB.")] string flowJson,
        [Description("Exact intended agent ID; never inferred.")] string agentId,
        [Description("Exact intended process instance ID; never inferred.")] string agentInstanceId,
        [Description("Intended application build fingerprint.")] string appBuild,
        [Description("Intended seed/state fingerprint.")] string seed,
        [Description("Intended route/window checkpoint fingerprint.")] string checkpoint,
        [Description("Explicit declared side-effect policy; only 'none' is supported by this inert increment.")] string sideEffectPolicy,
        [Description("Stable inert plan ID.")] string planId,
        [Description("Positive plan revision.")] int revision)
    {
        return CreateDraft(flowJson, agentId, agentInstanceId, appBuild, seed, checkpoint, sideEffectPolicy, planId, revision);
    }

    [McpServerTool(Name = "maui_test_author"), Description(
        "Prepare an inert draft from complete flow JSON. Only operation 'begin' is supported. " +
        "Does not persist a session, commit, request approval, run, repair, or write source.")]
    public static string Author(
        McpAgentSession session,
        [Description("Only 'begin'; all approval, commit and mutation operations are unavailable.")] string operation,
        [Description("Complete schema-1 executable flow JSON, not a path; at most 1 MiB.")] string flowJson,
        [Description("Exact intended agent ID.")] string agentId,
        [Description("Exact intended process instance ID.")] string agentInstanceId,
        [Description("Intended application build fingerprint.")] string appBuild,
        [Description("Intended seed/state fingerprint.")] string seed,
        [Description("Intended route/window checkpoint fingerprint.")] string checkpoint,
        [Description("Explicit declared side-effect policy; only 'none' is supported by this inert increment.")] string sideEffectPolicy,
        [Description("Stable inert plan ID.")] string planId,
        [Description("Positive plan revision.")] int revision)
    {
        if (operation != "begin")
            throw new McpException("capability-unavailable: only inert begin is supported; no approval or mutation host is installed.");
        return CreateDraft(flowJson, agentId, agentInstanceId, appBuild, seed, checkpoint, sideEffectPolicy, planId, revision);
    }

    private static string CreateDraft(string flowJson, string agentId, string agentInstanceId,
        string appBuild, string seed, string checkpoint, string sideEffectPolicy, string planId, int revision)
    {
        if (string.IsNullOrWhiteSpace(flowJson) || flowJson.Length > 1024 * 1024)
            throw new McpException("invalid-request: a bounded flow JSON document is required.");
        try
        {
            using var document = JsonDocument.Parse(flowJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schema", out var schema) ||
                !schema.TryGetInt32(out var version) || version != 1)
                throw new JsonException();
            RejectDuplicateProperties(document.RootElement);
            var flow = JsonSerializer.Deserialize(flowJson, PreviewTestingJsonContext.Default.MauiFlow)
                ?? throw new JsonException();
            var plan = PreviewFlowPlan.Create(planId, revision,
                new(agentId, agentInstanceId, appBuild, seed, checkpoint, sideEffectPolicy), flow);
            return JsonSerializer.Serialize(new PreviewDraftResult(
                "inert-draft", plan.Revision, plan.FlowDigest, plan.PlanDigest),
                PreviewDraftJsonContext.Default.PreviewDraftResult);
        }
        catch (JsonException)
        {
            throw new McpException("invalid-request: malformed, duplicate, or unsupported flow JSON.");
        }
        catch (ArgumentException ex)
        {
            throw new McpException("invalid-request: " + ex.Message);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateProperties(item);
        }
    }
}

internal sealed record PreviewDraftResult(string State, int Revision, string FlowDigest, string PlanDigest)
{
    public bool NativeApproval => false;
    public bool TargetObserved => false;
    public bool SessionPersisted => false;
    public bool ExecutionAvailable => false;
    public string Qualification => "not-qualified";
}

[JsonSerializable(typeof(PreviewDraftResult))]
internal sealed partial class PreviewDraftJsonContext : JsonSerializerContext;
