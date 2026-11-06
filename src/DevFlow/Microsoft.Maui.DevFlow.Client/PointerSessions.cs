using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// One coordinate sample in a DevFlow pointer session. Coordinates use the session's declared
/// coordinate space; v1 sessions use window-logical device-independent units.
/// </summary>
public sealed class PointerSessionSample
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>Monotonic milliseconds since the pointer session started.</summary>
    [JsonPropertyName("elapsedMs")]
    public double ElapsedMs { get; set; }
}

/// <summary>Starts one continuous pointer session.</summary>
public sealed class PointerSessionStartRequest
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("pointerId")]
    public long PointerId { get; set; }

    [JsonPropertyName("pointerType")]
    public string PointerType { get; set; } = "touch";

    [JsonPropertyName("coordinateSpace")]
    public string CoordinateSpace { get; set; } = "windowLogical";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("elapsedMs")]
    public double ElapsedMs { get; set; }

    [JsonPropertyName("windowId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WindowId { get; set; }

    [JsonPropertyName("captureEpoch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CaptureEpoch { get; set; }

    [JsonPropertyName("registryGeneration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RegistryGeneration { get; set; }

}

/// <summary>Reports the applied or terminal state of a pointer session operation.</summary>
public sealed class PointerSessionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("lastSequence")]
    public int LastSequence { get; set; }

    [JsonPropertyName("idleTimeoutMs")]
    public int IdleTimeoutMs { get; set; }

    [JsonPropertyName("maxDurationMs")]
    public int MaxDurationMs { get; set; }

    [JsonPropertyName("maxSamplesPerMove")]
    public int MaxSamplesPerMove { get; set; }

    [JsonPropertyName("maxRequestBytes")]
    public int MaxRequestBytes { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonIgnore]
    public int? StatusCode { get; internal set; }

    [JsonIgnore]
    public bool TransportFailure { get; internal set; }

    [JsonIgnore]
    public bool Unsupported
        => StatusCode is 404 or 501
            || string.Equals(Reason, "not_supported", StringComparison.Ordinal);
}
