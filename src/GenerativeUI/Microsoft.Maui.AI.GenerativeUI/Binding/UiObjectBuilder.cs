using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI.GenerativeUI.Binding;

/// <summary>
/// Walks JSON (from a <c>render_ui</c> payload or a REST response) into the generic
/// <see cref="UiObject"/> tree, and serializes a subtree back to JSON for <c>get_state</c>.
/// </summary>
public static class UiObjectBuilder
{
    /// <summary>Builds a fresh <see cref="UiObject"/> tree from a JSON element.</summary>
    public static UiObject Build(JsonElement element, string? name = null)
    {
        var node = new UiObject(name);
        Populate(node, element);
        return node;
    }

    /// <summary>Populates an existing node from a JSON element (used to seed a persistent form root).</summary>
    public static void Populate(UiObject node, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    Populate(node[prop.Name], prop.Value);
                break;

            case JsonValueKind.Array:
                node.Children.Clear();
                foreach (var item in element.EnumerateArray())
                    node.Children.Add(Build(item));
                break;

            case JsonValueKind.String:
                node.Value = element.GetString();
                break;

            case JsonValueKind.Number:
                node.Value = element.TryGetInt64(out var l) ? l : element.GetDouble();
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                node.Value = element.GetBoolean();
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                node.Value = null;
                break;
        }
    }

    /// <summary>
    /// Seeds a form root from a flat/nested object without clearing existing keys the payload omits,
    /// so in-progress edits survive a re-render that supplies only some fields.
    /// </summary>
    public static void Merge(UiObject node, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Populate(node, element);
            return;
        }

        foreach (var prop in element.EnumerateObject())
        {
            var child = node[prop.Name];
            if (prop.Value.ValueKind is JsonValueKind.Object)
                Merge(child, prop.Value);
            else
                Populate(child, prop.Value);
        }
    }

    /// <summary>
    /// Replaces a subtree while preserving object-member and collection identities where possible,
    /// so existing MAUI bindings stay attached across fresh snapshots of the same data.
    /// </summary>
    public static void Replace(UiObject node, JsonElement element)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                node.Value = null;
                node.Children.Clear();

                var incomingNames = element.EnumerateObject()
                    .Select(property => property.Name)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var (key, _) in node.Members.ToList())
                {
                    if (!incomingNames.Contains(key))
                        node.RemoveMember(key);
                }

                foreach (var property in element.EnumerateObject())
                    Replace(node[property.Name], property.Value);
                break;

            case JsonValueKind.Array:
                ClearMembers(node);
                node.Value = null;
                node.Children.Clear();
                foreach (var item in element.EnumerateArray())
                    node.Children.Add(Build(item));
                break;

            default:
                ClearMembers(node);
                node.Children.Clear();
                Populate(node, element);
                break;
        }
    }

    /// <summary>Serializes a node's members/children/value back into a JSON object for <c>get_state</c>.</summary>
    public static JsonNode? ToJson(UiObject node)
    {
        var members = node.Members.ToList();
        if (members.Count > 0)
        {
            var obj = new JsonObject();
            foreach (var (key, child) in members)
                obj[key] = ToJson(child);
            return obj;
        }

        if (node.Children.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var child in node.Children)
                arr.Add(ToJson(child));
            return arr;
        }

        return node.Value switch
        {
            null => null,
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            double d => JsonValue.Create(d),
            float f => JsonValue.Create(f),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            _ => JsonValue.Create(node.AsString()),
        };
    }

    private static void ClearMembers(UiObject node)
    {
        foreach (var (key, _) in node.Members.ToList())
            node.RemoveMember(key);
    }
}
