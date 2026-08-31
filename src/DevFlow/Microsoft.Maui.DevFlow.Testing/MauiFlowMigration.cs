using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Previews normalization of an existing executable flow without writing a file or inventing
/// runtime evidence. Schema 3 and identity-bearing migrations are intentionally out of scope.
/// </summary>
public static class MauiFlowMigration
{
    /// <summary>
    /// Produces a non-mutating preview for a legacy flow. Only schema 1 to schema 2 normalization
    /// is supported; schema 2 is already current and all other source or target schemas fail
    /// closed with a non-writable result.
    /// </summary>
    public static MauiFlowMigrationResult Preview(MauiFlow source, int targetSchema = MauiFlow.CurrentSchema)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = new MauiFlowMigrationResult
        {
            SourceSchema = source.Schema,
            TargetSchema = targetSchema,
        };

        if (targetSchema != MauiFlow.CurrentSchema)
        {
            result.Warnings.Add(
                $"Schema {targetSchema} is not a supported migration target. Only schema {MauiFlow.CurrentSchema} normalization is available.");
            return result;
        }

        if (source.Schema == 1)
        {
            var normalized = MauiFlowClone.Clone(source);
            normalized.Schema = MauiFlow.CurrentSchema;
            result.NormalizedFlow = normalized;
            result.WriteRequired = true;
            result.CanWrite = true;
            result.Changes.Add(new(
                "/schema",
                "replace",
                "Normalized the legacy schema marker from 1 to 2 without adding selector diagnostics or live facts."));
            result.Warnings.Add(
                "The preview does not generate fingerprints, source anchors, IDs, revisions, or validation evidence.");
            return result;
        }

        if (source.Schema == MauiFlow.CurrentSchema)
        {
            result.NormalizedFlow = MauiFlowClone.Clone(source);
            result.CanWrite = true;
            result.Warnings.Add($"The flow already uses schema {MauiFlow.CurrentSchema}; no write is needed.");
            return result;
        }

        result.Warnings.Add(
            $"Schema {source.Schema} is not supported for migration. The original flow remains unchanged.");
        return result;
    }
}

/// <summary>The complete result of a non-mutating flow migration preview.</summary>
public sealed class MauiFlowMigrationResult
{
    [JsonPropertyName("sourceSchema")] public int SourceSchema { get; init; }
    [JsonPropertyName("targetSchema")] public int TargetSchema { get; init; }
    [JsonPropertyName("normalizedFlow")] public MauiFlow? NormalizedFlow { get; internal set; }
    [JsonPropertyName("changes")] public List<MauiFlowMigrationChange> Changes { get; } = [];
    [JsonPropertyName("warnings")] public List<string> Warnings { get; } = [];
    [JsonPropertyName("writeRequired")] public bool WriteRequired { get; internal set; }
    [JsonPropertyName("canWrite")] public bool CanWrite { get; internal set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A precise prospective change in a migration preview.</summary>
public sealed record MauiFlowMigrationChange(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("description")] string Description);
