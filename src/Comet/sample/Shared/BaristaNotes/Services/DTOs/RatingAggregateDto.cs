using System.Collections.Generic;

namespace CometBaristaNotes.Services.DTOs;

public class RatingAggregateDto
{
    public double AverageRating { get; set; }
    public int TotalShots { get; set; }
    public int RatedShots { get; set; }
    public Dictionary<int, int> Distribution { get; set; } = new();

    public bool HasRatings => RatedShots > 0;
    public string FormattedAverage => HasRatings ? $"{AverageRating:F1} / 4" : "N/A";

    public int GetCountForRating(int rating) =>
        Distribution.TryGetValue(rating, out var count) ? count : 0;

    public double GetPercentageForRating(int rating) =>
        RatedShots == 0 ? 0.0 : (GetCountForRating(rating) / (double)RatedShots) * 100.0;
}
