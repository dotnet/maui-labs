using System.Collections.Generic;

namespace CometBaristaNotes.Models.Enums;

public enum EquipmentType
{
    Machine = 1,
    Grinder = 2,
    Tamper = 3,
    PuckScreen = 4,
    PourOverDripper = 5,
    MokaPot = 6,
    DripMachine = 7,
    Aeropress = 8,
    FrenchPress = 9,
    Other = 99
}

public static class EquipmentTypeExtensions
{
    public static IReadOnlyList<BrewMethod> CompatibleMethods(this EquipmentType type) => type switch
    {
        EquipmentType.Machine => new[] { BrewMethod.Espresso },
        EquipmentType.PourOverDripper => new[] { BrewMethod.PourOver, BrewMethod.V60 },
        EquipmentType.MokaPot => new[] { BrewMethod.Moka },
        EquipmentType.DripMachine => new[] { BrewMethod.Drip },
        EquipmentType.Aeropress => new[] { BrewMethod.Aeropress },
        EquipmentType.FrenchPress => new[] { BrewMethod.FrenchPress },
        _ => BrewMethodExtensions.All
    };
}
