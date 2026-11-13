using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>Serializes <paramref name="value"/> as JSON with web defaults (camelCase).</summary>
    public static DevFlowResponse Json(object? value, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        Body = DevFlowJson.Serialize(value)
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
        => Json(new SuccessBody(true, message, null, null));

    /// <summary>The DevFlow error envelope: <c>{"success":false,"error":…,"reason":…}</c>.</summary>
    public static DevFlowResponse Error(string message, int statusCode = 400, string? reason = null)
        => Json(new SuccessBody(false, null, message, reason), statusCode);

    /// <summary>A 404 error envelope.</summary>
    public static DevFlowResponse NotFound(string message = "Not found")
        => Error(message, 404);

    sealed record SuccessBody(
        bool Success,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);
}

/// <summary>JSON settings plugins share with the agent: web defaults and string enums.</summary>
public static class DevFlowJson
{
    /// <summary>camelCase, case-insensitive reads, enums as strings.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Serializes a value with <see cref="Options"/>.</summary>
    public static string Serialize(object? value)
        => value is null ? "null" : JsonSerializer.Serialize(value, value.GetType(), Options);

    /// <summary>Serializes a value with <see cref="Options"/> to a detached element.</summary>
    public static JsonElement SerializeToElement(object? value)
        => value is null
            ? JsonSerializer.SerializeToElement<object?>(null, Options)
            : JsonSerializer.SerializeToElement(value, value.GetType(), Options);

    static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
