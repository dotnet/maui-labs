using System;

using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Models;

/// <summary>
/// A brewing recipe for a specific Bean and BrewMethod.
/// </summary>
public class Recipe
{
    public int Id { get; set; }
    public int BeanId { get; set; }
    public BrewMethod BrewMethod { get; set; }
    public RecipeSource Source { get; set; }
    public string? SourceUrl { get; set; }
    public string? Title { get; set; }

    public decimal? DoseIn { get; set; }
    public decimal? OutputAmount { get; set; }
    public string? GrindHint { get; set; }
    public decimal? BrewTempC { get; set; }
    public decimal? TotalTimeSeconds { get; set; }
    public string? ParametersJson { get; set; }
    public string? Notes { get; set; }

    public DateTime FetchedAt { get; set; }
    public bool IsEditedByUser { get; set; }

    public Guid SyncId { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public bool IsDeleted { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Bean? Bean { get; set; }
}
