namespace AIExtensions.Sample.Garden.Shared;

/// <summary>
/// Stable sample products shared by the server, unit tests, and component previews.
/// </summary>
public static class GardenProductFixtures
{
    public static Product BasilSeeds { get; } = new(
        "seed-basil",
        "Sweet Basil Seeds",
        "Sweet Genovese basil — fast-growing, fragrant, and perfect for pesto. Sow indoors and transplant after the last frost.",
        3.49m,
        "seeds",
        "\ud83c\udf3f",
        "http://localhost:5225/images/products/basil-seeds.png",
        120,
        SeedDetails: new(
            "Sow 1/4 inch deep indoors 4–6 weeks before the last frost; transplant 12 inches apart.",
            "5–10 days",
            "60–75 days"));

    public static Product TomatoSeeds { get; } = new(
        "seed-tomato",
        "Heirloom Tomato Seeds",
        "Heirloom beefsteak tomatoes with rich, old-fashioned flavor. Indeterminate vines crop all season with support.",
        4.25m,
        "seeds",
        "\ud83c\udf45",
        "http://localhost:5225/images/products/tomato-seeds.png",
        80,
        SeedDetails: new(
            "Sow 1/4 inch deep indoors 6–8 weeks before the last frost; transplant 24–36 inches apart.",
            "6–12 days",
            "80–95 days"));

    public static Product TerracottaPot { get; } = new(
        "pot-terracotta",
        "Terracotta Pot",
        "Classic 8-inch terracotta pot with a drainage hole. Breathable clay keeps roots healthy and prevents overwatering.",
        9.99m,
        "tools",
        "\ud83e\udea3",
        "http://localhost:5225/images/products/terracotta-pot.png",
        40);

    public static Product WateringCan { get; } = new(
        "tool-watering",
        "Watering Can (1 gal)",
        "2-gallon galvanized-steel watering can with a removable brass rose for a gentle, even shower.",
        18.50m,
        "tools",
        "\ud83d\udea3",
        "http://localhost:5225/images/products/watering-can.png",
        15,
        Dimensions: new(20.5m, 14m, 8.5m, "inches"),
        ColorOptions: new(
        [
            new("Galvanized Steel", "#A7AFB2"),
            new("Sage Green", "#7E9278"),
            new("Warm Copper", "#B46F4B"),
        ]));

    public static Product PottingSoil { get; } = new(
        "soil-pottingmix",
        "All-Purpose Potting Mix",
        "Organic all-purpose potting mix with coco coir and perlite for excellent drainage and aeration.",
        12.00m,
        "soil",
        "\ud83e\udeb4",
        "http://localhost:5225/images/products/potting-soil.png",
        60);

    public static Product PepperSeeds { get; } = new(
        "seed-pepper",
        "Bell Pepper Seeds",
        "Colorful sweet peppers for containers or garden beds.",
        2.99m,
        "seeds",
        "🫑",
        Quantity: 75,
        SeedDetails: new("Sow indoors 8–10 weeks before the last frost.", "7–14 days", "70–90 days"));

    public static Product SunflowerSeeds { get; } = new(
        "seed-sunflower",
        "Giant Sunflower Seeds",
        "Towering sunflowers that attract pollinators and brighten the garden.",
        3.99m,
        "seeds",
        "🌻",
        Quantity: 90,
        SeedDetails: new("Direct sow 1 inch deep after frost.", "7–10 days", "75–95 days"));

    public static Product LettuceSeeds { get; } = new(
        "seed-lettuce",
        "Mixed Lettuce Seeds",
        "A quick-growing blend of crisp and tender salad greens.",
        2.29m,
        "seeds",
        "🥬",
        Quantity: 110,
        SeedDetails: new("Sow shallowly in cool soil and keep evenly moist.", "4–8 days", "30–45 days"));

    public static Product Compost { get; } = new(
        "soil-compost",
        "Organic Compost (10 lb)",
        "Rich organic compost for improving soil structure and fertility.",
        8.49m,
        "soil",
        "🍂",
        Quantity: 45);

    public static Product Mulch { get; } = new(
        "soil-mulch",
        "Cedar Mulch (2 cu ft)",
        "Aromatic cedar mulch that conserves moisture and suppresses weeds.",
        14.99m,
        "soil",
        "🪵",
        Quantity: 30);

    public static Product TomatoFood { get; } = new(
        "fert-tomato",
        "Tomato Plant Food",
        "Slow-release fertilizer blended for tomatoes and fruiting vegetables.",
        9.99m,
        "fertilizer",
        "💧",
        Quantity: 55);

    public static Product AllPurposeFertilizer { get; } = new(
        "fert-allpurpose",
        "All-Purpose Fertilizer",
        "Balanced plant food for vegetables, flowers, and container gardens.",
        7.99m,
        "fertilizer",
        "🧪",
        Quantity: 65);

    public static Product HandTrowel { get; } = new(
        "tool-trowel",
        "Hand Trowel",
        "A durable stainless-steel trowel with a comfortable grip.",
        12.49m,
        "tools",
        "🛠️",
        Quantity: 25,
        Dimensions: new(3.25m, 13m, 2m, "inches"));

    public static Product Pruners { get; } = new(
        "tool-pruner",
        "Bypass Pruners",
        "Sharp bypass blades for clean cuts on stems and small branches.",
        18.99m,
        "tools",
        "✂️",
        Quantity: 20);

    public static Product Gloves { get; } = new(
        "tool-glove",
        "Garden Gloves (pair)",
        "Breathable, reinforced gloves for planting and pruning.",
        6.99m,
        "tools",
        "🧤",
        Quantity: 50,
        ColorOptions: new(
        [
            new("Sage Green", "#7E9278"),
            new("Clay Red", "#A65C4B"),
            new("Midnight Blue", "#30475E"),
        ]));

    public static Product GardenHose { get; } = new(
        "tool-hose",
        "50 ft Garden Hose",
        "Flexible, kink-resistant hose with durable brass fittings.",
        29.99m,
        "equipment",
        "〰️",
        Quantity: 18);

    public static IReadOnlyList<Product> Catalog { get; } =
    [
        BasilSeeds,
        TomatoSeeds,
        PepperSeeds,
        SunflowerSeeds,
        LettuceSeeds,
        TerracottaPot,
        WateringCan,
        PottingSoil,
        Compost,
        Mulch,
        TomatoFood,
        AllPurposeFertilizer,
        HandTrowel,
        Pruners,
        Gloves,
        GardenHose,
    ];
}
