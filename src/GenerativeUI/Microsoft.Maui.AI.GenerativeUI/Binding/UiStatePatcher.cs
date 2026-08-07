using System.Text.Json;

namespace Microsoft.Maui.AI.GenerativeUI.Binding;

/// <summary>
/// Applies RFC 6902 JSON Patch operations (with RFC 6901 JSON Pointer paths) directly to the
/// observable <see cref="UiObject"/> state tree — in place — so bound UI updates without re-inflation.
/// Shapes match AG-UI's <c>STATE_DELTA</c>. Scalars set <see cref="UiObject.Value"/>; array ops mutate
/// <see cref="UiObject.Children"/>; both raise change notifications that flow to the canvas.
/// </summary>
public static class UiStatePatcher
{
    /// <summary>A single JSON Patch operation.</summary>
    public readonly record struct PatchOperation(string Op, string Path, JsonElement? Value, string? From);

    /// <summary>Parses a JSON Patch document (a JSON array of operations).</summary>
    public static IReadOnlyList<PatchOperation> ParseOperations(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new UiPatchException("A JSON Patch document must be a JSON array of operations.");

        var ops = new List<PatchOperation>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var op = GetString(el, "op") ?? throw new UiPatchException("Each operation needs an 'op'.");
            var path = GetString(el, "path") ?? throw new UiPatchException("Each operation needs a 'path'.");
            JsonElement? value = el.TryGetProperty("value", out var v) ? v.Clone() : null;
            var from = GetString(el, "from");
            ops.Add(new PatchOperation(op, path, value, from));
        }
        return ops;
    }

    /// <summary>Applies a sequence of operations to <paramref name="root"/>. Throws on an invalid op.</summary>
    public static void Apply(UiObject root, IEnumerable<PatchOperation> operations)
    {
        foreach (var op in operations)
            ApplyOne(root, op);
    }

    private static void ApplyOne(UiObject root, PatchOperation op)
    {
        var tokens = ParsePointer(op.Path);
        switch (op.Op)
        {
            case "add":
                Add(root, tokens, RequireValue(op));
                break;
            case "replace":
                Replace(root, tokens, RequireValue(op));
                break;
            case "remove":
                Remove(root, tokens);
                break;
            case "move":
                MoveOrCopy(root, op, remove: true);
                break;
            case "copy":
                MoveOrCopy(root, op, remove: false);
                break;
            case "test":
                Test(root, tokens, RequireValue(op));
                break;
            default:
                throw new UiPatchException($"Unsupported op '{op.Op}'.");
        }
    }

    private static void Add(UiObject root, IReadOnlyList<string> tokens, JsonElement value)
    {
        if (tokens.Count == 0)
        {
            UiObjectBuilder.Populate(root, value);
            return;
        }

        var parent = Navigate(root, tokens, tokens.Count - 1, create: true);
        var last = tokens[^1];

        // Array insert/append when the parent already holds a list, or the token is numeric / "-".
        if (last == "-")
        {
            parent.Children.Add(UiObjectBuilder.Build(value));
        }
        else if (int.TryParse(last, out var index) && (parent.Children.Count > 0 || !parent.HasMember(last)))
        {
            index = Math.Clamp(index, 0, parent.Children.Count);
            parent.Children.Insert(index, UiObjectBuilder.Build(value));
        }
        else
        {
            UiObjectBuilder.Populate(parent[last], value);
        }
    }

    private static void Replace(UiObject root, IReadOnlyList<string> tokens, JsonElement value)
    {
        if (tokens.Count == 0)
        {
            ClearMembers(root);
            UiObjectBuilder.Populate(root, value);
            return;
        }

        var target = Navigate(root, tokens, tokens.Count, create: false)
            ?? throw new UiPatchException($"replace target not found: {Join(tokens)}");
        ClearMembers(target);
        target.Children.Clear();
        UiObjectBuilder.Populate(target, value);
    }

    private static void Remove(UiObject root, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            throw new UiPatchException("Cannot remove the root.");

        var parent = Navigate(root, tokens, tokens.Count - 1, create: false)
            ?? throw new UiPatchException($"remove parent not found: {Join(tokens)}");
        var last = tokens[^1];

        if (int.TryParse(last, out var index) && index >= 0 && index < parent.Children.Count)
            parent.Children.RemoveAt(index);
        else if (!parent.RemoveMember(last))
            throw new UiPatchException($"remove target not found: {Join(tokens)}");
    }

    private static void MoveOrCopy(UiObject root, PatchOperation op, bool remove)
    {
        if (op.From is null)
            throw new UiPatchException($"'{op.Op}' requires a 'from'.");
        var fromTokens = ParsePointer(op.From);
        var source = Navigate(root, fromTokens, fromTokens.Count, create: false)
            ?? throw new UiPatchException($"{op.Op} source not found: {op.From}");

        var snapshot = UiObjectBuilder.ToJson(source);
        var valueJson = snapshot?.ToJsonString() ?? "null";
        using var doc = JsonDocument.Parse(valueJson);
        var value = doc.RootElement.Clone();

        if (remove)
            Remove(root, fromTokens);
        Add(root, ParsePointer(op.Path), value);
    }

    private static void Test(UiObject root, IReadOnlyList<string> tokens, JsonElement value)
    {
        var target = Navigate(root, tokens, tokens.Count, create: false)
            ?? throw new UiPatchException($"test target not found: {Join(tokens)}");
        var actual = UiObjectBuilder.ToJson(target)?.ToJsonString() ?? "null";
        var expected = value.ValueKind == JsonValueKind.Undefined ? "null" : value.GetRawText();
        if (!JsonEquals(actual, expected))
            throw new UiPatchException($"test failed at {Join(tokens)}.");
    }

    // ── Navigation ──────────────────────────────────────────────────────────────────────────────

    private static UiObject? Navigate(UiObject root, IReadOnlyList<string> tokens, int count, bool create)
    {
        var node = root;
        for (var i = 0; i < count; i++)
        {
            var token = tokens[i];
            if (int.TryParse(token, out var index) && node.Children.Count > 0)
            {
                if (index < 0 || index >= node.Children.Count)
                    return null;
                node = node.Children[index];
            }
            else if (create || node.HasMember(token))
            {
                node = node[token];
            }
            else
            {
                return null;
            }
        }
        return node;
    }

    private static void ClearMembers(UiObject node)
    {
        foreach (var (key, _) in node.Members.ToList())
            node.RemoveMember(key);
    }

    // ── JSON Pointer (RFC 6901) ─────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ParsePointer(string pointer)
    {
        if (string.IsNullOrEmpty(pointer))
            return [];
        if (pointer[0] != '/')
            throw new UiPatchException($"Invalid JSON Pointer '{pointer}' (must start with '/').");
        return [.. pointer[1..].Split('/').Select(Unescape)];
    }

    private static string Unescape(string token) => token.Replace("~1", "/").Replace("~0", "~");

    private static string Join(IReadOnlyList<string> tokens) => "/" + string.Join("/", tokens);

    private static JsonElement RequireValue(PatchOperation op) =>
        op.Value ?? throw new UiPatchException($"'{op.Op}' at {op.Path} requires a 'value'.");

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool JsonEquals(string a, string b)
    {
        try
        {
            using var da = JsonDocument.Parse(a);
            using var db = JsonDocument.Parse(b);
            return da.RootElement.GetRawText() == db.RootElement.GetRawText();
        }
        catch
        {
            return a == b;
        }
    }
}

/// <summary>Thrown when a JSON Patch operation is malformed or cannot be applied.</summary>
public sealed class UiPatchException(string message) : Exception(message);
