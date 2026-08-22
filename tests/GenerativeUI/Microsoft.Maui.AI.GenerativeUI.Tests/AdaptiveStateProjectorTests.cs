using AIExtensions.Sample.Garden.Shared;
using System.Text.Json;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class AdaptiveStateProjectorTests
{
    [Fact]
    public void Project_ReplacesTypedSnapshotWhilePreservingKeyedCollectionIdentity()
    {
        var session = new AdaptiveSurfaceSession(
            "catalog:one",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());
        var projector = new AdaptiveStateProjector();
        var initial = new List<Product>
        {
            GardenProductFixtures.WateringCan,
            GardenProductFixtures.BasilSeeds,
        };
        projector.Project(session, "catalog.products", initial, GardenJsonContext.Default.ListProduct);
        var collection = session.StateRoot["catalog"]["products"].Children;
        var wateringCan = collection[0];
        var basil = collection[1];

        var reordered = new List<Product>
        {
            GardenProductFixtures.BasilSeeds with { Quantity = 99 },
            GardenProductFixtures.WateringCan,
        };
        projector.Project(session, "catalog.products", reordered, GardenJsonContext.Default.ListProduct);

        Assert.Same(collection, session.StateRoot["catalog"]["products"].Children);
        Assert.Same(basil, collection[0]);
        Assert.Same(wateringCan, collection[1]);
        Assert.Equal(99, collection[0]["quantity"].AsNumber());

        projector.Project(
            session,
            "catalog.products",
            new List<Product>
            {
                GardenProductFixtures.TerracottaPot,
                reordered[0],
                reordered[1],
            },
            GardenJsonContext.Default.ListProduct);

        Assert.Same(basil, collection[1]);
        Assert.Same(wateringCan, collection[2]);
        Assert.Equal(GardenProductFixtures.TerracottaPot.Sku, collection[0]["sku"].AsString());
        Assert.Equal(3, session.StateVersion);
    }

    [Fact]
    public void Project_NullPrimaryKey_FallsBackToNextSemanticKey()
    {
        using var session = new AdaptiveSurfaceSession(
            "catalog:one",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());
        var projector = new AdaptiveStateProjector();
        projector.ProjectJson(
            session,
            "items",
            JsonDocument.Parse("""[{"id":null,"key":"x","value":1}]""").RootElement);
        var item = session.StateRoot["items"].Children[0];

        projector.ProjectJson(
            session,
            "items",
            JsonDocument.Parse("""[{"id":null,"key":"x","value":2}]""").RootElement);

        Assert.Same(item, session.StateRoot["items"].Children[0]);
        Assert.Equal(2, item["value"].AsNumber());
    }
}
