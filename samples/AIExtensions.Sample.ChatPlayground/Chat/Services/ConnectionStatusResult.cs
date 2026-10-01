using System.Text.Json.Serialization;

namespace AIExtensions.Sample.ChatPlayground;

public sealed record ConnectionStatusResult(
    [property: JsonPropertyName("networkAccess")] string NetworkAccess,
    [property: JsonPropertyName("connectionProfiles")] string[] ConnectionProfiles);
