using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class Goal4QualificationTests
{
    private static PreviewQualificationInput ValidMetrics() =>
        new(1, new(100, 100), new(90, 100), new(99, 100), 0, 300, true, 0.05, 250, 0, [new(99, 100)]);

    [Fact]
    public void PassingMetricClaimsCannotQualifyOrAuthorize()
    {
        var result = PreviewQualification.Evaluate(ValidMetrics());
        Assert.True(result.MetricGatesSatisfied);
        Assert.Equal("not-qualified", result.Qualification);
        Assert.False(result.RepairAuthority);
        Assert.True(result.DiagnosticOnly);
    }

    [Fact]
    public void FiveEmulatorPassesAreNotPhysicalTierOneEvidence()
    {
        var result = PreviewQualification.Evaluate(ValidMetrics() with
        { PhysicalAndroid = false, Tier1Flows = [new(5, 5)] });
        Assert.Contains("physical-android-evidence", result.Reasons);
        Assert.Contains("tier1-first-attempts", result.Reasons);
        Assert.InRange(PreviewQualification.WilsonLower(new(5, 5)), 0.56, 0.57);
    }

    [Fact]
    public void RepairPrecisionUsesWilsonBoundNotPointEstimate()
    {
        var result = PreviewQualification.Evaluate(ValidMetrics() with { Repair = new(95, 100) });
        Assert.Contains("repair-precision", result.Reasons);
    }

    [Fact]
    public void EveryDeclaredFlowNeedsItsOwnDenominator()
    {
        var result = PreviewQualification.Evaluate(ValidMetrics() with { Tier1Flows = [new(1000, 1000), new(1, 1)] });
        Assert.Contains("tier1-first-attempts", result.Reasons);
    }

    [Fact]
    public void FalseHealAndPrivacyThresholdsAreZero()
    {
        var result = PreviewQualification.Evaluate(ValidMetrics() with { FalseHeals = 1, PrivacyEscapes = 1 });
        Assert.Contains("zero-false-heals", result.Reasons);
        Assert.Contains("privacy-escapes", result.Reasons);
    }

    [Theory]
    [InlineData(0.050001, 250, "confidence-calibration")]
    [InlineData(0.05, 250.001, "host-operation-budget")]
    public void MeasurableThresholdsRejectValuesJustOutsideBoundary(double ece, double p95, string reason) =>
        Assert.Contains(reason, PreviewQualification.Evaluate(
            ValidMetrics() with { CalibrationEce = ece, HostOperationP95Ms = p95 }).Reasons);

    [Fact]
    public void MalformedOrUnboundedCountsDoNotBecomeDefaults()
    {
        Assert.Throws<ArgumentException>(() => PreviewQualification.Evaluate(ValidMetrics() with { Repair = new(2, 1) }));
        Assert.Throws<ArgumentException>(() => PreviewQualification.Evaluate(ValidMetrics() with { CalibrationEce = double.NaN }));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{}", PreviewQualificationJsonContext.Default.PreviewQualificationInput));
    }

    [Fact]
    public async Task ExampleCorpusRemainsNotQualified()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "tests", "DevFlow", "Goal4Foundation", "qualification-example.json");
        var result = await PreviewQualificationCommands.AssessFileAsync(path, CancellationToken.None);
        Assert.Equal("not-qualified", result.Qualification);
        Assert.False(result.MetricGatesSatisfied);
        Assert.Contains("repair-precision", result.Reasons);
    }
}
