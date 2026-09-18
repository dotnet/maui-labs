using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CometBaristaNotes.Services.Grind;

public static class DeterministicGrindInterpolator
{
    public static readonly IReadOnlyList<GrindAnchor> DF64SeedAnchors =
    [
        new(50m, 0m, "df64v-chart"),
        new(140m, 6m, "df64v-chart"),
        new(280m, 18m, "df64v-chart"),
        new(500m, 41m, "df64v-chart"),
        new(600m, 50m, "df64v-chart"),
        new(700m, 60m, "df64v-chart"),
        new(1000m, 75m, "df64v-chart"),
        new(1300m, 90m, "df64v-chart"),
    ];

    public static IReadOnlyList<GrindAnchor> ParseAnchors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<GrindAnchor>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<GrindAnchor>();
            var anchors = new List<GrindAnchor>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("micron", out var micronElement)
                    || !element.TryGetProperty("setting", out var settingElement)
                    || !micronElement.TryGetDecimal(out var micron)
                    || !settingElement.TryGetDecimal(out var setting)
                    || micron <= 0
                    || setting < 0)
                    continue;
                var source = element.TryGetProperty("source", out var sourceElement)
                    ? sourceElement.GetString() ?? "user"
                    : "user";
                anchors.Add(new(micron, setting, source));
            }
            return anchors
                .GroupBy(anchor => anchor.Micron)
                .Select(group => group.Last())
                .OrderBy(anchor => anchor.Micron)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<GrindAnchor>();
        }
    }

    public static InterpolationResult? Interpolate(
        IReadOnlyList<GrindAnchor> anchors,
        decimal targetMicron,
        (decimal Min, decimal Max)? targetMicronRange = null,
        decimal? minSetting = null,
        decimal? maxSetting = null)
    {
        var valid = anchors
            .Where(anchor => anchor.Micron > 0 && anchor.Setting >= 0)
            .GroupBy(anchor => anchor.Micron)
            .Select(group => group.Last())
            .OrderBy(anchor => anchor.Micron)
            .ToList();
        if (valid.Count < 2)
            return null;

        var suggested = InterpolateOne(valid, targetMicron);
        var range = targetMicronRange ?? (targetMicron * 0.9m, targetMicron * 1.1m);
        var first = InterpolateOne(valid, range.Min);
        var second = InterpolateOne(valid, range.Max);
        var minimum = Math.Min(first, second);
        var maximum = Math.Max(first, second);

        if (minSetting.HasValue)
        {
            minimum = Math.Max(minimum, minSetting.Value);
            suggested = Math.Max(suggested, minSetting.Value);
        }
        if (maxSetting.HasValue)
        {
            maximum = Math.Min(maximum, maxSetting.Value);
            suggested = Math.Min(suggested, maxSetting.Value);
        }

        return new(
            Math.Round(minimum, 2),
            Math.Round(maximum, 2),
            Math.Round(suggested, 2),
            valid.Count);
    }

    private static decimal InterpolateOne(IReadOnlyList<GrindAnchor> anchors, decimal micron)
    {
        if (micron <= anchors[0].Micron) return anchors[0].Setting;
        if (micron >= anchors[^1].Micron) return anchors[^1].Setting;
        for (var index = 0; index < anchors.Count - 1; index++)
        {
            var first = anchors[index];
            var second = anchors[index + 1];
            if (micron < first.Micron || micron > second.Micron)
                continue;
            var span = second.Micron - first.Micron;
            return span == 0
                ? first.Setting
                : first.Setting + (micron - first.Micron) / span * (second.Setting - first.Setting);
        }
        return anchors[^1].Setting;
    }
}

public sealed record InterpolationResult(decimal Min, decimal Max, decimal Suggested, int AnchorsUsed);
