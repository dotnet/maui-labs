using System;

using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Models;

public static class BrewMethodValueRangeCatalog
{
    public static DrinkValueRangeDefinition GetDefinition(
        BrewMethod method,
        DrinkValueMetric metric)
    {
        var profile = GetProfile(method);
        var hard = GetHardRanges(method);

        return metric switch
        {
            DrinkValueMetric.DoseIn => new(
                new(profile.DoseMin, profile.DoseMax),
                hard.Dose,
                profile.DoseDefault,
                profile.DoseStep,
                "g"),
            DrinkValueMetric.Yield => new(
                new(profile.OutputMin, profile.OutputMax),
                hard.Yield,
                profile.OutputDefault,
                profile.OutputStep,
                "g"),
            DrinkValueMetric.Time => new(
                new(profile.TimeMin, profile.TimeMax),
                hard.Time,
                profile.TimeDefault,
                profile.TimeStep,
                "s"),
            DrinkValueMetric.GrindMicrons => GetGrindDefinition(method),
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
        };
    }

    public static BrewMethodProfile GetProfile(BrewMethod method) => method switch
    {
        BrewMethod.Espresso => new(method, 5, 30, 18, 0.1m, 10, 100, 36, 0.1m, 10, 60, 28, 1),
        BrewMethod.PourOver => new(method, 10, 60, 20, 0.1m, 100, 800, 320, 0.1m, 60, 900, 210, 5),
        BrewMethod.V60 => new(method, 10, 40, 18, 0.1m, 100, 600, 300, 0.1m, 60, 600, 180, 5),
        BrewMethod.Moka => new(method, 5, 50, 18, 0.1m, 20, 400, 80, 0.1m, 30, 600, 180, 5),
        BrewMethod.Drip => new(method, 10, 120, 30, 0.1m, 100, 1500, 500, 0.1m, 60, 1200, 300, 10),
        BrewMethod.Aeropress => new(method, 5, 40, 15, 0.1m, 50, 400, 200, 0.1m, 30, 600, 90, 5),
        BrewMethod.FrenchPress => new(method, 10, 100, 30, 0.1m, 100, 1200, 500, 0.1m, 60, 900, 240, 5),
        BrewMethod.Turkish => new(method, 5, 20, 7, 0.1m, 25, 150, 70, 0.1m, 60, 300, 180, 5),
        BrewMethod.Siphon => new(method, 15, 60, 25, 0.1m, 200, 1000, 400, 0.1m, 60, 600, 240, 5),
        BrewMethod.Cupping => new(method, 8, 15, 10, 0.1m, 150, 300, 180, 0.1m, 180, 600, 240, 5),
        BrewMethod.ColdBrew => new(method, 50, 300, 100, 0.1m, 500, 3000, 1000, 0.1m, 14400, 86400, 43200, 1800),
        BrewMethod.ColdDrip => new(method, 30, 150, 60, 0.1m, 200, 1500, 500, 0.1m, 7200, 43200, 21600, 600),
        BrewMethod.SteepAndRelease => new(method, 10, 60, 18, 0.1m, 100, 800, 300, 0.1m, 60, 600, 240, 5),
        _ => new(method, 5, 30, 18, 0.1m, 10, 100, 36, 0.1m, 10, 60, 28, 1),
    };

    public static GrindMicronRangeSpec GetGrindSpec(BrewMethod method) => method switch
    {
        BrewMethod.Turkish => new(50, 225, 5, 130),
        BrewMethod.Espresso => new(175, 380, 5, 270),
        BrewMethod.Moka => new(350, 650, 10, 500),
        BrewMethod.V60 => new(400, 700, 10, 550),
        BrewMethod.PourOver => new(400, 925, 25, 660),
        BrewMethod.Aeropress => new(300, 960, 25, 600),
        BrewMethod.Siphon => new(360, 800, 25, 580),
        BrewMethod.Drip => new(290, 900, 25, 600),
        BrewMethod.Cupping => new(450, 850, 25, 650),
        BrewMethod.SteepAndRelease => new(450, 825, 25, 640),
        BrewMethod.FrenchPress => new(690, 1300, 25, 1000),
        BrewMethod.ColdBrew => new(825, 1300, 25, 1100),
        BrewMethod.ColdDrip => new(825, 1300, 25, 1100),
        _ => new(200, 1300, 25, 600),
    };

    private static DrinkValueRangeDefinition GetGrindDefinition(BrewMethod method)
    {
        var grind = GetGrindSpec(method);
        return new(
            new(grind.Min, grind.Max),
            new(40, 1500),
            grind.Default,
            grind.Step,
            "um");
    }

    private static HardValueRanges GetHardRanges(BrewMethod method) => method switch
    {
        BrewMethod.Espresso => new(new(5, 30), new(10, 100), new(10, 60)),
        BrewMethod.Turkish => new(new(3, 20), new(25, 200), new(60, 300)),
        BrewMethod.Moka => new(new(5, 50), new(20, 400), new(30, 600)),
        BrewMethod.Aeropress => new(new(5, 40), new(50, 400), new(30, 600)),
        BrewMethod.Siphon => new(new(10, 60), new(150, 1000), new(60, 600)),
        BrewMethod.V60 => new(new(10, 40), new(100, 600), new(60, 600)),
        BrewMethod.PourOver => new(new(10, 60), new(100, 800), new(60, 900)),
        BrewMethod.Drip => new(new(10, 120), new(100, 1500), new(60, 1200)),
        BrewMethod.Cupping => new(new(5, 20), new(100, 400), new(120, 600)),
        BrewMethod.SteepAndRelease => new(new(10, 60), new(100, 1000), new(60, 900)),
        BrewMethod.FrenchPress => new(new(10, 100), new(100, 1200), new(60, 900)),
        BrewMethod.ColdBrew => new(new(30, 500), new(250, 4000), new(3600, 86400)),
        BrewMethod.ColdDrip => new(new(20, 200), new(150, 2000), new(1800, 43200)),
        _ => new(new(5, 30), new(10, 100), new(10, 60)),
    };

    private sealed record HardValueRanges(
        DrinkValueRange Dose,
        DrinkValueRange Yield,
        DrinkValueRange Time);
}
