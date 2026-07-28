using System.Collections.ObjectModel;
using System.ComponentModel;
using AIExtensions.Sample.Garden.Models;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Owns the product catalog data and the add-to-cart action.
/// </summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    private readonly CurrentCart _currentCart;

    public CatalogViewModel(CurrentCart currentCart)
    {
        _currentCart = currentCart;

        var groups = ProductCatalog.All
            .GroupBy(p => p.Category)
            .Select(g =>
            {
                var group = new CatalogGroupViewModel(g.Key);
                group.AddRange(g.Select(p => new CatalogItemViewModel(p)));
                return group;
            })
            .ToList();

        Products = new(groups.SelectMany(g => g));
        Groups = groups;
    }

    public ObservableCollection<CatalogItemViewModel> Products { get; }

    public IReadOnlyList<CatalogGroupViewModel> Groups { get; }

    [ExportAIFunction("recommend_bundle")]
    [Description("Build a random starter bundle with seed packs, soil, fertilizer, and one tool or equipment item. Returns the bundle with quantities and total price but does not add anything to the cart.")]
    public string RecommendBundle(
        [Description("Optional focus like 'tomato', 'basil', 'flowers', or 'starter'. Leave blank for a surprise bundle.")]
        string? focus = null)
    {
        static bool MatchesFocus(CatalogItemViewModel product, string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            (product.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
             product.Sku.Contains(value, StringComparison.OrdinalIgnoreCase) ||
             product.Category.Contains(value, StringComparison.OrdinalIgnoreCase));

        static CatalogItemViewModel PickRandom(List<CatalogItemViewModel> products, string category) =>
            products.Count > 0
                ? products[Random.Shared.Next(products.Count)]
                : throw new InvalidOperationException($"No products found for category '{category}'.");

        var seedOptions = Products.Where(p => p.Category == "Seeds").ToList();
        var focusedSeeds = seedOptions.Where(p => MatchesFocus(p, focus)).ToList();
        var seed = PickRandom(focusedSeeds.Count > 0 ? focusedSeeds : seedOptions, "Seeds");

        var soil = PickRandom([.. Products.Where(p => p.Category == "Soil")], "Soil");
        var fertilizer = PickRandom([.. Products.Where(p => p.Category == "Fertilizer")], "Fertilizer");
        var gear = PickRandom([.. Products.Where(p => p.Category is "Tools" or "Equipment")], "Tools/Equipment");
        var seedPacks = Random.Shared.Next(2, 6);

        var lines = new[]
        {
            (Product: seed.Name, Qty: seedPacks, Unit: seed.Price, Subtotal: seed.Price * seedPacks),
            (Product: soil.Name, Qty: 1, Unit: soil.Price, Subtotal: soil.Price),
            (Product: fertilizer.Name, Qty: 1, Unit: fertilizer.Price, Subtotal: fertilizer.Price),
            (Product: gear.Name, Qty: 1, Unit: gear.Price, Subtotal: gear.Price)
        };

        var total = lines.Sum(l => l.Subtotal);
        var label = string.IsNullOrWhiteSpace(focus) ? "starter" : focus.Trim();

        return
            $"{label} bundle recommendation: " +
            string.Join(", ", lines.Select(l => $"{l.Qty}x {l.Product} ({l.Subtotal:C})")) +
            $". Total: {total:C}.";
    }

    [RelayCommand]
    public void AddToCart(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return;

        _currentCart.AddItem(sku);
    }
}
