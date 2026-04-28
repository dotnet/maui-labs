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

    [JsonPropertyName("project")]
    public string Project { get; init; } = "";

    [JsonPropertyName("tfm")]
    public string Tfm { get; init; } = "";

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = "";

    [JsonPropertyName("appName")]
    public string AppName { get; init; } = "";

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("connectedAt")]
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Computes the agent ID from project path and TFM.
    /// </summary>
    public static string ComputeId(string project, string tfm)
    {
        var input = $"{project}|{tfm}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
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

    [JsonPropertyName("currentPort")]
    public int? CurrentPort { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}
