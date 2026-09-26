using System;
using System.Collections.Generic;

namespace CometBaristaNotes.Models.Enums;

/// <summary>
/// A brewing method for preparing coffee.
/// </summary>
public enum BrewMethod
{
    Espresso = 1,
    PourOver = 2,
    Moka = 3,
    Drip = 4,
    Aeropress = 5,
    FrenchPress = 6,
    Turkish = 7,
    V60 = 8,
    Siphon = 9,
    Cupping = 10,
    ColdBrew = 11,
    ColdDrip = 12,
    SteepAndRelease = 13,
}

public static class BrewMethodExtensions
{
    public static string DisplayName(this BrewMethod method) => method switch
    {
        BrewMethod.Espresso => "Espresso",
        BrewMethod.PourOver => "Pour Over",
        BrewMethod.Moka => "Moka",
        BrewMethod.Drip => "Drip",
        BrewMethod.Aeropress => "Aeropress",
        BrewMethod.FrenchPress => "French Press",
        BrewMethod.Turkish => "Turkish",
        BrewMethod.V60 => "V60",
        BrewMethod.Siphon => "Siphon",
        BrewMethod.Cupping => "Cupping",
        BrewMethod.ColdBrew => "Cold Brew",
        BrewMethod.ColdDrip => "Cold Drip",
        BrewMethod.SteepAndRelease => "Steep & Release",
        _ => method.ToString()
    };

    public static string ShortName(this BrewMethod method) => method switch
    {
        BrewMethod.Espresso => "Esp",
        BrewMethod.PourOver => "Pour",
        BrewMethod.Moka => "Moka",
        BrewMethod.Drip => "Drip",
        BrewMethod.Aeropress => "Aero",
        BrewMethod.FrenchPress => "Press",
        BrewMethod.Turkish => "Trk",
        BrewMethod.V60 => "V60",
        BrewMethod.Siphon => "Siph",
        BrewMethod.Cupping => "Cup",
        BrewMethod.ColdBrew => "CldB",
        BrewMethod.ColdDrip => "CldD",
        BrewMethod.SteepAndRelease => "Steep",
        _ => method.ToString()
    };

    public static BrewMethodProfile Profile(this BrewMethod method)
        => BrewMethodValueRangeCatalog.GetProfile(method);

    public static IReadOnlyList<string> DrinkTypesFor(this BrewMethod method) => method switch
    {
        BrewMethod.Espresso       => new[] { "Espresso", "Ristretto", "Lungo", "Americano", "Macchiato", "Cortado", "Flat White", "Cappuccino", "Latte", "Mocha" },
        BrewMethod.V60            => new[] { "Pour Over" },
        BrewMethod.PourOver       => new[] { "Pour Over" },
        BrewMethod.Drip           => new[] { "Drip" },
        BrewMethod.Aeropress      => new[] { "Aeropress" },
        BrewMethod.FrenchPress    => new[] { "French Press" },
        BrewMethod.Moka           => new[] { "Moka" },
        BrewMethod.Turkish        => new[] { "Turkish" },
        BrewMethod.Siphon         => new[] { "Siphon" },
        BrewMethod.Cupping        => new[] { "Cupping" },
        BrewMethod.ColdBrew       => new[] { "Cold Brew" },
        BrewMethod.ColdDrip       => new[] { "Cold Drip" },
        BrewMethod.SteepAndRelease => new[] { "Steep & Release" },
        _ => new[] { method.DisplayName() }
    };

    public static IReadOnlyList<BrewMethod> All { get; } = new[]
    {
        BrewMethod.Turkish,
        BrewMethod.Espresso,
        BrewMethod.Moka,
        BrewMethod.V60,
        BrewMethod.PourOver,
        BrewMethod.Aeropress,
        BrewMethod.Siphon,
        BrewMethod.Drip,
        BrewMethod.Cupping,
        BrewMethod.SteepAndRelease,
        BrewMethod.FrenchPress,
        BrewMethod.ColdBrew,
        BrewMethod.ColdDrip,
    };

    public static GrindMicronRangeSpec GrindMicronRange(this BrewMethod method)
        => BrewMethodValueRangeCatalog.GetGrindSpec(method);
}

public record GrindMicronRangeSpec(int Min, int Max, int Step, int Default);

public record BrewMethodProfile(
    BrewMethod Method,
    decimal DoseMin, decimal DoseMax, decimal DoseDefault, decimal DoseStep,
    decimal OutputMin, decimal OutputMax, decimal OutputDefault, decimal OutputStep,
    int TimeMin, int TimeMax, int TimeDefault, int TimeStep)
{
    public decimal ClampDose(decimal value) => Math.Max(DoseMin, Math.Min(DoseMax, value));
    public decimal ClampOutput(decimal value) => Math.Max(OutputMin, Math.Min(OutputMax, value));
    public decimal ClampTime(decimal value) => Math.Max(TimeMin, Math.Min(TimeMax, value));
}
