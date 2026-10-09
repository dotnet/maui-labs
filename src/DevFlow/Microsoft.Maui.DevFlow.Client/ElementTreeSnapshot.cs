using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

public sealed class ElementTreeSnapshot
{
    [JsonPropertyName("revision")]
    public string Revision { get; set; } = string.Empty;

    [JsonPropertyName("elements")]
    public List<ElementInfo> Elements { get; set; } = [];
}
