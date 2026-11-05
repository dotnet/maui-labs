using System;
using System.Collections.Generic;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services.DTOs;

public record ShotRecordDto
{
    public int Id { get; init; }
    public DateTime Timestamp { get; init; }
    public BeanDto? Bean { get; init; }
    public BagSummaryDto? Bag { get; init; }
    public EquipmentDto? Machine { get; init; }
    public EquipmentDto? Grinder { get; init; }
    public List<EquipmentDto> Accessories { get; init; } = new();
    public UserProfileDto? MadeBy { get; init; }
    public UserProfileDto? MadeFor { get; init; }
    public BrewMethod BrewMethod { get; init; } = BrewMethod.Espresso;
    public string? ParametersJson { get; init; }
    public decimal DoseIn { get; init; }
    public int? GrindMicrons { get; init; }
    public decimal? WaterTempC { get; init; }
    public decimal ExpectedTime { get; init; }
    public decimal ExpectedOutput { get; init; }
    public string DrinkType { get; init; } = string.Empty;
    public decimal? ActualTime { get; init; }
    public decimal? ActualOutput { get; init; }
    public decimal? PreinfusionTime { get; init; }
    public int? Rating { get; init; }
    public string? TastingNotes { get; init; }
}

public record CreateShotDto
{
    public DateTime? Timestamp { get; init; }
    public int? BagId { get; init; }
    public int? MachineId { get; init; }
    public int? GrinderId { get; init; }
    public List<int> AccessoryIds { get; init; } = new();
    public int? MadeById { get; init; }
    public int? MadeForId { get; init; }
    public BrewMethod BrewMethod { get; init; } = BrewMethod.Espresso;
    public string? ParametersJson { get; init; }
    public decimal DoseIn { get; init; }
    public int? GrindMicrons { get; init; }
    public decimal? WaterTempC { get; init; }
    public decimal ExpectedTime { get; init; }
    public decimal ExpectedOutput { get; init; }
    public string DrinkType { get; init; } = string.Empty;
    public decimal? ActualTime { get; init; }
    public decimal? ActualOutput { get; init; }
    public decimal? PreinfusionTime { get; init; }
    public int? Rating { get; init; }
    public string? TastingNotes { get; init; }
}

public record UpdateShotDto
{
    public int? BagId { get; init; }
    public int? MachineId { get; init; }
    public int? GrinderId { get; init; }
    // Null IDs leave equipment unchanged unless clearing is explicitly requested.
    public bool ClearMachine { get; init; }
    public bool ClearGrinder { get; init; }
    public List<int>? AccessoryIds { get; init; }
    public int? MadeById { get; init; }
    public int? MadeForId { get; init; }
    public decimal? ActualTime { get; init; }
    public decimal? ActualOutput { get; init; }
    public decimal? PreinfusionTime { get; init; }
    public int? Rating { get; init; }
    public string DrinkType { get; init; } = string.Empty;
    public decimal? DoseIn { get; init; }
    public int? GrindMicrons { get; init; }
    public decimal? WaterTempC { get; init; }
    public decimal? ExpectedTime { get; init; }
    public decimal? ExpectedOutput { get; init; }
    public string? TastingNotes { get; init; }
    public BrewMethod? BrewMethod { get; init; }
    public string? ParametersJson { get; init; }
}

public record EquipmentDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public EquipmentType Type { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateEquipmentDto
{
    public string Name { get; init; } = string.Empty;
    public EquipmentType Type { get; init; }
    public string? Notes { get; init; }
}

public record UpdateEquipmentDto
{
    public string? Name { get; init; }
    public EquipmentType? Type { get; init; }
    public string? Notes { get; init; }
    public bool? IsActive { get; init; }
}

public record BeanDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Roaster { get; init; }
    public DateTime? RoastDate { get; init; }
    public string? Origin { get; init; }
    public string? Notes { get; init; }
    public string? RoasterUrl { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public RatingAggregateDto? RatingAggregate { get; init; }
}

public record CreateBeanDto
{
    public string Name { get; init; } = string.Empty;
    public string? Roaster { get; init; }
    public DateTime? RoastDate { get; init; }
    public string? Origin { get; init; }
    public string? Notes { get; init; }
    public string? RoasterUrl { get; init; }
}

public record UpdateBeanDto
{
    public string? Name { get; init; }
    public string? Roaster { get; init; }
    public DateTime? RoastDate { get; init; }
    public string? Origin { get; init; }
    public string? Notes { get; init; }
    public string? RoasterUrl { get; init; }
    public bool? IsActive { get; init; }
}

public record UserProfileDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? AvatarPath { get; init; }
    public string? Context { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateUserProfileDto
{
    public string Name { get; init; } = string.Empty;
    public string? AvatarPath { get; init; }
    public string? Context { get; init; }
}

public record UpdateUserProfileDto
{
    public string? Name { get; init; }
    public string? AvatarPath { get; init; }
    public string? Context { get; init; }
}

public record PagedResult<T>
{
    public List<T> Items { get; init; } = new();
    public int TotalCount { get; init; }
    public int PageIndex { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageIndex > 0;
    public bool HasNextPage => PageIndex < TotalPages - 1;
}

public record BeanFilterOptionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public record ShotFilterCriteriaDto
{
    public IReadOnlyList<int>? BeanIds { get; init; }
    public IReadOnlyList<int>? MadeForIds { get; init; }
    public IReadOnlyList<int>? Ratings { get; init; }
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
    public bool HasFilters =>
        (BeanIds?.Count ?? 0) > 0 ||
        (MadeForIds?.Count ?? 0) > 0 ||
        (Ratings?.Count ?? 0) > 0 ||
        PeriodStart.HasValue;
}

public record EquipmentContextDto
{
    public string? MachineName { get; init; }
    public string? GrinderName { get; init; }
}

public class ProfileImageUpdateResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? NewAvatarPath { get; set; }

    public static ProfileImageUpdateResult SuccessResult(string avatarPath) =>
        new() { Success = true, NewAvatarPath = avatarPath };
    public static ProfileImageUpdateResult FailureResult(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
