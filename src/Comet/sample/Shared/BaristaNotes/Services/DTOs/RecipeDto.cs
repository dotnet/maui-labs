using System;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services.DTOs;

public record RecipeDto
{
    public int Id { get; init; }
    public int BeanId { get; init; }
    public BrewMethod BrewMethod { get; init; }
    public RecipeSource Source { get; init; }
    public string? SourceUrl { get; init; }
    public string? Title { get; init; }
    public decimal? DoseIn { get; init; }
    public decimal? OutputAmount { get; init; }
    public string? GrindHint { get; init; }
    public decimal? BrewTempC { get; init; }
    public decimal? TotalTimeSeconds { get; init; }
    public string? ParametersJson { get; init; }
    public string? Notes { get; init; }
    public DateTime FetchedAt { get; init; }
    public bool IsEditedByUser { get; init; }
}

public record CreateRecipeDto
{
    public int BeanId { get; init; }
    public BrewMethod BrewMethod { get; init; }
    public RecipeSource Source { get; init; }
    public string? SourceUrl { get; init; }
    public string? Title { get; init; }
    public decimal? DoseIn { get; init; }
    public decimal? OutputAmount { get; init; }
    public string? GrindHint { get; init; }
    public decimal? BrewTempC { get; init; }
    public decimal? TotalTimeSeconds { get; init; }
    public string? ParametersJson { get; init; }
    public string? Notes { get; init; }
}

public record UpdateRecipeDto
{
    public string? Title { get; init; }
    public decimal? DoseIn { get; init; }
    public decimal? OutputAmount { get; init; }
    public string? GrindHint { get; init; }
    public decimal? BrewTempC { get; init; }
    public decimal? TotalTimeSeconds { get; init; }
    public string? ParametersJson { get; init; }
    public string? Notes { get; init; }
}
