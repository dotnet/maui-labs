using System.Globalization;
using System.Text.Json;

namespace Microsoft.Maui.AI.GenerativeUI.Dsl;

/// <summary>
/// One node in a UI-DSL document. Wraps the raw JSON so unknown/extra props are ignored and missing
/// props fall back to defaults (the DSL is deliberately forgiving). See
/// <c>docs/GenerativeUI/spec/appendix-ui-dsl.md</c>.
/// </summary>
public sealed class UiNode
{
    private readonly JsonElement _element;

    private UiNode(JsonElement element, string type, IReadOnlyList<string> style, IReadOnlyList<UiNode> children)
    {
        _element = element;
        Type = type;
        Style = style;
        Children = children;
    }

    /// <summary>Node type: a built-in (<c>Label</c>, <c>Stack</c>, …) or a registered control/screen name.</summary>
    public string Type { get; }

    /// <summary>Optional id for targeting/debugging.</summary>
    public string? Id => GetString("id");

    /// <summary>Optional one-way dotted path into the document's <c>data</c>.</summary>
    public string? Bind => GetString("bind");

    /// <summary>Registered style token(s), normalized to a list (accepts a string or an array).</summary>
    public IReadOnlyList<string> Style { get; }

    /// <summary>Child nodes (for containers and pre-expanded <c>List</c> rows).</summary>
    public IReadOnlyList<UiNode> Children { get; }

    /// <summary>The raw node JSON (for registered controls' <c>props</c> and future needs).</summary>
    public JsonElement Raw => _element;

    /// <summary>The <c>props</c> object (registered controls), or <c>null</c> if absent.</summary>
    public JsonElement? Props =>
        _element.ValueKind == JsonValueKind.Object &&
        _element.TryGetProperty("props", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : null;

    public string? GetString(string name)
        => _element.ValueKind == JsonValueKind.Object &&
           _element.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            }
            : null;

    public double? GetNumber(string name)
        => _element.ValueKind == JsonValueKind.Object &&
           _element.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.GetDouble(),
                JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
                _ => null,
            }
            : null;

    public bool? GetBool(string name)
        => _element.ValueKind == JsonValueKind.Object &&
           _element.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
                _ => null,
            }
            : null;

    /// <summary>Parses a JSON element into a <see cref="UiNode"/> (recursively). Never throws on shape.</summary>
    public static UiNode Parse(JsonElement element)
    {
        var type = element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? "Unknown"
            : "Unknown";

        var style = ParseStyle(element);
        var children = ParseChildren(element);
        return new UiNode(element, type, style, children);
    }

    private static IReadOnlyList<string> ParseStyle(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("style", out var s))
            return [];

        return s.ValueKind switch
        {
            JsonValueKind.String => s.GetString() is { Length: > 0 } str ? [str] : [],
            JsonValueKind.Array => [.. s.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(x => x.Length > 0)],
            _ => [],
        };
    }

    private static IReadOnlyList<UiNode> ParseChildren(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("children", out var c) ||
            c.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<UiNode>();
        foreach (var child in c.EnumerateArray())
        {
            if (child.ValueKind == JsonValueKind.Object)
                list.Add(Parse(child));
        }
        return list;
    }
}
