using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

public sealed record CompositionPlanGenerationRequest(
    string Intent,
    string Scaffold,
    string DataContract,
    string DataPath,
    string Title,
    string ExpectedPlanId,
    int ExpectedRevision,
    JsonNode? Data,
    IReadOnlyList<ResolvedComponentCandidate> Candidates,
    CompositionPlan? CurrentPlan = null,
    CompositionPlan? InvalidPlan = null,
    string? CorrectionErrors = null);

public interface IComponentPlanGenerator
{
    Task<ComponentPlanGenerationResult> GenerateAsync(
        CompositionPlanGenerationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ComponentPlanGenerationResult(
    CompositionPlan? Plan,
    TimeSpan Latency,
    long? InputTokens,
    long? OutputTokens);

/// <summary>Generates a tiny typed plan from the already-filtered native component catalog.</summary>
public sealed class ComponentPlanGenerator(IChatClient chatClient) : IComponentPlanGenerator
{
    private const string SystemPrompt =
        """
        You compose trusted native app components; you do not author primitive UI.
        Return only the requested CompositionPlan JSON schema.
        Use only supplied component aliases, slots, variants, and dataPath values.
        Use each component at most once.
        Preserve the supplied planId and revision exactly.
        On follow-ups, preserve unchanged section ids and change only the slots, order, priorities,
        or variants needed to answer the new intent.
        Higher priority sections render before lower priority sections within a slot.
        Keep reasons concise and specific to the user's intent.
        """;

    public async Task<ComponentPlanGenerationResult> GenerateAsync(
        CompositionPlanGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var response = await chatClient.GetResponseAsync<CompositionPlan>(
            [
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, BuildUserPrompt(request)),
            ],
            CompositionJsonContext.Default.Options,
            new ChatOptions { MaxOutputTokens = 2000 },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return new(
            response.TryGetResult(out var plan) ? plan : null,
            stopwatch.Elapsed,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount);
    }

    private static string BuildUserPrompt(CompositionPlanGenerationRequest request)
    {
        var candidates = new JsonArray();
        foreach (var candidate in request.Candidates)
        {
            candidates.Add(new JsonObject
            {
                ["alias"] = candidate.Descriptor.Alias,
                ["description"] = candidate.Descriptor.Description,
                ["dataContract"] = candidate.Descriptor.DataContract,
                ["requiredBindings"] = ToArray(candidate.Descriptor.RequiredBindings),
                ["optionalBindings"] = ToArray(candidate.Descriptor.OptionalBindings),
                ["allowedSlots"] = ToArray(candidate.Descriptor.AllowedSlots.Select(slot => slot.ToString())),
                ["variants"] = ToArray(candidate.Descriptor.Variants),
                ["dataPath"] = candidate.DataPath,
            });
        }

        var prompt = new JsonObject
        {
            ["intent"] = request.Intent,
            ["scaffold"] = request.Scaffold,
            ["dataContract"] = request.DataContract,
            ["dataPath"] = request.DataPath,
            ["title"] = request.Title,
            ["requiredPlanId"] = request.ExpectedPlanId,
            ["requiredRevision"] = request.ExpectedRevision,
            ["data"] = request.Data?.DeepClone(),
            ["candidates"] = candidates,
            ["currentPlan"] = SerializePlan(request.CurrentPlan),
        };

        if (request.InvalidPlan is not null)
            prompt["invalidPlan"] = SerializePlan(request.InvalidPlan);
        if (request.CorrectionErrors is not null)
            prompt["correction"] = JsonNode.Parse(request.CorrectionErrors);

        return prompt.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode? SerializePlan(CompositionPlan? plan)
        => plan is null
            ? null
            : JsonSerializer.SerializeToNode(plan, CompositionJsonContext.Default.CompositionPlan);

    private static JsonArray ToArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }
}
