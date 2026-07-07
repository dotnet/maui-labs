using System.Text.Json;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Maps a single <c>find_order</c> tool call/result into one <see cref="OrderSummaryBlock"/>. Registered
/// with <c>options.AddBlockHandler(new OrderSummaryHandler())</c>.
/// </summary>
/// <remarks>
/// The <b>simplest</b> kind of block handler: a textbook one-to-one tool→block mapping that mirrors the
/// built-in <c>FunctionInvocationHandler</c> but narrowed to the <c>find_order</c> tool. Phase 1 claims the
/// call and reads its <c>orderId</c> argument; phase 2 claims the matching result (by <c>CallId</c>) and
/// projects the returned <see cref="Order"/>. Every other tool flows to the built-in handler.
/// <para>
/// This whole file is mechanical — exactly what a <c>[ToolBlock]</c> source generator would emit for a 1:1
/// tool block — so it is intended to be <b>deleted</b> once that generator is available (see
/// <see cref="OrderSummaryBlock"/> for the migration).
/// </para>
/// </remarks>
public sealed class OrderSummaryHandler : ContentBlockHandler<OrderSummaryBlock>
{
    private const string ToolName = "find_order";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public override BlockMappingResult<OrderSummaryBlock> Handle(
        BlockMappingContext context, OrderSummaryBlock state)
    {
        // Phase 1: claim the find_order call and capture its orderId argument.
        if (state.Call is null)
        {
            FunctionCallContent? call = null;
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionCallContent fc &&
                    string.Equals(fc.Name, ToolName, StringComparison.OrdinalIgnoreCase))
                {
                    call = fc;
                    break;
                }
            }

            if (call is not null)
            {
                context.MarkHandled(call);
                state.Call = call;
                state.Id = call.CallId;
                state.OrderId = GetStringArgument(call, "orderId");
                return BlockMappingResult<OrderSummaryBlock>.Emit(state, state);
            }
        }

        // Phase 2: claim the result whose CallId matches this block's call, then project the order.
        if (state.Call is not null)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionResultContent result && result.CallId == state.Call.CallId)
                {
                    context.MarkHandled(result);
                    state.Result = result;
                    state.Order = ExtractOrder(result.Result);
                    return BlockMappingResult<OrderSummaryBlock>.Complete();
                }
            }
        }

        return BlockMappingResult<OrderSummaryBlock>.Pass();
    }

    /// <summary>Reads a string argument from the call, handling both boxed strings and <see cref="JsonElement"/>.</summary>
    private static string GetStringArgument(FunctionCallContent call, string name)
    {
        if (call.Arguments is { } args && args.TryGetValue(name, out var value) && value is not null)
        {
            return value switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } je => je.GetString() ?? string.Empty,
                JsonElement je => je.ToString(),
                _ => value.ToString() ?? string.Empty,
            };
        }

        return string.Empty;
    }

    /// <summary>
    /// <c>find_order</c> returns a live <see cref="Order"/> (when invoked in-process), JSON (a string or
    /// <see cref="JsonElement"/>), or <see langword="null"/> for a not-found lookup. Handle every shape.
    /// </summary>
    private static Order? ExtractOrder(object? result) => result switch
    {
        null => null,
        Order order => order,
        string s when !string.IsNullOrWhiteSpace(s) => Deserialize(s),
        JsonElement { ValueKind: JsonValueKind.Object } je => Deserialize(je),
        JsonElement => null,
        _ => TryRoundTrip(result),
    };

    private static Order? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Order>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static Order? Deserialize(JsonElement element)
    {
        try
        {
            return element.Deserialize<Order>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static Order? TryRoundTrip(object result)
    {
        try
        {
            return JsonSerializer.Deserialize<Order>(JsonSerializer.Serialize(result), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
