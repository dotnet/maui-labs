using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Attributes.Tests.Equivalence;

/// <summary>
/// Shared utilities for the equivalence suite: normalize arbitrary values to JsonElement so
/// "expected (baseline)" and "actual (ours)" can be compared regardless of whether either side
/// produced a raw .NET object, a JsonElement, or a string.
/// </summary>
internal static class EquivalenceHelpers
{
    public static JsonElement Normalize(object? value, JsonSerializerOptions? options = null)
    {
        options ??= AIJsonUtilities.DefaultOptions;
        if (value is JsonElement e)
        {
            return e;
        }
        return JsonSerializer.SerializeToElement(value, options);
    }

    public static void AssertInvocationEqual(object? expected, object? actual, JsonSerializerOptions? options = null)
    {
        var opts = options ?? AIJsonUtilities.DefaultOptions;
        var e = Normalize(expected, opts);
        var a = Normalize(actual, opts);
        var en = JsonSerializer.SerializeToNode(e, opts);
        var an = JsonSerializer.SerializeToNode(a, opts);
        if (!JsonNode.DeepEquals(en, an))
        {
            throw new Xunit.Sdk.XunitException(
                $"Values are not equivalent.\nExpected: {e.GetRawText()}\nActual:   {a.GetRawText()}");
        }
    }
}
