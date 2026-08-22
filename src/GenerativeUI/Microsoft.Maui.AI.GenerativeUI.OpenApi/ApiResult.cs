using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// Whether an invocation is a safe read (GET) or a mutating write (POST/PUT/PATCH/DELETE). The
/// invoker enforces that the resolved operation's HTTP method matches, so a read can never perform a
/// write and vice versa.
/// </summary>
public enum ApiAccess
{
    /// <summary>A safe, non-mutating GET operation.</summary>
    Read,

    /// <summary>A mutating POST/PUT/PATCH/DELETE operation.</summary>
    Write,
}

/// <summary>Structured error for a failed API invocation (RFC 7807 <c>ProblemDetails</c> when available).</summary>
public sealed record ApiError
{
    /// <summary>HTTP status code, or 0 when the request never left the client (validation/gating).</summary>
    public required int Status { get; init; }

    /// <summary>Short, human-readable summary of the problem.</summary>
    public string? Title { get; init; }

    /// <summary>Longer explanation of the problem, when available.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// The normalized outcome of an API invocation: a capped JSON body on success, or a structured
/// <see cref="ApiError"/> on failure. <see cref="ToResponseJson"/> renders the compact envelope the
/// AI model sees.
/// </summary>
public sealed record ApiResult
{
    /// <summary>True for a 2xx response.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>HTTP status code, or 0 when the request never left the client.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Response body (JSON, possibly truncated) on success; null for empty/no-content responses.</summary>
    public string? Body { get; init; }

    /// <summary>True when <see cref="Body"/> was capped to the response-size limit.</summary>
    public bool Truncated { get; init; }

    /// <summary>Structured error on failure; null on success.</summary>
    public ApiError? Error { get; init; }

    internal static ApiResult Ok(int status, string? body, bool truncated) =>
        new() { IsSuccess = true, StatusCode = status, Body = body, Truncated = truncated };

    internal static ApiResult Fail(int status, string title, string? detail) =>
        new() { IsSuccess = false, StatusCode = status, Error = new ApiError { Status = status, Title = title, Detail = detail } };

    /// <summary>
    /// Renders the compact JSON envelope handed back to the model:
    /// <c>{"status":200,"data":…}</c> on success (with <c>"truncated":true</c> when capped), or
    /// <c>{"status":404,"error":{"title":…,"detail":…}}</c> on failure.
    /// </summary>
    public string ToResponseJson()
    {
        var envelope = new JsonObject { ["status"] = StatusCode };

        if (IsSuccess)
        {
            if (Body is not null)
            {
                if (!Truncated && TryParse(Body, out var node))
                {
                    envelope["data"] = node;
                }
                else
                {
                    envelope["data"] = Body;
                    if (Truncated)
                        envelope["truncated"] = true;
                }
            }
        }
        else
        {
            envelope["error"] = new JsonObject
            {
                ["title"] = Error?.Title,
                ["detail"] = Error?.Detail,
            };
        }

        return envelope.ToJsonString(RelaxedJson);
    }

    // Model-facing JSON: don't HTML-escape apostrophes/quotes/non-ASCII — the result is fed to a
    // model, not embedded in HTML, so relaxed escaping is safe and produces cleaner, cheaper output.
    private static readonly JsonSerializerOptions RelaxedJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static bool TryParse(string json, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }
}
