// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.Models;

public record DiagnoseResult
{
    /// <summary>
    /// Schema version. Increment when introducing breaking changes to the JSON shape.
    /// Consumers should treat unknown future versions as best-effort.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("status")]
    public required DiagnoseStatus Status { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("checks")]
    public required IReadOnlyList<DiagnoseCheckResult> Checks { get; init; }

    public static DiagnoseStatus ComputeStatus(IEnumerable<DiagnoseCheckResult> checks)
    {
        var status = DiagnoseStatus.Passed;
        foreach (var check in checks)
        {
            if (check.Status == DiagnoseCheckStatus.Failed)
                return DiagnoseStatus.Failed;
            if (check.Status == DiagnoseCheckStatus.Warning)
                status = DiagnoseStatus.Warning;
        }
        return status;
    }
}

public record DiagnoseCheckResult
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required DiagnoseCheckStatus Status { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("remediation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RemediationResult? Remediation { get; init; }
}

[JsonConverter(typeof(DiagnoseStatusConverter))]
public enum DiagnoseStatus
{
    Passed,
    Warning,
    Failed,
}

[JsonConverter(typeof(DiagnoseCheckStatusConverter))]
public enum DiagnoseCheckStatus
{
    Passed,
    Warning,
    Failed,
    Skipped,
}

internal sealed class DiagnoseStatusConverter()
    : JsonStringEnumConverter<DiagnoseStatus>(JsonNamingPolicy.CamelCase);

internal sealed class DiagnoseCheckStatusConverter()
    : JsonStringEnumConverter<DiagnoseCheckStatus>(JsonNamingPolicy.CamelCase);