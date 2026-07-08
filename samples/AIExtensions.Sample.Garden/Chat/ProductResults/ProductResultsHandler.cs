using System.Text.Json;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Maps every product-lookup tool (<c>search_products</c>, <c>list_all_products</c>, <c>get_product</c>)
/// call/result in a turn into a single <see cref="ProductResultsBlock"/>. Registered with
/// <c>options.AddBlockHandler(new ProductResultsHandler())</c>.
/// </summary>
/// <remarks>
/// The pipeline gives active blocks first claim on new content, and a handler that never returns
/// <c>Complete</c> stays active for the whole turn — so this folds all product calls/results into one
/// block. Every other tool (cart, orders, navigation, reviews) is left untouched and flows to the
/// built-in function-invocation handler.
/// </remarks>
public sealed class ProductResultsHandler : ContentBlockHandler<ProductResultsBlock>
{
    private static readonly HashSet<string> ProductTools =
        new(StringComparer.OrdinalIgnoreCase) { "search_products", "list_all_products", "get_product" };

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public override BlockMappingResult<ProductResultsBlock> Handle(
        BlockMappingContext context, ProductResultsBlock state)
    {
        var unhandled = new List<AIContent>();
        foreach (var content in context.UnhandledContents)
            unhandled.Add(content);

        var claimed = false;

        foreach (var content in unhandled)
        {
            switch (content)
            {
                case FunctionCallContent call when ProductTools.Contains(call.Name):
                    context.MarkHandled(call);
                    state.TrackCall(call);
                    claimed = true;
                    break;

                case FunctionResultContent result when state.OwnsResult(result.CallId):
                    context.MarkHandled(result);
                    state.AddProducts(ExtractProducts(result.Result));
                    state.MarkResult(result.CallId);
                    state.AnyResultReceived = true;
                    claimed = true;
                    break;
            }
        }

        if (!claimed)
            return BlockMappingResult<ProductResultsBlock>.Pass();

        if (string.IsNullOrEmpty(state.Id))
        {
            state.Id = Guid.NewGuid().ToString("N");
            return BlockMappingResult<ProductResultsBlock>.Emit(state, state);
        }

        return BlockMappingResult<ProductResultsBlock>.Update(state);
    }

    /// <summary>
    /// Product tools return either a live <see cref="Product"/> / list (when invoked in-process) or JSON
    /// (a string or <see cref="JsonElement"/>). Handle all shapes and ignore a null <c>get_product</c>.
    /// </summary>
    private static IEnumerable<Product> ExtractProducts(object? result)
    {
        switch (result)
        {
            case null:
                return [];
            case Product single:
                return [single];
            case IEnumerable<Product> many:
                return [.. many];
            case string s when !string.IsNullOrWhiteSpace(s):
                return DeserializeJson(s);
            case JsonElement je:
                return DeserializeElement(je);
            default:
                // Unknown live object — round-trip through JSON as a last resort.
                try
                {
                    return DeserializeJson(JsonSerializer.Serialize(result));
                }
                catch
                {
                    return [];
                }
        }
    }

    private static IEnumerable<Product> DeserializeJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return DeserializeElement(doc.RootElement);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<Product> DeserializeElement(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.Array => element.Deserialize<List<Product>>(JsonOptions) ?? [],
                JsonValueKind.Object => element.Deserialize<Product>(JsonOptions) is { } p ? [p] : [],
                _ => [],
            };
        }
        catch
        {
            return [];
        }
    }
}
