namespace Microsoft.Maui.Cli.DevFlow.Testing;

internal sealed record PreviewFailureIdentity(
    string? FlowDigest, string? PlanDigest, string? AppSourceFingerprint, string? RuntimeProfile,
    string? Checkpoint, string? Seed, string? FailureCode, string? StepId);

internal enum PreviewCorrespondence { Indeterminate, Different, MatchingFacts }

/// <summary>Compares supplied facts only. Even MatchingFacts is not attestation or run authority.</summary>
internal static class PreviewFailureCorrespondence
{
    public static PreviewCorrespondence Compare(PreviewFailureIdentity imported, PreviewFailureIdentity local)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(local);
        string?[] left = [imported.FlowDigest, imported.PlanDigest, imported.AppSourceFingerprint,
            imported.RuntimeProfile, imported.Checkpoint, imported.Seed, imported.FailureCode, imported.StepId];
        string?[] right = [local.FlowDigest, local.PlanDigest, local.AppSourceFingerprint,
            local.RuntimeProfile, local.Checkpoint, local.Seed, local.FailureCode, local.StepId];
        // Absence is not proof of either a match or a mismatch.
        if (left.Concat(right).Any(string.IsNullOrWhiteSpace))
            return PreviewCorrespondence.Indeterminate;
        return left.SequenceEqual(right, StringComparer.Ordinal)
            ? PreviewCorrespondence.MatchingFacts : PreviewCorrespondence.Different;
    }
}
