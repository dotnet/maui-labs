using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Flows;

namespace Microsoft.Maui.Cli.DevFlow.Testing;

internal sealed record PreviewFlowBinding(
    string AgentId, string AgentInstanceId, string AppBuild, string Seed, string Checkpoint,
    string SideEffectPolicy);

/// <summary>An inert, immutable snapshot. Creating a plan never authorizes execution.</summary>
internal sealed class PreviewFlowPlan
{
    private readonly byte[] _flow;
    public string PlanId { get; }
    public int Revision { get; }
    public string FlowDigest { get; }
    public string PlanDigest { get; }
    public PreviewFlowBinding Binding { get; }

    private PreviewFlowPlan(string planId, int revision, PreviewFlowBinding binding, byte[] flow)
    {
        PlanId = planId;
        Revision = revision;
        Binding = binding;
        _flow = flow;
        FlowDigest = Hash(flow);
        PlanDigest = Hash(JsonSerializer.SerializeToUtf8Bytes(
            new PreviewPlanIdentity(planId, revision, binding, FlowDigest),
            PreviewTestingJsonContext.Default.PreviewPlanIdentity));
    }

    public static PreviewFlowPlan Create(string planId, int revision, PreviewFlowBinding binding, MauiFlow flow)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(flow);
        if (revision < 1 || new[] { planId, binding.AgentId, binding.AgentInstanceId,
            binding.AppBuild, binding.Seed, binding.Checkpoint }.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Length > 256))
            throw new ArgumentException("A positive revision and complete exact-target binding are required.");
        if (binding.SideEffectPolicy != "none")
            throw new ArgumentException("Resettable and non-replayable flows require a later host-owned admission contract.");
        if (flow.Schema != MauiFlow.CurrentSchema || flow.Steps is not { Count: > 0 and <= 256 } ||
            flow.Steps.Any(step => step is null || step.Asserts?.Any(assertion => assertion is null) == true))
            throw new ArgumentException("Only a bounded, nonempty schema-1 flow is supported.");
        var validation = FlowValidator.Validate(flow);
        if (!validation.Ok)
            throw new ArgumentException("The flow does not satisfy the executable flow contract.");
        if (!flow.Steps.Any(step => step.Asserts?.Any(assertion => assertion.Verify) == true))
            throw new ArgumentException("A hard assertion is required; observations are not assertions.");
        foreach (var step in flow.Steps)
        {
            if (step.Action is not (FlowActions.Tap or FlowActions.Scroll or FlowActions.Navigate or
                FlowActions.Back or FlowActions.Assert))
                throw new ArgumentException("This initial preview excludes data entry and property/environment mutation.");
            CheckSelector(FlowValidator.EffectiveSelector(step));
            if (step.Args?.ItemIndex is not null || step.Args?.Element is not null)
                throw new ArgumentException("Repeated items and runtime-ID scrolling require a later scoped-item contract.");
            foreach (var assertion in step.Asserts ?? [])
                CheckSelector(assertion.Selector);
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(flow, PreviewTestingJsonContext.Default.MauiFlow);
        if (bytes.Length > 1024 * 1024)
            throw new ArgumentException("The flow exceeds the 1 MiB plan limit.");
        return new PreviewFlowPlan(planId, revision, binding, bytes);
    }

    public MauiFlow CopyFlow() =>
        JsonSerializer.Deserialize(_flow, PreviewTestingJsonContext.Default.MauiFlow)
        ?? throw new InvalidOperationException("The plan snapshot is invalid.");

    private static void CheckSelector(FlowSelector? selector)
    {
        if (selector is null)
            return;
        if (string.IsNullOrWhiteSpace(selector.AutomationId) || selector.Text is not null ||
            selector.Id is not null || selector.TypeIndex is not null || selector.Index is not null)
            throw new ArgumentException("Only a unique app-owned AutomationId is supported; no fallback selectors.");
    }

    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}

internal sealed record PreviewPlanIdentity(
    string PlanId, int Revision, PreviewFlowBinding Binding, string FlowDigest);

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(MauiFlow))]
[JsonSerializable(typeof(PreviewPlanIdentity))]
[JsonSerializable(typeof(PreviewFlowEvidence))]
internal sealed partial class PreviewTestingJsonContext : JsonSerializerContext;
