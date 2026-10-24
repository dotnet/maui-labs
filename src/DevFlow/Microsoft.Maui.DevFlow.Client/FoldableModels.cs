using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

[JsonConverter(typeof(FoldModeJsonConverter))]
public enum FoldMode
{
    Unknown,
    Folded,
    PartiallyFolded,
    Unfolded,
}

public sealed class FoldModeJsonConverter : JsonConverter<FoldMode>
{
    public override FoldMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String && FoldModes.TryParse(reader.GetString(), out var mode)
            ? mode : throw new JsonException("Expected folded, partially-folded, unfolded, or unknown.");

    public override void Write(Utf8JsonWriter writer, FoldMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(FoldModes.Format(value));
}

public static class FoldModes
{
    public static string Format(FoldMode mode) => mode switch
    {
        FoldMode.Unknown => "unknown",
        FoldMode.Folded => "folded",
        FoldMode.PartiallyFolded => "partially-folded",
        FoldMode.Unfolded => "unfolded",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static bool TryParse(string? value, out FoldMode mode)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        mode = normalized switch
        {
            "folded" => FoldMode.Folded,
            "partially-folded" => FoldMode.PartiallyFolded,
            "unfolded" => FoldMode.Unfolded,
            _ => FoldMode.Unknown,
        };
        return mode != FoldMode.Unknown || normalized == "unknown";
    }
}

public sealed class FoldHinge
{
    [JsonPropertyName("index")]
    public int Index { get; init; }
    [JsonPropertyName("angleDegrees")]
    public double AngleDegrees { get; init; }
}

public sealed class FoldDisplay
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("uniqueId")]
    public string UniqueId { get; init; } = "";
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("active")]
    public bool Active { get; init; }
    [JsonPropertyName("primary")]
    public bool Primary { get; init; }
    [JsonPropertyName("scale")]
    public double Scale { get; init; }
    [JsonPropertyName("nativeWidth")]
    public double NativeWidth { get; init; }
    [JsonPropertyName("nativeHeight")]
    public double NativeHeight { get; init; }
    [JsonPropertyName("rotation")]
    public string? Rotation { get; init; }
}

public sealed class FoldCapabilities
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; init; } = "";
    [JsonPropertyName("platform")]
    public string Platform { get; init; } = "";
    [JsonPropertyName("backend")]
    public string Backend { get; init; } = "";
    [JsonPropertyName("experimental")]
    public bool Experimental { get; init; }
    [JsonPropertyName("canReadAngle")]
    public bool CanReadAngle { get; init; }
    [JsonPropertyName("canSetAngle")]
    public bool CanSetAngle { get; init; }
    [JsonPropertyName("canSetMode")]
    public bool CanSetMode { get; init; }
    [JsonPropertyName("hingeIndices")]
    public int[] HingeIndices { get; init; } = [];
    [JsonPropertyName("supportedModes")]
    public FoldMode[] SupportedModes { get; init; } = [];
    [JsonPropertyName("minimumAngleDegrees")]
    public double MinimumAngleDegrees { get; init; }
    [JsonPropertyName("maximumAngleDegrees")]
    public double MaximumAngleDegrees { get; init; } = 180;
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class FoldState
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; init; } = "";
    [JsonPropertyName("platform")]
    public string Platform { get; init; } = "";
    [JsonPropertyName("backend")]
    public string Backend { get; init; } = "";
    [JsonPropertyName("experimental")]
    public bool Experimental { get; init; }
    [JsonPropertyName("mode")]
    public FoldMode Mode { get; init; }
    [JsonPropertyName("hinges")]
    public FoldHinge[] Hinges { get; init; } = [];
    [JsonPropertyName("displays")]
    public FoldDisplay[] Displays { get; init; } = [];
    [JsonPropertyName("observedAtUtc")]
    public DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed class FoldSetResult
{
    [JsonPropertyName("requestedMode")]
    public FoldMode RequestedMode { get; init; }
    [JsonPropertyName("requestedAngleDegrees")]
    public double RequestedAngleDegrees { get; init; }
    [JsonPropertyName("hingeIndex")]
    public int HingeIndex { get; init; }
    [JsonPropertyName("state")]
    public FoldState State { get; init; } = new();
}
