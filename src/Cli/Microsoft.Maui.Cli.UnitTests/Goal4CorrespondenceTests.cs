using Microsoft.Maui.Cli.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class Goal4CorrespondenceTests
{
    private static PreviewFailureIdentity Identity() =>
        new("flow", "plan", "source", "runtime", "checkpoint", "seed", "locator-not-found", "3");

    [Fact]
    public void MissingSourceAttestationIsIndeterminateNotReproduced() =>
        Assert.Equal(PreviewCorrespondence.Indeterminate,
            PreviewFailureCorrespondence.Compare(Identity() with { AppSourceFingerprint = null }, Identity()));

    [Fact]
    public void MatchingFailureCodeAloneCannotEstablishCorrespondence() =>
        Assert.Equal(PreviewCorrespondence.Different,
            PreviewFailureCorrespondence.Compare(Identity(), Identity() with { Checkpoint = "different-route" }));

    [Fact]
    public void MatchingFactsRemainOnlyAComparison() =>
        Assert.Equal(PreviewCorrespondence.MatchingFacts,
            PreviewFailureCorrespondence.Compare(Identity(), Identity()));

    [Fact]
    public void APlanChangeInvalidatesCorrespondence() =>
        Assert.Equal(PreviewCorrespondence.Different,
            PreviewFailureCorrespondence.Compare(Identity(), Identity() with { PlanDigest = "changed" }));
}
