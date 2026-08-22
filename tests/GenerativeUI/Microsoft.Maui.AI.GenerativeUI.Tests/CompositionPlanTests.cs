using System.Text.Json;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class CompositionPlanTests
{
    [Fact]
    public void Deserialize_ValidPlan_PreservesTypedStructure()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "planId": "watering-can-detail",
              "revision": 2,
              "scaffold": "ProductDetail",
              "title": "Watering Can dimensions",
              "sections": [
                {
                  "id": "dimensions",
                  "slot": "Primary",
                  "component": "DimensionsPanel",
                  "dataPath": "product",
                  "variant": "default",
                  "priority": 100,
                  "reason": "The user asked how big it is."
                }
              ]
            }
            """;

        var plan = JsonSerializer.Deserialize<CompositionPlan>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(plan);
        Assert.Equal(CompositionPlan.CurrentSchemaVersion, plan.SchemaVersion);
        Assert.Equal("watering-can-detail", plan.PlanId);
        Assert.Equal(2, plan.Revision);
        var section = Assert.Single(plan.Sections);
        Assert.Equal(CompositionSlot.Primary, section.Slot);
        Assert.Equal("DimensionsPanel", section.Component);
        Assert.Equal("product", section.DataPath);
        Assert.Equal(100, section.Priority);
    }
}
