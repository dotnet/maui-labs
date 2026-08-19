namespace GenerativeUI.Sample.Garden.Shared;

/// <summary>
/// Stable sample products shared by the server, unit tests, and component previews.
/// </summary>
public static class GardenProductFixtures
{
    public static Product BasilSeeds { get; } = new(
        "basil-seeds",
        "Basil Seeds",
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
        "tomato-seeds",
        "Tomato Seeds",
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
        "terracotta-pot",
        "Terracotta Pot",
        "Classic 8-inch terracotta pot with a drainage hole. Breathable clay keeps roots healthy and prevents overwatering.",
        9.99m,
        "tools",
        "\ud83e\udea3",
        "http://localhost:5225/images/products/terracotta-pot.png",
        40);

    public static Product WateringCan { get; } = new(
        "watering-can",
        "Watering Can",
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
        "potting-soil",
        "Potting Soil",
        "Organic all-purpose potting mix with coco coir and perlite for excellent drainage and aeration.",
        12.00m,
        "soil",
        "\ud83e\udeb4",
        "http://localhost:5225/images/products/potting-soil.png",
        60);

    public static IReadOnlyList<Product> Catalog { get; } =
    [
        BasilSeeds,
        TomatoSeeds,
        TerracottaPot,
        WateringCan,
        PottingSoil,
    ];
}
