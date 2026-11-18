using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>The result of a plugin route or tool.</summary>
public sealed class DevFlowResponse
{
    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>Content type of <see cref="Body"/> or <see cref="BodyBytes"/>.</summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>Text body.</summary>
    public string? Body { get; init; }

    /// <summary>Binary body, used instead of <see cref="Body"/> when set.</summary>
    public byte[]? BodyBytes { get; init; }

    /// <summary>Additional transport headers, e.g. capture identity or retry information.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Serializes <paramref name="value"/> as JSON with web defaults (camelCase).</summary>
    public static DevFlowResponse Json(object? value, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        Body = DevFlowJson.Serialize(value)
    };

    /// <summary>Serializes a payload using explicit source-generated metadata.</summary>
    public static DevFlowResponse Json<T>(T value, JsonTypeInfo<T> typeInfo, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        Body = JsonSerializer.Serialize(value, typeInfo)
    };

    /// <summary>A plain text response.</summary>
    public static DevFlowResponse Text(string text, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/plain; charset=utf-8",
        Body = text
    };

    /// <summary>A binary response, e.g. an image.</summary>
    public static DevFlowResponse Bytes(byte[] data, string contentType) => new()
    {
        ContentType = contentType,
        BodyBytes = data
    };

    /// <summary><c>{"success":true}</c> with an optional message.</summary>
    public static DevFlowResponse Ok(string? message = null)
        => Json(message is null
            ? new Dictionary<string, object?> { ["success"] = true }
            : new Dictionary<string, object?> { ["success"] = true, ["message"] = message });

    /// <summary>The DevFlow error envelope: <c>{"success":false,"error":…,"reason":…}</c>.</summary>
    public static DevFlowResponse Error(string message, int statusCode = 400, string? reason = null)
        => Json(reason is null
            ? new Dictionary<string, object?> { ["success"] = false, ["error"] = message }
            : new Dictionary<string, object?> { ["success"] = false, ["error"] = message, ["reason"] = reason }, statusCode);

    /// <summary>A 404 error envelope.</summary>
    public static DevFlowResponse NotFound(string message = "Not found")
        => Error(message, 404);

}
