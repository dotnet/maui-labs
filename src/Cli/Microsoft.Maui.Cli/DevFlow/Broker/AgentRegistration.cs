using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Represents a registered agent in the broker.
/// </summary>
public record AgentRegistration
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>
    /// Opaque broker-assigned generation for this connected process. It changes on every
    /// registration, including a reconnect with the same stable agent ID.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; init; } = "";

    [JsonPropertyName("project")]
    public string Project { get; init; } = "";

    [JsonPropertyName("tfm")]
    public string Tfm { get; init; } = "";

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = "";

    [JsonPropertyName("appName")]
    public string AppName { get; init; } = "";

    [JsonPropertyName("packageId")]
    public string? PackageId { get; init; }

    /// <summary>
    /// Identity of the virtual device this app reported running on, used to pair the agent with
    /// the device around it. Null for desktop apps and unrecognised hosts.
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("processId")]
    public int? ProcessId { get; init; }

    [JsonPropertyName("connectedAt")]
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Computes the agent ID from project path and TFM.
    /// </summary>
    public static string ComputeId(string project, string tfm)
    {
        var input = $"{project}|{tfm}";
        return ComputeId(input);
    }

    /// <summary>
    /// Computes a process-specific agent ID while preserving the legacy identity
    /// for agents that do not report a process ID.
    /// </summary>
    public static string ComputeId(string project, string tfm, string? sessionId, int? processId)
    {
        if (processId is not > 0)
            return ComputeId(project, tfm);

        var input = $"{project}|{tfm}|{sessionId ?? ""}|{processId.Value}";
        return ComputeId(input);
    }

    /// <summary>
    /// Computes the process instance identity that authoring sessions, approvals, grants, and
    /// checkpoints bind to, or <see langword="null"/> when the registration carries no evidence of
    /// which process it came from.
    /// </summary>
    /// <remarks>
    /// This identifies the app process, not the broker connection. Minting a fresh value per
    /// WebSocket made a broker restart, or any transient reconnect, look like a different app: every
    /// retained session and issued approval bound to the old value became stale even though the app
    /// never restarted, and the only apparent remedy was restarting the editor session. Deriving it
    /// from the app's own session id and process id keeps the property the trust model actually
    /// needs — a genuinely new process reports a new session id and process id, so it still gets a
    /// new instance identity — while a reconnect of the same process keeps its own.
    /// <para>
    /// A registration that reports no process id proves nothing about which process it is, so this
    /// returns null and the caller keeps minting an unguessable per-connection value rather than
    /// inventing continuity from absent evidence.
    /// </para>
    /// </remarks>
    public static string? ComputeInstanceId(string? project, string? tfm, string? sessionId, int? processId)
    {
        if (processId is not > 0)
            return null;

        // The domain separator keeps this from ever colliding with the agent id computed from the
        // same facts, so one can never be presented in place of the other.
        var input = $"devflow-agent-instance|{project ?? ""}|{tfm ?? ""}|{sessionId ?? ""}|{processId.Value}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant()[..32];
    }

    private static string ComputeId(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}

/// <summary>
/// The outcome of a device operation routed through the broker.
/// </summary>
/// <param name="Success">Whether the operation was performed.</param>
/// <param name="Reason">Why it was not, phrased for a human.</param>
public record DeviceControlResult(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("reason")] string? Reason);

internal sealed record DeviceMutationLeaseResult
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("allowed")] public bool Allowed { get; init; }
    [JsonPropertyName("youHold")] public bool YouHold { get; init; }
    [JsonPropertyName("heldByOther")] public bool HeldByOther { get; init; }
    [JsonPropertyName("transactionId")] public string? TransactionId { get; init; }
    [JsonPropertyName("holderKind")] public string? HolderKind { get; init; }
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("expiresInMs")] public long ExpiresInMs { get; init; }
    [JsonPropertyName("authorityEpoch")] public long AuthorityEpoch { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }

    public static DeviceMutationLeaseResult Failed(string error) =>
        new() { Error = error };
}

internal sealed record DeviceMutationLeaseRequest
{
    [JsonPropertyName("action")] public string Action { get; init; } = "";
    [JsonPropertyName("leaseId")] public string LeaseId { get; init; } = "";
    [JsonPropertyName("transactionId")] public string? TransactionId { get; init; }
    [JsonPropertyName("deviceId")] public string? DeviceId { get; init; }
    [JsonPropertyName("catalog")] public bool Catalog { get; init; }
    [JsonPropertyName("force")] public bool Force { get; init; }
}

/// <summary>
/// Broker state file written to ~/.mauidevflow/broker.json
/// </summary>
public record BrokerState
{
    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// Unguessable token that lets a LOCAL host shell (canvas, VS Code) embed the broker inspector
    /// in an iframe: requests carrying <c>?embed={token}</c> get relaxed anti-framing headers. Only
    /// local processes can read this file, so a remote clickjacking page cannot obtain the token and
    /// is still served <c>X-Frame-Options: DENY</c>.
    /// </summary>
    [JsonPropertyName("embedToken")]
    public string? EmbedToken { get; init; }

    /// <summary>
    /// Per-broker-process secret for a trusted local native host to obtain a single-use approval
    /// confirmation. This is intentionally written only to the owner-restricted local state file;
    /// it is never an Inspector, MCP, or agent protocol value.
    /// </summary>
    [JsonPropertyName("nativeApprovalToken")]
    public string? NativeApprovalToken { get; init; }
}

internal record RegistrationMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("project")]
    public string Project { get; init; } = "";

    [JsonPropertyName("tfm")]
    public string Tfm { get; init; } = "";

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = "";

    [JsonPropertyName("appName")]
    public string AppName { get; init; } = "";

    [JsonPropertyName("packageId")]
    public string? PackageId { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("currentPort")]
    public int? CurrentPort { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("processId")]
    public int? ProcessId { get; init; }
}
