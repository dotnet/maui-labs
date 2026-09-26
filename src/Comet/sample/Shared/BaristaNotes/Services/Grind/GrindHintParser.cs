using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CometBaristaNotes.Services.Grind;

public static class GrindHintParser
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);
    private static readonly Regex MicronsRegex = new(
        @"(?<v>\d{2,4}(?:\.\d+)?)\s*(?:µm|μm|um|micron[s]?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        Timeout);
    private static readonly Regex NumericWithScaleRegex = new(
        @"(?:(?<scale>[A-Za-z][A-Za-z0-9 ]{1,20})\s*[:\-]\s*(?<v>\d{1,3}(?:\.\d+)?))|(?:(?<v2>\d{1,3}(?:\.\d+)?)\s+on\s+(?<scale2>[A-Za-z][A-Za-z0-9 ]{1,20}))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        Timeout);

    public static ParsedGrindHint Parse(string? rawHint)
    {
        if (string.IsNullOrWhiteSpace(rawHint))
            return new(GrindHintKind.Unknown, rawHint ?? string.Empty, "raw:");

        var raw = rawHint.Trim();
        var lower = raw.ToLowerInvariant();
        try
        {
            var match = MicronsRegex.Match(raw);
            if (match.Success
                && decimal.TryParse(match.Groups["v"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                && value is >= 50 and <= 3000)
            {
                var microns = Math.Round(value, 0);
                return new(
                    GrindHintKind.Microns,
                    raw,
                    $"um:{microns:0}",
                    microns,
                    (microns - 25, microns + 25));
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        var descriptor = MatchDescriptor(lower);
        if (descriptor.HasValue)
        {
            var range = DescriptorMicronRange(descriptor.Value);
            return new(
                GrindHintKind.Descriptive,
                raw,
                $"desc:{DescriptorToken(descriptor.Value)}",
                Math.Round((range.Min + range.Max) / 2m, 0),
                range,
                descriptor);
        }

        try
        {
            var match = NumericWithScaleRegex.Match(raw);
            if (match.Success)
            {
                var scale = match.Groups["scale"].Success ? match.Groups["scale"] : match.Groups["scale2"];
                var value = match.Groups["v"].Success ? match.Groups["v"] : match.Groups["v2"];
                if (decimal.TryParse(value.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var numeric))
                {
                    var normalizedScale = scale.Value.Trim().ToLowerInvariant();
                    return new(
                        GrindHintKind.Numeric,
                        raw,
                        $"num:{normalizedScale}:{numeric}",
                        NumericScale: normalizedScale,
                        NumericValue: numeric);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return new(GrindHintKind.Unknown, raw, $"raw:{lower}");
    }

    public static (decimal Min, decimal Max) DescriptorMicronRange(GrindDescriptor descriptor) => descriptor switch
    {
        GrindDescriptor.ExtraFine => (100m, 200m),
        GrindDescriptor.Fine => (200m, 400m),
        GrindDescriptor.MediumFine => (400m, 600m),
        GrindDescriptor.Medium => (600m, 800m),
        GrindDescriptor.MediumCoarse => (800m, 1000m),
        GrindDescriptor.Coarse => (1000m, 1200m),
        GrindDescriptor.ExtraCoarse => (1200m, 1500m),
        _ => (600m, 800m),
    };

    private static GrindDescriptor? MatchDescriptor(string value)
    {
        if (value.Contains("extra-fine") || value.Contains("extra fine") || value.Contains("very fine")) return GrindDescriptor.ExtraFine;
        if (value.Contains("extra-coarse") || value.Contains("extra coarse") || value.Contains("very coarse")) return GrindDescriptor.ExtraCoarse;
        if (value.Contains("medium-fine") || value.Contains("medium fine")) return GrindDescriptor.MediumFine;
        if (value.Contains("medium-coarse") || value.Contains("medium coarse")) return GrindDescriptor.MediumCoarse;
        if (value.Contains("medium")) return GrindDescriptor.Medium;
        if (value.Contains("fine")) return GrindDescriptor.Fine;
        if (value.Contains("coarse")) return GrindDescriptor.Coarse;
        return null;
    }

    private static string DescriptorToken(GrindDescriptor descriptor) => descriptor switch
    {
        GrindDescriptor.ExtraFine => "extra-fine",
        GrindDescriptor.Fine => "fine",
        GrindDescriptor.MediumFine => "medium-fine",
        GrindDescriptor.Medium => "medium",
        GrindDescriptor.MediumCoarse => "medium-coarse",
        GrindDescriptor.Coarse => "coarse",
        GrindDescriptor.ExtraCoarse => "extra-coarse",
        _ => "unknown",
    };
}
