using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>General-purpose schema used to compare typed and JSON-schema chat responses.</summary>
public sealed class PlaygroundResponse
{
    /// <summary>Gets or sets a concise response summary.</summary>
    [Description("A brief, accurate answer to the user's request. Do not invent details not supported by the prompt.")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Gets or sets the key points supporting the summary.</summary>
    [Description("Specific points supporting the summary. Use an empty list when there are no distinct supporting points.")]
    public List<string> KeyPoints { get; set; } = [];

    /// <summary>Gets or sets a broad category for the response.</summary>
    [Description("A short, broad category for the response, such as General, Technical, or Creative.")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Gets or sets the sentiment or tone of the response.</summary>
    [Description("Classify the overall tone. Use Neutral for objective or factual replies.")]
    public PlaygroundSentiment Sentiment { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PlaygroundSentiment>))]
public enum PlaygroundSentiment
{
    Neutral,
    Positive,
    Negative,
    Mixed,
}

/// <summary>Provides trim-safe JSON metadata for the playground's structured response.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(PlaygroundResponse))]
internal partial class PlaygroundJsonContext : JsonSerializerContext;
