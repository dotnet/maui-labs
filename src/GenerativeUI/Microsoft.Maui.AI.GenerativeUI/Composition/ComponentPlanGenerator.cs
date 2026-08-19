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
    Task<CompositionPlan?> GenerateAsync(
        CompositionPlanGenerationRequest request,
        CancellationToken cancellationToken = default);
}

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

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<CompositionPlan?> GenerateAsync(
        CompositionPlanGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await chatClient.GetResponseAsync<CompositionPlan>(
            [
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, BuildUserPrompt(request)),
            ],
            new ChatOptions { MaxOutputTokens = 2000 },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.TryGetResult(out var plan) ? plan : null;
    }

    private static string BuildUserPrompt(CompositionPlanGenerationRequest request)
    {
        var candidates = request.Candidates.Select(candidate => new
        {
            candidate.Descriptor.Alias,
            candidate.Descriptor.Description,
            candidate.Descriptor.DataContract,
            candidate.Descriptor.RequiredBindings,
            candidate.Descriptor.OptionalBindings,
            candidate.Descriptor.AllowedSlots,
            candidate.Descriptor.Variants,
            candidate.DataPath,
        });

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
            ["candidates"] = JsonSerializer.SerializeToNode(candidates, s_jsonOptions),
            ["currentPlan"] = JsonSerializer.SerializeToNode(request.CurrentPlan, s_jsonOptions),
        };

        if (request.InvalidPlan is not null)
            prompt["invalidPlan"] = JsonSerializer.SerializeToNode(request.InvalidPlan, s_jsonOptions);
        if (request.CorrectionErrors is not null)
            prompt["correction"] = JsonNode.Parse(request.CorrectionErrors);

        return prompt.ToJsonString(s_jsonOptions);
    }
}
