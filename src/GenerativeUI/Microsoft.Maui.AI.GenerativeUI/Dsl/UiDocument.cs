using System.Text.Json;

namespace Microsoft.Maui.AI.GenerativeUI.Dsl;

/// <summary>
/// A parsed <c>render_ui</c> document: <c>schemaVersion</c> + root <c>ui</c> node, plus optional
/// one-way <c>data</c>, editable <c>form</c> seed, and non-visual <c>meta</c>.
/// See <c>docs/GenerativeUI/spec/appendix-ui-dsl.md §2</c>.
/// </summary>
public sealed class UiDocument
{
    /// <summary>The DSL schema version this library understands.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public UiNode? Ui { get; init; }
    public JsonElement? Data { get; init; }
    public JsonElement? Form { get; init; }
    public string? Title { get; init; }
    public bool Replace { get; init; } = true;

    /// <summary>
    /// Parses a document from JSON text. Throws <see cref="UiDocumentParseException"/> on malformed
    /// JSON or a missing root <c>ui</c> node (the tool surfaces this so the model can retry).
    /// </summary>
    public static UiDocument Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new UiDocumentParseException($"Invalid JSON: {ex.Message}", json);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new UiDocumentParseException("Document root must be a JSON object.", json);

            var schemaVersion = root.TryGetProperty("schemaVersion", out var sv) && sv.ValueKind == JsonValueKind.Number
                ? sv.GetInt32()
                : CurrentSchemaVersion;

            if (!root.TryGetProperty("ui", out var uiEl) || uiEl.ValueKind != JsonValueKind.Object)
                throw new UiDocumentParseException("Document must contain a 'ui' object (the root node).", json);

            // JsonElements are backed by the JsonDocument, which is disposed here — clone what we keep.
            var ui = UiNode.Parse(uiEl.Clone());

            JsonElement? data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                ? d.Clone()
                : null;

            JsonElement? form = root.TryGetProperty("form", out var f) && f.ValueKind == JsonValueKind.Object
                ? f.Clone()
                : null;

            string? title = null;
            var replace = true;
            if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                if (meta.TryGetProperty("title", out var tt) && tt.ValueKind == JsonValueKind.String)
                    title = tt.GetString();
                if (meta.TryGetProperty("replace", out var rp) && rp.ValueKind is JsonValueKind.False)
                    replace = false;
            }

            return new UiDocument
            {
                SchemaVersion = schemaVersion,
                Ui = ui,
                Data = data,
                Form = form,
                Title = title,
                Replace = replace,
            };
        }
    }
}

/// <summary>Thrown when a <c>render_ui</c> document is malformed; carries the offending text (truncated).</summary>
public sealed class UiDocumentParseException(string message, string rawJson) : Exception(message)
{
    public string RawJson { get; } = rawJson.Length > 500 ? rawJson[..500] + "…" : rawJson;
}
