using System.Text.Json;
using AIExtensions.Sample.Garden.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class AdaptiveRegionRendererTests
{
    [Fact]
    public void Render_RenamedSemanticNodes_ReusesNativeViews()
    {
        var registry = new GenerativeUiRegistry()
            .AddComponent<GridComponent>(new ComponentDescriptor
            {
                Alias = "ProductHero",
                Description = "Product identity.",
                DataContract = nameof(Product),
                RequiredBindings = ["name"],
                Variants = ["default", "compact"],
            });
        using var services = new ServiceCollection().BuildServiceProvider();
        using var session = new AdaptiveSurfaceSession(
            "product:first",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());
        UiObjectBuilder.Replace(
            session.StateRoot["product"],
            JsonSerializer.SerializeToElement(
                GardenProductFixtures.WateringCan,
                GardenJsonContext.Default.Product));
        var host = new AdaptiveRegionView(AdaptiveCompositionTestCatalog.Region);
        host.Attach(session);
        var renderer = new AdaptiveRegionRenderer(registry, services);

        renderer.Render(AdaptiveCompositionTestCatalog.StandardLayout(), session);
        var root = session.GetMountedView("root");
        var component = session.GetMountedView("hero");

        var diff = renderer.Render(
            AdaptiveCompositionTestCatalog.StandardLayout(
                revision: 2,
                rootId: "renamed-root",
                componentId: "renamed-hero"),
            session);

        Assert.Same(root, session.GetMountedView("renamed-root"));
        Assert.Same(component, session.GetMountedView("renamed-hero"));
        Assert.Same(root, host.Content);
        Assert.Empty(diff.Added);
        Assert.Equal(["renamed-root", "renamed-hero"], diff.Reused);
        var gridComponent = Assert.IsType<GridComponent>(component);
        Assert.Single(gridComponent.Children);
        Assert.Equal("default", gridComponent.Variant);
    }

    [Fact]
    public void Render_RemovedOptionalRegion_ClearsStaleHost()
    {
        var registry = new GenerativeUiRegistry()
            .AddComponent<TestComponent>(new ComponentDescriptor
            {
                Alias = "ProductHero",
                Description = "Product identity.",
                DataContract = nameof(Product),
                RequiredBindings = ["name"],
                Variants = ["default"],
            });
        using var services = new ServiceCollection().BuildServiceProvider();
        using var session = new AdaptiveSurfaceSession(
            "product:first",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());
        UiObjectBuilder.Replace(
            session.StateRoot["product"],
            JsonSerializer.SerializeToElement(
                GardenProductFixtures.WateringCan,
                GardenJsonContext.Default.Product));
        var primary = new AdaptiveRegionView("Main");
        var optional = new AdaptiveRegionView("Aside");
        primary.Attach(session);
        optional.Attach(session);
        var renderer = new AdaptiveRegionRenderer(registry, services);
        var standard = AdaptiveCompositionTestCatalog.StandardLayout();
        renderer.Render(standard with
        {
            Regions =
            [
                standard.Regions[0],
                new() { Region = "Aside", RootNodeId = "aside-root" },
            ],
            Nodes =
            [
                .. standard.Nodes,
                standard.Nodes[0] with { Id = "aside-root" },
                standard.Nodes[1] with
                {
                    Id = "aside-component",
                    ParentId = "aside-root",
                },
            ],
        }, session);

        renderer.Render(standard with { Revision = 2 }, session);

        Assert.NotNull(primary.Content);
        Assert.Null(optional.Content);
    }

    public sealed class TestComponent : ContentView, ICompositionComponent
    {
        public string? Variant { get; private set; }

        public void ApplyVariant(string? variant) => Variant = variant;

        public void Detach() => BindingContext = null;
    }

    public sealed class GridComponent : Grid, ICompositionComponent
    {
        public GridComponent() => Children.Add(new Label { Text = "Authored content" });

        public string? Variant { get; private set; }

        public void ApplyVariant(string? variant) => Variant = variant;

        public void Detach() => BindingContext = null;
    }
}
