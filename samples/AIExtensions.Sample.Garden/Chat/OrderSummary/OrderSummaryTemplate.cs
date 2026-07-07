using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders an <see cref="OrderSummaryBlock"/> with <see cref="OrderSummaryView"/>.
/// </summary>
/// <remarks>
/// Because <see cref="OrderSummaryBlock"/> derives from <c>FunctionInvocationContentBlock</c>, the generic
/// tool template (<c>FunctionInvocationTemplate</c> → <c>GardenToolView</c>) also matches it. That generic
/// template self-demotes to a low priority when it is not scoped to a tool, so this template — which matches
/// the concrete <see cref="OrderSummaryBlock"/> at the default priority — wins automatically. The tie-break
/// is therefore encapsulated here in one typed class, replacing the <c>Priority="1"</c> magic number that a
/// declarative <c>GenericContentTemplate</c> needed in XAML.
/// </remarks>
public sealed class OrderSummaryTemplate : ContentTemplate
{
    public OrderSummaryTemplate() => ViewType = typeof(OrderSummaryView);

    public override bool When(ContentContext context) => context.Block is OrderSummaryBlock;
}
