using System.Text;
using System.Text.Json;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>One product-lookup tool call folded into the aggregate block, kept for inspection.</summary>
public sealed record TrackedCall(string CallId, string ToolName, string Arguments)
{
    /// <summary><see langword="true"/> once this call's result has been applied to the block.</summary>
    public bool HasResult { get; internal set; }
}

/// <summary>
/// Aggregates every product-lookup tool call in a single turn into one block, so a search that returns
/// several items — or several lookups in the same turn (e.g. "compare the tomato and basil seeds") —
/// renders as one grouped product view (a carousel) instead of a separate plain tool card per call.
/// </summary>
/// <remarks>
/// Demonstrates <b>many-to-one</b> block mapping. The <see cref="ProductResultsHandler"/> emits this block
/// on the first product call and folds each additional call/result into <see cref="Products"/>. The view
/// adapts: exactly one product renders as a single detail card, more than one as a horizontal carousel,
/// and none (a not-found lookup) as a friendly empty state. It also keeps the constituent <see cref="Calls"/>
/// (tool name + arguments) so a raw block inspector can show exactly which tool calls made up the block.
/// </remarks>
public sealed class ProductResultsBlock : ContentBlock
{
    private readonly List<Product> _products = [];
    private readonly List<TrackedCall> _calls = [];

    /// <summary>The distinct products discovered this turn, in call/result order.</summary>
    public IReadOnlyList<Product> Products => _products;

    /// <summary>The product-lookup tool calls that were folded into this block (name + arguments).</summary>
    public IReadOnlyList<TrackedCall> Calls => _calls;

    /// <summary><see langword="true"/> once at least one product tool result has been applied.</summary>
    public bool AnyResultReceived { get; internal set; }

    /// <summary>Records a product-tool call (name + arguments) so its result can be matched later.</summary>
    internal void TrackCall(FunctionCallContent call) =>
        _calls.Add(new TrackedCall(call.CallId, call.Name, FormatArguments(call.Arguments)));

    /// <summary>Whether the given result id belongs to a product call this block is tracking.</summary>
    internal bool OwnsResult(string callId) => _calls.Any(c => c.CallId == callId);

    /// <summary>Marks the tracked call for <paramref name="callId"/> as resolved.</summary>
    internal void MarkResult(string callId)
    {
        var call = _calls.FirstOrDefault(c => c.CallId == callId);
        if (call is not null)
            call.HasResult = true;
    }

    private static string FormatArguments(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0)
            return "()";

        var sb = new StringBuilder("(");
        foreach (var (key, value) in args)
        {
            if (sb.Length > 1)
                sb.Append(", ");
            var text = value switch
            {
                null => "null",
                JsonElement je => je.ToString(),
                _ => value.ToString(),
            };
            sb.Append(key).Append('=').Append(text);
        }
        return sb.Append(')').ToString();
    }

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
