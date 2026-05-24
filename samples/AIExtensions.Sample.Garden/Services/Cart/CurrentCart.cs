using System.ComponentModel;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Models;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// Manages the active shopping cart. Registered as a singleton in DI;
/// call <see cref="Clear"/> to empty the cart.
/// Demonstrates: [ExportAIFunction] on instance methods and properties of a DI service.
/// </summary>
public sealed class CurrentCart
{
    private readonly List<ListItem> _items = [];

    private void NotifyChanged() =>
        WeakReferenceMessenger.Default.Send(new CartChangedMessage());

    // Feature: [ExportAIFunction] on an instance property — the generator
    // resolves CurrentCart from DI then reads the getter.
    [ExportAIFunction("show_list")]
    [Description("Returns every item currently in the shopping cart with quantity, unit price, and subtotal.")]
    public IReadOnlyList<ListItem> Items => [.. _items];

    public ListItem? FindItem(string skuOrName)
    {
        var product = ProductCatalog.FindByName(skuOrName);
        if (product is null)
            return null;
        return _items.FirstOrDefault(i => string.Equals(i.Product.Sku, product.Sku, StringComparison.OrdinalIgnoreCase));
    }

    // Feature: [ExportAIFunction] with described parameters — each
    // [Description] becomes part of the JSON schema the AI model sees.
    [ExportAIFunction("add_to_list")]
    [Description("Adds a product to the cart, or increments the quantity if it's already there.")]
    public ListItem AddItem(
        [Description("Product sku or name (e.g., 'seed-tomato' or 'Heirloom Tomato Seeds').")] string skuOrName,
        [Description("How many to add. Defaults to 1.")] int quantity = 1)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");

        var product = ProductCatalog.FindByName(skuOrName)
            ?? throw new InvalidOperationException($"No product matched '{skuOrName}'. Try search_products to browse the catalog.");

        var idx = _items.FindIndex(i => string.Equals(i.Product.Sku, product.Sku, StringComparison.OrdinalIgnoreCase));
        ListItem updated;
        if (idx >= 0)
        {
            updated = _items[idx] with { Quantity = _items[idx].Quantity + quantity };
            _items[idx] = updated;
        }
        else
        {
            updated = new ListItem(product, quantity);
            _items.Add(updated);
        }
        NotifyChanged();
        return updated;
    }

    // Feature: [ExportAIFunction] with a custom tool name that differs from
    // the method name. The AI sees "change_qty" but the method is SetQuantity.
    [ExportAIFunction("change_qty")]
    [Description("Sets a new quantity for an item in the cart. Setting it to 0 removes the item.")]
    public ListItem? SetQuantity(
        [Description("Product sku or name already in the cart.")] string skuOrName,
        [Description("The new quantity. Use 0 to remove the item.")] int quantity)
    {
        var product = ProductCatalog.FindByName(skuOrName)
            ?? throw new InvalidOperationException($"No product matched '{skuOrName}'.");

        if (quantity <= 0)
        {
            RemoveItem(product.Sku);
            return null;
        }

        var idx = _items.FindIndex(i => string.Equals(i.Product.Sku, product.Sku, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return null;

        var updated = _items[idx] with { Quantity = quantity };
        _items[idx] = updated;
        NotifyChanged();
        return updated;
    }

    [ExportAIFunction("remove_from_list")]
    [Description("Removes a product from the cart entirely.")]
    public bool RemoveItem(
        [Description("Product sku or name to remove.")] string skuOrName)
    {
        var product = ProductCatalog.FindByName(skuOrName);
        if (product is null)
            return false;

        var idx = _items.FindIndex(i => string.Equals(i.Product.Sku, product.Sku, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return false;
        _items.RemoveAt(idx);
        NotifyChanged();
        return true;
    }

    [ExportAIFunction("cancel_list", ApprovalRequired = true)]
    [Description("Discards every item from the cart.")]
    public void Clear()
    {
        _items.Clear();
        NotifyChanged();
    }
}
