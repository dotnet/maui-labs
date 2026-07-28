using System.ComponentModel;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// Persists completed orders and supports checkout and reorder flows.
///
/// Demonstrates: placing [ExportAIFunction] on an interface. The source
/// generator targets whatever implementation is registered in DI, so
/// swapping InMemoryOrderArchive for PreferencesOrderArchive (or any
/// other implementation) requires zero changes to the AI tool wiring.
/// </summary>
public interface IOrderArchive
{
    /// <summary>
    /// Every past order, newest first.
    /// Demonstrates: [ExportAIFunction] on an interface property.
    /// </summary>
    [ExportAIFunction("list_past_orders")]
    [Description("Lists every past order, newest first.")]
    IReadOnlyList<Order> Orders { get; }

    /// <summary>
    /// Looks up a single order by its id.
    /// </summary>
    [ExportAIFunction("find_order")]
    [Description("Looks up a single past order by its id.")]
    Order? FindOrder(
        [Description("The order id (from list_past_orders).")] string orderId);

    /// <summary>
    /// Finalizes the current cart as an order and clears the cart.
    /// Demonstrates: [ExportAIFunction] with ApprovalRequired on an
    /// interface method, and [FromServices] to inject a sibling DI
    /// service that the AI model never sees as a parameter.
    /// </summary>
    [ExportAIFunction("checkout_list", ApprovalRequired = true)]
    [Description("Checks out the current cart as a finalized order and clears the cart.")]
    Order Checkout([FromServices] CurrentCart cart);

    /// <summary>
    /// Copies every item from a past order into the current cart.
    /// Demonstrates: [FromServices] parameter injection on an interface
    /// method — the generator wires up CurrentCart from DI automatically.
    /// </summary>
    [ExportAIFunction("reorder")]
    [Description("Copies every item from a past order into the current cart.")]
    void Reorder(
        [Description("The id of the past order to copy (from list_past_orders).")] string orderId,
        [FromServices] CurrentCart cart);

    /// <summary>
    /// Removes all past orders from the archive.
    /// </summary>
    [ExportAIFunction("clear_past_orders", ApprovalRequired = true)]
    [Description("Removes all past orders from the archive. This action cannot be undone.")]
    void Clear();
}
