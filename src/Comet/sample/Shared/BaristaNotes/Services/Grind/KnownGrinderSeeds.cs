using System;
using System.Collections.Generic;

namespace CometBaristaNotes.Services.Grind;

public static class KnownGrinderSeeds
{
    public static IReadOnlyList<GrindAnchor>? TryGet(string? grinderName)
    {
        if (string.IsNullOrWhiteSpace(grinderName))
            return null;
        return grinderName.Contains("DF64", StringComparison.OrdinalIgnoreCase)
            ? DeterministicGrindInterpolator.DF64SeedAnchors
            : null;
    }
}
