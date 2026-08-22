using System.Text.Json;
using AIExtensions.Sample.Garden.Shared;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi.Tests;

public sealed class ProductFacetRoundTripTests
{
    [Fact]
    public void WateringCan_RoundTripsDimensionsAndColors()
    {
        var product = RoundTrip(GardenProductFixtures.WateringCan);

        Assert.Null(product.SeedDetails);
        Assert.Equal(new Dimensions(20.5m, 14m, 8.5m, "inches"), product.Dimensions);
        Assert.Collection(
            Assert.IsType<ColorOptions>(product.ColorOptions).Options,
            color => Assert.Equal(new ProductColor("Galvanized Steel", "#A7AFB2"), color),
            color => Assert.Equal(new ProductColor("Sage Green", "#7E9278"), color),
            color => Assert.Equal(new ProductColor("Warm Copper", "#B46F4B"), color));
    }

    [Fact]
    public void BasilSeeds_RoundTripsGrowingDetailsOnly()
    {
        var product = RoundTrip(GardenProductFixtures.BasilSeeds);

        Assert.NotNull(product.SeedDetails);
        Assert.Contains("1/4 inch", product.SeedDetails.PlantingInstructions, StringComparison.Ordinal);
        Assert.Equal("5–10 days", product.SeedDetails.GerminationWindow);
        Assert.Equal("60–75 days", product.SeedDetails.HarvestWindow);
        Assert.Null(product.Dimensions);
        Assert.Null(product.ColorOptions);
    }

    private static Product RoundTrip(Product product)
    {
        var json = JsonSerializer.Serialize(product, GardenJsonContext.Default.Product);
        return JsonSerializer.Deserialize(json, GardenJsonContext.Default.Product)
            ?? throw new InvalidOperationException("Product did not deserialize.");
    }
}
