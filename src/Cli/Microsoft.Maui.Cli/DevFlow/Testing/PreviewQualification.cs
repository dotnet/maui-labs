using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.DevFlow.Testing;

internal sealed record PreviewRate(int Successes, int Total);
internal sealed record PreviewQualificationInput(
    int Schema, PreviewRate Repair, PreviewRate Classification, PreviewRate Selectors,
    int FalseHeals, int NoRepairEvaluations, bool PhysicalAndroid,
    double CalibrationEce, double HostOperationP95Ms, int PrivacyEscapes, PreviewRate[] Tier1Flows);

internal sealed record PreviewQualificationResult(bool MetricGatesSatisfied, string[] Reasons)
{
    public string Qualification => "not-qualified";
    public bool DiagnosticOnly => true;
    public bool RepairAuthority => false;
}

/// <summary>Diagnostic metric checks adapted from preview-qualification-policy-v1, not a certifier.</summary>
internal static class PreviewQualification
{
    public static PreviewQualificationResult Evaluate(PreviewQualificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Schema != 1 || input.Repair is null || input.Classification is null ||
            input.Selectors is null || input.Tier1Flows is null || input.Tier1Flows.Length > 256 ||
            input.FalseHeals < 0 || input.NoRepairEvaluations is < 0 or > 1_000_000 ||
            input.FalseHeals > input.NoRepairEvaluations || input.PrivacyEscapes < 0 ||
            !double.IsFinite(input.CalibrationEce) || input.CalibrationEce is < 0 or > 1 ||
            !double.IsFinite(input.HostOperationP95Ms) || input.HostOperationP95Ms < 0)
            throw new ArgumentException("Invalid or unbounded qualification input.");
        foreach (var rate in input.Tier1Flows.Concat([input.Repair, input.Classification, input.Selectors]))
            if (rate is null || rate.Total is < 0 or > 1_000_000 || rate.Successes < 0 || rate.Successes > rate.Total)
                throw new ArgumentException("Metric counts must have explicit bounded valid denominators.");

        var reasons = new List<string>();
        if (input.Repair.Total < 100 || WilsonLower(input.Repair) < 0.95) reasons.Add("repair-precision");
        if (input.Classification.Total < 100 || Rate(input.Classification) < 0.90) reasons.Add("classification-accuracy");
        if (input.Selectors.Total < 100 || Rate(input.Selectors) < 0.99) reasons.Add("selector-stability");
        if (input.FalseHeals != 0 || input.NoRepairEvaluations < 300) reasons.Add("zero-false-heals");
        if (!input.PhysicalAndroid) reasons.Add("physical-android-evidence");
        if (input.CalibrationEce > 0.05) reasons.Add("confidence-calibration");
        if (input.HostOperationP95Ms > 250) reasons.Add("host-operation-budget");
        if (input.PrivacyEscapes != 0) reasons.Add("privacy-escapes");
        if (input.Tier1Flows.Length == 0 || input.Tier1Flows.Any(rate => rate.Total < 100 || Rate(rate) < 0.99))
            reasons.Add("tier1-first-attempts");
        return new(reasons.Count == 0, reasons.ToArray());
    }

    private static double Rate(PreviewRate rate) => rate.Total == 0 ? 0 : (double)rate.Successes / rate.Total;

    internal static double WilsonLower(PreviewRate rate)
    {
        if (rate.Total == 0) return 0;
        const double z = 1.959963984540054;
        var p = Rate(rate);
        var n = rate.Total;
        return (p + z * z / (2 * n) - z * Math.Sqrt((p * (1 - p) + z * z / (4 * n)) / n))
            / (1 + z * z / n);
    }
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PreviewQualificationInput))]
[JsonSerializable(typeof(PreviewQualificationResult))]
internal sealed partial class PreviewQualificationJsonContext : JsonSerializerContext;
