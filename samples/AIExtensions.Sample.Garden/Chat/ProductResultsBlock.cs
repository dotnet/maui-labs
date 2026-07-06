using System.Text;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Aggregates every product-lookup tool call in a single turn into one block, so a search that returns
/// several items — or several lookups in the same turn (e.g. "compare the tomato and basil seeds") —
/// renders as one grouped product view (a carousel) instead of a separate plain tool card per call.
/// </summary>
/// <remarks>
/// Demonstrates <b>many-to-one</b> block mapping. The <see cref="ProductResultsHandler"/> emits this block
/// on the first product call and folds each additional call/result into <see cref="Products"/>. The view
/// adapts: exactly one product renders as a single detail card, more than one as a horizontal carousel,
/// and none (a not-found lookup) as a friendly empty state. Because it is a plain <see cref="ContentBlock"/>,
/// the "raw" template set falls back to the default view, which shows <see cref="ToString"/>.
/// </remarks>
public sealed class ProductResultsBlock : ContentBlock
{
    private readonly List<Product> _products = [];
    private readonly HashSet<string> _callIds = new(StringComparer.Ordinal);

    /// <summary>The distinct products discovered this turn, in call/result order.</summary>
    public IReadOnlyList<Product> Products => _products;

    /// <summary><see langword="true"/> once at least one product tool result has been applied.</summary>
    public bool AnyResultReceived { get; internal set; }

    /// <summary>Records that a product-tool call was seen so its result can be matched later.</summary>
    internal void TrackCall(string callId) => _callIds.Add(callId);

    /// <summary>Whether the given result id belongs to a product call this block is tracking.</summary>
    internal bool OwnsResult(string callId) => _callIds.Contains(callId);

    /// <summary>Adds products, de-duplicating by sku while preserving discovery order.</summary>
    internal void AddProducts(IEnumerable<Product> products)
    {
        foreach (var product in products)
        {
            if (_products.Any(p => string.Equals(p.Sku, product.Sku, StringComparison.OrdinalIgnoreCase)))
                continue;
            _products.Add(product);
        }
    }

    public override string ToString()
    {
        if (_products.Count == 0)
            return AnyResultReceived ? "No matching products." : "Looking up products…";

        var sb = new StringBuilder();
        sb.Append("Products (").Append(_products.Count).Append(')');
        foreach (var product in _products)
        {
            sb.AppendLine();
            sb.Append("• ").Append(product.Name).Append(" — $").Append(product.Price.ToString("0.00"));
        }
        return sb.ToString();
    }
}
