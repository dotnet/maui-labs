using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed record CommandDescription(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("mutating")] bool Mutating);
