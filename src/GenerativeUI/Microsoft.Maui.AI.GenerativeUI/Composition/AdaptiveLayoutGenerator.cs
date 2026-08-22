using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// Generates the strict flat layout document from the complete model-visible surface context.
/// </summary>
public sealed class AdaptiveLayoutGenerator(IChatClient chatClient) : IAdaptiveLayoutGenerator
{
    private const string SystemPrompt =
        """
        You arrange app-authored native components inside fixed application regions.
        Return only the requested ComponentLayoutDocument JSON schema.
        You may use only Stack, Grid, Tabs, Section, and Component node kinds.
        Never author XAML, source code, styles, colors, primitive leaves, labels, buttons, images, or fields.
        Component nodes are leaves and may reference only aliases, data paths, variants, and regions
        explicitly marked available in the supplied catalog and data manifest.
        Preserve the required layoutId and revision exactly. Use stable node IDs across follow-ups.
        On follow-ups, change only structure needed for the current intent and preserve semantic identity.
        Keep every reason concise and specific.
        """;

    public async Task<AdaptiveLayoutGenerationResult> GenerateAsync(
        AdaptiveSurfaceCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var response = await chatClient.GetResponseAsync<ComponentLayoutDocument>(
            [
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, BuildUserPrompt(request)),
            ],
            ComponentLayoutJsonContext.Default.Options,
            new ChatOptions { MaxOutputTokens = 4000 },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return new(
            response.TryGetResult(out var layout) ? layout : null,
            stopwatch.Elapsed,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount);
    }

    private static string BuildUserPrompt(AdaptiveSurfaceCompositionRequest request)
    {
        var prompt = new JsonObject
        {
            ["requiredLayoutId"] = request.ExpectedLayoutId,
            ["requiredRevision"] = request.ExpectedRevision,
            ["context"] = JsonSerializer.SerializeToNode(
                request.Context,
                ComponentLayoutJsonContext.Default.AdaptiveSurfaceContext),
            ["standardLayout"] = SerializeLayout(request.StandardLayout),
            ["currentLayout"] = SerializeLayout(request.CurrentLayout),
            ["invalidLayout"] = SerializeLayout(request.InvalidLayout),
        };

        if (request.CorrectionErrors is not null)
            prompt["correction"] = JsonNode.Parse(request.CorrectionErrors);

        return prompt.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode? SerializeLayout(ComponentLayoutDocument? layout)
        => layout is null
            ? null
            : JsonSerializer.SerializeToNode(
                layout,
                ComponentLayoutJsonContext.Default.ComponentLayoutDocument);
}
