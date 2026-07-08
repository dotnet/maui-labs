using System.Text;
using System.Text.Json;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>A single <c>Label: Value</c> row shown in the block-preview inspector.</summary>
public sealed record PreviewField(string Label, string Value);

/// <summary>
/// A diagnostic view that visualizes the RAW <see cref="ContentBlock"/> the pipeline produced,
/// regardless of type: a heading with the block's friendly type name plus a list of its meaningful
/// values. Used by <see cref="BlockPreviewTemplate"/> as the single "all blocks" renderer, so you can
/// inspect exactly what the handlers built — a function call's args/result, the products an aggregate
/// block folded together, etc. — independent of the designed views.
/// </summary>
public partial class BlockPreviewView : ContentContextView
{
    public static readonly BindableProperty TypeNameProperty =
        BindableProperty.Create(nameof(TypeName), typeof(string), typeof(BlockPreviewView), string.Empty);

    public static readonly BindableProperty FieldsProperty =
        BindableProperty.Create(nameof(Fields), typeof(IReadOnlyList<PreviewField>), typeof(BlockPreviewView));

    public BlockPreviewView()
    {
        InitializeComponent();
    }

    /// <summary>The block's friendly type name, shown as the card heading.</summary>
    public string TypeName
    {
        get => (string)GetValue(TypeNameProperty);
        set => SetValue(TypeNameProperty, value);
    }

    /// <summary>The extracted <c>Label: Value</c> rows for the block.</summary>
    public IReadOnlyList<PreviewField>? Fields
    {
        get => (IReadOnlyList<PreviewField>?)GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        var block = ContentContext?.Block;
        if (block is null)
        {
            TypeName = string.Empty;
            Fields = [];
            return;
        }

        TypeName = block.GetType().Name;
        Fields = [.. BuildFields(block)];
    }

    private static IEnumerable<PreviewField> BuildFields(ContentBlock block)
    {
        switch (block)
        {
            // OrderSummaryBlock is-a FunctionInvocationContentBlock, so match it first.
            case OrderSummaryBlock order:
                yield return new("Kind", "1:1 tool block (find_order)");
                yield return new("OrderId", string.IsNullOrEmpty(order.OrderId) ? "(none)" : order.OrderId);
                yield return new("Order", order.Order is { } o
                    ? $"{o.Id} — {o.Items.Count} item(s), {o.Total:C}"
                    : order.HasResult ? "(not found)" : "(pending)");
                break;

            case ProductResultsBlock products:
                yield return new("Kind", "N:1 aggregate block");
                yield return new("Calls", products.Calls.Count.ToString());
                foreach (var call in products.Calls)
                    yield return new("• Call", $"{call.ToolName}{call.Arguments}{(call.HasResult ? "" : " (pending)")}");
                yield return new("Products", products.Products.Count.ToString());
                foreach (var p in products.Products)
                    yield return new("• Product", $"{p.Name} — {p.Price:C}");
                if (products.Products.Count == 0)
                    yield return new("State", products.AnyResultReceived ? "no matches" : "looking up…");
                break;

            case FunctionInvocationContentBlock fn:
                yield return new("Kind", "Function call");
                yield return new("Tool", fn.ToolName ?? "(none)");
                yield return new("Args", FormatArguments(fn.Arguments));
                yield return new("Result", fn.HasResult ? Format(fn.Result?.Result) : "(pending)");
                break;

            case TextContentBlock text:
                yield return new("Kind", "Text");
                yield return new("RawText", text.RawText);
                break;

            default:
                yield return new("Value", block.ToString() ?? "(empty)");
                break;
        }

        yield return new("Id", string.IsNullOrEmpty(block.Id) ? "(none)" : block.Id);
        yield return new("Role", block.Role?.Value ?? "(none)");
        yield return new("State", block.LifecycleState.ToString());
    }

    private static string FormatArguments(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0)
            return "(none)";

        var sb = new StringBuilder();
        foreach (var (key, value) in args)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(key).Append('=').Append(Format(value));
        }
        return sb.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => "(null)",
        string s => s,
        JsonElement je => je.ToString(),
        _ => value.ToString() ?? "(null)",
    };
}
