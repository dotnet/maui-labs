using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

internal sealed class LayoutDiagnosticsDelta
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "layout-diagnostics-delta";

    [JsonPropertyName("snapshot")]
    public LayoutSnapshotInfo Snapshot { get; set; } = new();

    [JsonPropertyName("summary")]
    public LayoutInspectionSummary Summary { get; set; } = new();

    [JsonPropertyName("coverage")]
    public LayoutCoverageInfo Coverage { get; set; } = new();

    [JsonPropertyName("added")]
    public List<LayoutFinding> Added { get; set; } = [];

    [JsonPropertyName("updated")]
    public List<LayoutFinding> Updated { get; set; } = [];

    [JsonPropertyName("removed")]
    public List<string> Removed { get; set; } = [];
}

internal static class LayoutDiagnosticsDeltaBuilder
{
    public static LayoutDiagnosticsDelta? Build(
        LayoutInspectionResult? previous,
        LayoutInspectionResult current)
    {
        if (previous is null)
        {
            return new LayoutDiagnosticsDelta
            {
                Snapshot = current.Snapshot,
                Summary = current.Summary,
                Coverage = current.Coverage,
                Added = current.Findings.ToList()
            };
        }

        if (previous.Findings.GroupBy(
                finding => finding.Id,
                StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1)
            || current.Findings.GroupBy(
                finding => finding.Id,
                StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            return null;
        }

        var unmatchedPrevious = previous.Findings
            .GroupBy(finding => finding.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var delta = new LayoutDiagnosticsDelta
        {
            Snapshot = current.Snapshot,
            Summary = current.Summary,
            Coverage = current.Coverage
        };

        foreach (var finding in current.Findings)
        {
            if (!unmatchedPrevious.TryGetValue(finding.Id, out var candidates)
                || candidates.Count == 0)
            {
                delta.Added.Add(finding);
            }
            else
            {
                var currentSignature = Signature(finding);
                var exactMatch = candidates.FindIndex(candidate =>
                    Signature(candidate).Equals(
                        currentSignature,
                        StringComparison.Ordinal));
                if (exactMatch >= 0)
                {
                    candidates.RemoveAt(exactMatch);
                }
                else
                {
                    candidates.RemoveAt(0);
                    delta.Updated.Add(finding);
                }
            }
        }

        foreach (var candidates in unmatchedPrevious.Values)
        {
            foreach (var finding in candidates)
                delta.Removed.Add(finding.Id);
        }

        return delta.Added.Count == 0
            && delta.Updated.Count == 0
            && delta.Removed.Count == 0
            && SummariesEqual(previous.Summary, current.Summary)
            && previous.Coverage.Overall == current.Coverage.Overall
                ? null
                : delta;
    }

    private static string Signature(LayoutFinding finding)
        => string.Join(
            "|",
            finding.RuleId,
            finding.Subtype,
            finding.Outcome,
            finding.Severity,
            finding.Confidence,
            finding.Actionability,
            finding.Element.Id,
            finding.Message,
            finding.Suppressed,
            string.Join(
                ",",
                finding.RelatedElements
                    .OrderBy(related => related.Relation, StringComparer.Ordinal)
                    .ThenBy(related => related.Element.Id, StringComparer.Ordinal)
                    .Select(related => $"{related.Relation}:{related.Element.Id}")));

    private static bool SummariesEqual(
        LayoutInspectionSummary first,
        LayoutInspectionSummary second)
        => first.Violations == second.Violations
            && first.Observations == second.Observations
            && first.Incomplete == second.Incomplete
            && first.Passes == second.Passes
            && first.NotApplicable == second.NotApplicable
            && first.Suppressed == second.Suppressed;
}
