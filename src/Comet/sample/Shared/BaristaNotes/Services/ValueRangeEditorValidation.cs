#nullable enable
using System.Globalization;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services;

public static class ValueRangeEditorValidation
{
    public static bool TryGetCanonicalRange(
        DrinkValueMetric metric,
        DrinkValueRangeDefinition definition,
        RangeEditorUnit editorUnit,
        string minimumText,
        string maximumText,
        string originalMinimumText,
        string originalMaximumText,
        decimal originalMinimumCanonical,
        decimal originalMaximumCanonical,
        out DrinkValueRange range,
        out string? error,
        CultureInfo? culture = null)
    {
        range = definition.AutoRange;
        culture ??= CultureInfo.CurrentCulture;

        if (!decimal.TryParse(minimumText, NumberStyles.Number, culture, out var displayMinimum)
            || !decimal.TryParse(maximumText, NumberStyles.Number, culture, out var displayMaximum))
        {
            error = $"Enter valid values in {editorUnit.Label}.";
            return false;
        }

        var minimum = minimumText == originalMinimumText
            ? originalMinimumCanonical
            : editorUnit.ToCanonical(displayMinimum);
        var maximum = maximumText == originalMaximumText
            ? originalMaximumCanonical
            : editorUnit.ToCanonical(displayMaximum);

        if (minimum >= maximum)
        {
            error = "Minimum must be less than maximum.";
            return false;
        }

        if (!definition.HardRange.Contains(minimum)
            || !definition.HardRange.Contains(maximum))
        {
            error =
                $"Use values from {DrinkValueRangeFormatting.FormatRange(metric, definition.HardRange)}.";
            return false;
        }

        if (metric is DrinkValueMetric.DoseIn or DrinkValueMetric.Yield
            && (decimal.Round(minimum, 1) != minimum
                || decimal.Round(maximum, 1) != maximum))
        {
            error = "Use no more than one decimal place.";
            return false;
        }

        if (metric is DrinkValueMetric.GrindMicrons or DrinkValueMetric.Time
            && (decimal.Truncate(minimum) != minimum
                || decimal.Truncate(maximum) != maximum))
        {
            error = "Use values that convert to whole units.";
            return false;
        }

        range = new DrinkValueRange(minimum, maximum);
        error = null;
        return true;
    }
}
