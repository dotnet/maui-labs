using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>General-purpose schema used to compare typed and JSON-schema chat responses.</summary>
public sealed class PlaygroundResponse
{
    /// <summary>Gets or sets a concise response summary.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Gets or sets the key points supporting the summary.</summary>
    public List<string> KeyPoints { get; set; } = [];

    /// <summary>Gets or sets a broad category for the response.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Gets or sets the sentiment or tone of the response.</summary>
    public string Sentiment { get; set; } = string.Empty;
}

/// <summary>Provides trim-safe JSON metadata for the playground's structured response.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(PlaygroundResponse))]
internal partial class PlaygroundJsonContext : JsonSerializerContext;
