using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services.Grind;

public sealed class InMemoryGrindTranslationService(IBaristaDataStore store) : IGrindTranslationService
{
    public Task<GrindTranslationResult> TranslateAsync(
        GrindTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = GrindHintParser.Parse(request.GrindHint);
        var profile = request.EquipmentId.HasValue
            ? store.GrinderProfiles.FirstOrDefault(item =>
                item.EquipmentId == request.EquipmentId.Value && !item.IsDeleted)
            : null;

        var history = FindMostRecentHistory(request);
        if (history?.GrindMicrons is int historyValue)
        {
            var historyMicrons = (decimal)historyValue;
            var result = TryProfileThenSeed(
                profile,
                request.GrinderModel,
                historyMicrons,
                (historyMicrons - 25m, historyMicrons + 25m));
            if (result is not null)
                return Task.FromResult(Build(
                    result,
                    parsed,
                    GrindTranslationSource.UserHistory,
                    "high",
                    $"From your last {FormatMethod(request.Method)} on this grinder.",
                    request.GrinderModel));
        }

        if (parsed.Microns.HasValue)
        {
            var profileResult = TryProfile(profile, parsed.Microns.Value, parsed.MicronRange);
            if (profileResult is not null)
                return Task.FromResult(Build(
                    profileResult,
                    parsed,
                    GrindTranslationSource.Deterministic,
                    profileResult.AnchorsUsed >= 3 ? "medium" : "low",
                    $"Calculated from calibration anchors for {request.GrinderModel}.",
                    request.GrinderModel));

            var seedResult = TrySeed(request.GrinderModel, parsed.Microns.Value, parsed.MicronRange);
            if (seedResult is not null)
                return Task.FromResult(Build(
                    seedResult,
                    parsed,
                    GrindTranslationSource.Deterministic,
                    "medium",
                    $"Calculated from the built-in DF64 calibration for {request.GrinderModel}.",
                    request.GrinderModel));
        }

        return Task.FromResult(new GrindTranslationResult(
            null,
            null,
            null,
            parsed.Kind,
            GrindTranslationSource.Default,
            "low",
            parsed.Kind == GrindHintKind.Unknown
                ? "Recipe grind couldn't be interpreted; adjust manually or add grinder calibration."
                : "Add at least two grinder calibration anchors to get a personalized setting.",
            request.GrinderModel));
    }

    private ShotRecord? FindMostRecentHistory(GrindTranslationRequest request)
    {
        if (!request.EquipmentId.HasValue)
            return null;
        var query = store.Shots.Where(shot =>
            !shot.IsDeleted
            && shot.GrinderId == request.EquipmentId.Value
            && shot.BrewMethod == request.Method
            && shot.GrindMicrons.HasValue);
        if (request.BeanId.HasValue)
        {
            var bagIds = store.Bags
                .Where(bag => bag.BeanId == request.BeanId.Value)
                .Select(bag => bag.Id)
                .ToHashSet();
            query = query.Where(shot => bagIds.Contains(shot.BagId));
        }
        return query.OrderByDescending(shot => shot.Timestamp).FirstOrDefault();
    }

    private static InterpolationResult? TryProfileThenSeed(
        GrinderProfile? profile,
        string grinderModel,
        decimal microns,
        (decimal Min, decimal Max)? range) =>
        TryProfile(profile, microns, range) ?? TrySeed(grinderModel, microns, range);

    private static InterpolationResult? TryProfile(
        GrinderProfile? profile,
        decimal microns,
        (decimal Min, decimal Max)? range)
    {
        var anchors = DeterministicGrindInterpolator.ParseAnchors(profile?.AnchorsJson);
        return anchors.Count < 2
            ? null
            : DeterministicGrindInterpolator.Interpolate(
                anchors,
                microns,
                range,
                profile?.MinSetting,
                profile?.MaxSetting);
    }

    private static InterpolationResult? TrySeed(
        string grinderModel,
        decimal microns,
        (decimal Min, decimal Max)? range)
    {
        var anchors = KnownGrinderSeeds.TryGet(grinderModel);
        return anchors is null
            ? null
            : DeterministicGrindInterpolator.Interpolate(anchors, microns, range);
    }

    private static GrindTranslationResult Build(
        InterpolationResult result,
        ParsedGrindHint parsed,
        GrindTranslationSource source,
        string confidence,
        string explanation,
        string grinderModel) =>
        new(
            result.Min,
            result.Max,
            result.Suggested,
            parsed.Kind,
            source,
            confidence,
            explanation,
            grinderModel);

    private static string FormatMethod(BrewMethod method) => method switch
    {
        BrewMethod.Espresso => "espresso",
        BrewMethod.PourOver => "pour-over",
        BrewMethod.V60 => "V60",
        BrewMethod.Moka => "moka",
        BrewMethod.Drip => "drip",
        BrewMethod.Aeropress => "Aeropress",
        BrewMethod.FrenchPress => "French press",
        BrewMethod.Turkish => "Turkish",
        BrewMethod.Siphon => "siphon",
        BrewMethod.Cupping => "cupping",
        BrewMethod.ColdBrew => "cold brew",
        BrewMethod.ColdDrip => "cold drip",
        BrewMethod.SteepAndRelease => "steep-and-release",
        _ => "drink",
    };
}
