using System;
using System.Collections.Generic;
using System.Linq;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services.DTOs;

public record ShotContextDto
{
    public decimal DoseIn { get; init; }
    public decimal? ActualOutput { get; init; }
    public decimal? ActualTime { get; init; }
    public int? GrindMicrons { get; init; }
    public int? Rating { get; init; }
    public string? TastingNotes { get; init; }
    public string? DrinkType { get; init; }
    public BrewMethod BrewMethod { get; init; } = BrewMethod.Espresso;
    public DateTime Timestamp { get; init; }
}

public record BeanContextDto
{
    public string Name { get; init; } = string.Empty;
    public string? Roaster { get; init; }
    public string? Origin { get; init; }
    public DateTime RoastDate { get; init; }
    public int DaysFromRoast { get; init; }
    public string? Notes { get; init; }
}

public record AIAdviceRequestDto
{
    public int ShotId { get; init; }
    public required ShotContextDto CurrentShot { get; init; }
    public List<ShotContextDto> HistoricalShots { get; init; } = [];
    public required BeanContextDto BeanInfo { get; init; }
    public EquipmentContextDto? Equipment { get; init; }
    public UserProfileDto? MadeFor { get; init; }
}

public record AIAdviceResponseDto
{
    public bool Success { get; init; }
    public IReadOnlyList<ShotAdjustment> Adjustments { get; init; } = Array.Empty<ShotAdjustment>();
    public string? Reasoning { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public string? Source { get; init; }
    public string? PromptSent { get; init; }
    public int HistoricalShotsCount { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public record ShotAdjustment
{
    public string Parameter { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;

    public string Recommendation => string.Join(
        " ",
        new[] { Direction, Parameter, Amount }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public record BeanRecommendationContextDto
{
    public int BeanId { get; init; }
    public string BeanName { get; init; } = string.Empty;
    public string? Roaster { get; init; }
    public string? Origin { get; init; }
    public string? Notes { get; init; }
    public DateTime? RoastDate { get; init; }
    public int? DaysFromRoast { get; init; }
    public bool HasHistory { get; init; }
    public List<ShotContextDto>? HistoricalShots { get; init; }
    public EquipmentContextDto? Equipment { get; init; }
}

public enum RecommendationType
{
    NewBean = 0,
    ReturningBean = 1
}

public record AIRecommendationDto
{
    public bool Success { get; init; }
    public decimal Dose { get; init; }
    public string GrindSetting { get; init; } = string.Empty;
    public decimal Output { get; init; }
    public decimal Duration { get; init; }
    public RecommendationType RecommendationType { get; init; }
    public string? Confidence { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public string? Source { get; init; }
}
public record SpeechRecognitionResultDto(bool Success, string? Transcript, double Confidence, string? ErrorMessage);

public sealed class BeanLabelExtraction
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Name { get; set; }
    public string? Roaster { get; set; }
    public string? Origin { get; set; }
    public DateTime? RoastDate { get; set; }
    public string? Notes { get; set; }
}

public enum PhotoWorkflowIntent { Unknown, Coffee, Profile, Room }
public sealed record PhotoWorkflowAnalysis(bool Success, PhotoWorkflowIntent Intent, bool IsObvious, string? Rationale, string? ErrorMessage);
public sealed record PersonIdentificationCandidate(int ProfileId, string Name, byte[] AvatarBytes);
public sealed record PersonIdentificationResult(bool Success, int? MatchedProfileId, string? MatchedName, string? Rationale, string? ErrorMessage);
public sealed record VisionAnalysisResult(bool Success, int PeopleCount, int CupsNeeded, int BeansNeededGrams, string? Message, string? ErrorMessage);
