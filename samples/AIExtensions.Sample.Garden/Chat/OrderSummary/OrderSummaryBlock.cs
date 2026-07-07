using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// A single <c>find_order</c> tool call projected into one strongly-typed block: the looked-up
/// <see cref="OrderId"/> (from the call arguments) and the resolved <see cref="Models.Order"/> (from the
/// result). Rendered by <c>OrderSummaryView</c> as a receipt-style order card.
/// </summary>
/// <remarks>
/// This is the <b>simplest</b> mapping shape in the sample — a textbook <b>one-to-one</b> tool→block: one
/// tool call produces exactly one block. The mechanical call/result correlation lives in
/// <see cref="OrderSummaryHandler"/>, which is precisely the code a <c>[ToolBlock]</c> source generator
/// would emit. When that generator lands, delete <see cref="OrderSummaryHandler"/> and its registration,
/// mark this class <c>partial</c>, and annotate it — the block and its view stay unchanged:
/// <code>
/// [ToolBlock("find_order")]
/// public partial class OrderSummaryBlock : FunctionInvocationContentBlock
/// {
///     [ToolParameter] public string OrderId { get; set; }
///     [ToolResult]    public Order?  Order   { get; set; }
/// }
/// </code>
/// Contrast this with <see cref="ProductResultsBlock"/> (many calls aggregated into one block) and
/// <see cref="GardenFormattedTextBlock"/> (assistant text projected into a block) — those advanced shapes
/// are outside a 1:1 generator's scope and stay hand-written.
/// </remarks>
public sealed class OrderSummaryBlock : FunctionInvocationContentBlock
{
    /// <summary>The order id the model looked up (from the <c>find_order</c> call arguments).</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>The resolved order, or <see langword="null"/> when no order matched the id.</summary>
    public Order? Order { get; set; }

    public override string ToString() =>
        Order is { } order
            ? $"Order {order.Id} — {order.Items.Count} item(s), {order.Total:C}"
            : HasResult ? $"No order found for '{OrderId}'." : "Looking up order…";
}
