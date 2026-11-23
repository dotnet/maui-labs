using System;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services.Grind;

public enum GrindTranslationSource
{
    UserHistory,
    Deterministic,
    Cache,
    AI,
    Default,
}

public enum GrindHintKind
{
    Unknown,
    Microns,
    Descriptive,
    Numeric,
}

public enum GrindDescriptor
{
    ExtraFine,
    Fine,
    MediumFine,
    Medium,
    MediumCoarse,
    Coarse,
    ExtraCoarse,
}

public sealed record GrindTranslationRequest(
    int? EquipmentId,
    string GrinderModel,
    string GrindHint,
    BrewMethod Method,
    int? BeanId);

public sealed record GrindTranslationResult(
    decimal? MinSetting,
    decimal? MaxSetting,
    decimal? SuggestedSetting,
    GrindHintKind ParsedKind,
    GrindTranslationSource Source,
    string ConfidenceLabel,
    string? Explanation,
    string GrinderModel);

public sealed record GrindAnchor(
    decimal Micron,
    decimal Setting,
    string Source,
    DateTime? UpdatedAt = null);

public sealed record ParsedGrindHint(
    GrindHintKind Kind,
    string RawHint,
    string Normalized,
    decimal? Microns = null,
    (decimal Min, decimal Max)? MicronRange = null,
    GrindDescriptor? Descriptor = null,
    string? NumericScale = null,
    decimal? NumericValue = null);

public interface IGrindTranslationService
{
    Task<GrindTranslationResult> TranslateAsync(
        GrindTranslationRequest request,
        CancellationToken cancellationToken = default);
}
