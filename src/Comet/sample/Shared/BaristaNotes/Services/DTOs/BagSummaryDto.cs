using System;

namespace CometBaristaNotes.Services.DTOs;

public class BagSummaryDto
{
    public int Id { get; set; }
    public int BeanId { get; set; }
    public string BeanName { get; set; } = string.Empty;
    public DateTime RoastDate { get; set; }
    public string? Notes { get; set; }
    public bool IsComplete { get; set; }
    public int ShotCount { get; set; }
    public double? AverageRating { get; set; }

    public string FormattedRoastDate => RoastDate.ToString("MMM dd, yyyy");
    public string DisplayLabel =>
        $"{BeanName} - Roasted {FormattedRoastDate}" +
        (Notes != null ? $" - {Notes}" : "");
    public string FormattedRating =>
        AverageRating.HasValue ? AverageRating.Value.ToString("F1") : "No ratings";
    public string StatusBadge => IsComplete ? "Complete" : "Active";
}
