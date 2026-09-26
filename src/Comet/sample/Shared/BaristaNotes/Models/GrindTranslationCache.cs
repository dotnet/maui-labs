using System;

using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Models;

/// <summary>
/// Cached grind-translation result for a (grinder model, recipe grind hint,
/// brew method) triple. TTL-based cache.
/// </summary>
public class GrindTranslationCache
{
    public int Id { get; set; }
    public string GrinderModelNormalized { get; set; } = string.Empty;
    public string GrindHintNormalized { get; set; } = string.Empty;
    public BrewMethod BrewMethod { get; set; }
    public decimal? MinSetting { get; set; }
    public decimal? MaxSetting { get; set; }
    public decimal? SuggestedSetting { get; set; }
    public string Confidence { get; set; } = "low";
    public string Source { get; set; } = "Default";
    public string? Explanation { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
