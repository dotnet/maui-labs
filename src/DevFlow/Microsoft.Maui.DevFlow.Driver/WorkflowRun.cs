using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Broker-issued context automatically attached to mutating requests made within a workflow run.
/// The context is process-instance-bound and does not provide recovery across a new app process.
/// </summary>
public sealed class WorkflowRunContext
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("agentInstanceId")]
    public string AgentInstanceId { get; set; } = "";

    [JsonPropertyName("authorityEpoch")]
    public long AuthorityEpoch { get; set; }

    /// <summary>
    /// Reserved for a future approval-grant protocol. The current agent binds it when supplied
    /// but does not interpret the digest as authorization on its own.
    /// </summary>
    [JsonPropertyName("approvalDigest")]
    public string? ApprovalDigest { get; set; }

    internal void Validate()
    {
        if (!WorkflowCommandDigest.IsSafeIdentifier(RunId, 128) ||
            !WorkflowCommandDigest.IsSafeIdentifier(AgentInstanceId, 128) ||
            AuthorityEpoch <= 0 ||
            (!string.IsNullOrWhiteSpace(ApprovalDigest) &&
             !WorkflowCommandDigest.IsSafeIdentifier(ApprovalDigest, 128)))
        {
            throw new ArgumentException(
                "A workflow context requires a valid run ID, agent instance ID, positive authority epoch, and optional approval digest.");
        }
    }

    internal WorkflowRunContext Clone() => new()
    {
        RunId = RunId,
        AgentInstanceId = AgentInstanceId,
        AuthorityEpoch = AuthorityEpoch,
        ApprovalDigest = string.IsNullOrWhiteSpace(ApprovalDigest) ? null : ApprovalDigest.Trim()
    };
}

/// <summary>Result of beginning, ending, or abandoning an agent workflow ledger.</summary>
public sealed class WorkflowRunControlStatus
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("alreadyTerminal")]
    public bool AlreadyTerminal { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("commands")]
    public int Commands { get; set; }

    [JsonPropertyName("authorityEpoch")]
    public long? AuthorityEpoch { get; set; }

    [JsonPropertyName("agentInstanceId")]
    public string? AgentInstanceId { get; set; }
}

/// <summary>Metadata for one fenced workflow command. No request or response body is retained.</summary>
public sealed class WorkflowCommandReceipt
{
    [JsonPropertyName("runId")]
    public string RunId { get; init; } = "";

    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("commandId")]
    public string CommandId { get; init; } = "";

    [JsonPropertyName("actionDigest")]
    public string ActionDigest { get; init; } = "";

    [JsonPropertyName("authorityEpoch")]
    public long AuthorityEpoch { get; init; }

    [JsonPropertyName("acknowledgementState")]
    public string AcknowledgementState { get; internal set; } = "prepared";

    [JsonPropertyName("httpStatusCode")]
    public int? HttpStatusCode { get; internal set; }
}

/// <summary>
/// Raised when the agent explicitly rejects a fenced workflow command. Callers must not retry
/// this exception blindly because the agent may have recorded unknown completion.
/// </summary>
public sealed class WorkflowCommandException : InvalidOperationException
{
    public WorkflowCommandException(
        string reason,
        string? message = null,
        WorkflowCommandReceipt? receipt = null,
        Exception? innerException = null)
        : base(message ?? $"Workflow command failed: {reason}.", innerException)
    {
        Reason = reason;
        Receipt = receipt;
    }

    public string Reason { get; }
    public WorkflowCommandReceipt? Receipt { get; }
    public bool IsUnknownCompletion =>
        string.Equals(Reason, "workflow-unknown-completion", StringComparison.Ordinal);
}

/// <summary>Canonical action-digest helpers shared by workflow-run callers and test hosts.</summary>
public static class WorkflowCommandDigest
{
    public static string Compute(string method, string canonicalPath, string? body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);

        var material = string.Concat(
            method.Trim().ToUpperInvariant(),
            "\n",
            canonicalPath,
            "\n",
            CanonicalizeBody(body));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    internal static async Task<string> ComputeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        return Compute(
            request.Method.Method,
            CanonicalPath(request.RequestUri),
            body);
    }

    internal static string CreateCommandId(string runId, long sequence, string actionDigest)
    {
        var material = $"{runId}\n{sequence}\n{actionDigest}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return $"cmd_{hash[..32]}";
    }

    internal static bool IsSafeIdentifier(string? value, int maximumLength)
        => value is { Length: > 0 } &&
           value.Length <= maximumLength &&
           value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static string CanonicalPath(Uri? uri)
    {
        if (uri is null)
            return "/";

        if (string.IsNullOrWhiteSpace(uri.Query))
            return uri.AbsolutePath;

        var query = uri.Query[1..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(static segment =>
            {
                var split = segment.Split('=', 2);
                var key = Uri.UnescapeDataString(split[0]);
                var value = split.Length == 2 ? Uri.UnescapeDataString(split[1]) : string.Empty;
                return new KeyValuePair<string, string>(key, value);
            })
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Value, StringComparer.Ordinal)
            .Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        return $"{uri.AbsolutePath}?{string.Join("&", query)}";
    }

    private static string CanonicalizeBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            WriteCanonicalJson(writer, document.RootElement);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
