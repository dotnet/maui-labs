using System.Globalization;

namespace Microsoft.Maui.DevFlow.Testing;

internal static class MauiFlowStepIdentity
{
    public static string Create(int sequence)
        => $"step-{sequence.ToString("D4", CultureInfo.InvariantCulture)}";

    public static string Get(FlowStep step)
        => string.IsNullOrWhiteSpace(step.StepId)
            ? step.Seq.ToString(CultureInfo.InvariantCulture)
            : step.StepId.Trim();

    public static FlowStep? FindBySequence(MauiFlow? flow, int sequence)
        => flow?.Steps.FirstOrDefault(step => step.Seq == sequence);

    public static FlowStep? Find(MauiFlow? flow, string? stepId)
    {
        if (flow is null || string.IsNullOrWhiteSpace(stepId))
            return null;

        var value = stepId.Trim();
        var stableMatches = flow.Steps
            .Where(step =>
                !string.IsNullOrWhiteSpace(step.StepId) &&
                string.Equals(step.StepId.Trim(), value, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        var sequenceValue = value.StartsWith("step-", StringComparison.Ordinal)
            ? value[5..]
            : value;
        if (!int.TryParse(sequenceValue, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            return stableMatches.Length == 1 ? stableMatches[0] : null;

        var sequenceMatch = FindBySequence(flow, sequence);
        if (!value.StartsWith("step-", StringComparison.Ordinal))
            return sequenceMatch;
        if (stableMatches.Length > 1 ||
            stableMatches.Length == 1 &&
            sequenceMatch is not null &&
            !ReferenceEquals(stableMatches[0], sequenceMatch))
        {
            return null;
        }

        return stableMatches.Length == 1 ? stableMatches[0] : sequenceMatch;
    }
}
