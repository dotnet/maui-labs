using System.Text.Json;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// A <c>get_connection_status</c> call as a typed block. The <c>[ToolBlock]</c> source generator writes the handler that
/// creates it for the tool's calls and maps the result into it; <c>options.AddGeneratedToolBlocks()</c> registers it.
/// </summary>
/// <remarks>
/// The tool returns a <see cref="ConnectionStatusResult"/>, which reaches the block as JSON. The generated handler reads
/// <c>networkAccess</c> into <see cref="NetworkAccess"/> without reflection; the connection types, a list, are read from
/// the same JSON. The engine raises <see cref="ContentBlock.PropertyChanged"/> for every property of a custom block
/// when its handler maps the result, so bindings to the properties below refresh too.
/// </remarks>
[ToolBlock("get_connection_status")]
public sealed partial class ConnectionStatusBlock : FunctionInvocationContentBlock
{
    /// <summary>Gets the network access the device reported, such as <c>Internet</c> or <c>None</c>.</summary>
    [ToolResult(Name = "networkAccess")]
    public string? NetworkAccess { get; set; }

    /// <summary>Gets whether the device can reach the internet.</summary>
    public bool IsOnline => NetworkAccess is "Internet" or "ConstrainedInternet";

    /// <summary>Gets what the card says about the connection, such as <c>Online</c>.</summary>
    public string Summary => NetworkAccess switch
    {
        "Internet" => "Online",
        "ConstrainedInternet" => "Online, with limited access",
        "Local" => "Local network only",
        "None" => "Offline",
        null => string.Empty,
        var other => other,
    };

    /// <summary>Gets the active connection types, such as <c>WiFi, Cellular</c>.</summary>
    public string ConnectionTypes => Result?.Result switch
    {
        JsonElement { ValueKind: JsonValueKind.Object } result when
            result.TryGetProperty("connectionProfiles", out var profiles) &&
            profiles.ValueKind == JsonValueKind.Array =>
            string.Join(", ", profiles.EnumerateArray()
                .Where(static profile => profile.ValueKind == JsonValueKind.String)
                .Select(static profile => profile.GetString())),
        _ => string.Empty,
    };
}
