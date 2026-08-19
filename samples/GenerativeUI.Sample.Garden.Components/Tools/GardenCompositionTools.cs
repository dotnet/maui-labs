using System.ComponentModel;
using System.Text.Json;
using GenerativeUI.Sample.Garden.Components;
using GenerativeUI.Sample.Garden.Shared;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.ApplicationModel;
using CanvasState = Microsoft.Maui.AI.GenerativeUI.Canvas.CanvasState;

namespace GenerativeUI.Sample.Garden.Tools;

/// <summary>Typed Garden adapter over the generic native component composer.</summary>
public sealed class GardenCompositionTools(
    CanvasState canvas,
    CompositionSessionState session,
    ComponentComposer composer,
    CompositionPlanRenderer renderer)
{
    private const string ProductDataPath = "product";

    [ExportAIFunction("compose_product_detail")]
    [Description(
        "Compose the active product detail from registered native components. On the first call for a product, " +
        "supply the complete typed Product returned by read_api. On follow-up requests about that same product, " +
        "omit product to reuse the existing state and plan. This is read-only and never submits reviews or writes data.")]
    public async Task<string> ComposeProductDetailAsync(
        [Description("The user's current product-detail intent, such as 'show it', 'how big is it?', or 'what colors?'.")]
        string intent,
        [Description("Complete product data on the first call for a product; omit on follow-ups for the active product.")]
        Product? product = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return "Error: intent is required.";

        if (product is not null)
        {
            var element = JsonSerializer.SerializeToElement(product, GardenJsonContext.Default.Product);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var currentSku = UiObjectPath.ResolveDotted(canvas.StateRoot, $"{ProductDataPath}.sku")?.AsString();
                if (!string.Equals(currentSku, product.Sku, StringComparison.OrdinalIgnoreCase))
                    session.Reset();
                UiObjectBuilder.Replace(canvas.StateRoot[ProductDataPath], element);
            }).ConfigureAwait(false);
        }

        var activeProduct = UiObjectPath.ResolveDotted(canvas.StateRoot, ProductDataPath);
        if (activeProduct is null || !UiObjectPath.HasData(activeProduct))
            return "Error: no active product. Supply the complete product on the first compose_product_detail call.";

        var title = UiObjectPath.ResolveDotted(activeProduct, "name")?.AsString() ?? "Product";
        var composition = await composer.ComposeAsync(
            new(
                intent,
                GardenComponentCatalog.ProductDetailScaffoldAlias,
                nameof(Product),
                ProductDataPath,
                title),
            canvas.StateRoot,
            cancellationToken).ConfigureAwait(false);

        var diff = await MainThread.InvokeOnMainThreadAsync(() =>
            renderer.Render(composition.Plan, canvas.StateRoot)).ConfigureAwait(false);

        return JsonSerializer.Serialize(
            new
            {
                plan = composition.Plan,
                source = composition.Source.ToString(),
                correctionCount = composition.CorrectionCount,
                render = diff,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
