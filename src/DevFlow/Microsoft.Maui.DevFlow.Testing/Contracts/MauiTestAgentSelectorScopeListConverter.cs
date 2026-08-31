using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

internal sealed class MauiTestAgentSelectorScopeListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("allowedSelectors must be an array.");

        var selectors = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return selectors;

            if (reader.TokenType == JsonTokenType.String)
            {
                selectors.Add(reader.GetString()!);
                continue;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("allowedSelectors entries must be selector keys or selector objects.");

            using var document = JsonDocument.ParseValue(ref reader);
            selectors.Add(ToScopeKey(document.RootElement));
        }

        throw new JsonException("allowedSelectors array was not terminated.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        List<string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var selector in value)
            writer.WriteStringValue(selector);
        writer.WriteEndArray();
    }

    private static string ToScopeKey(JsonElement selector)
    {
        if (selector.TryGetProperty("automationId", out var automationId) &&
            automationId.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(automationId.GetString()))
        {
            var stableItemKey = ReadOptionalString(selector, "stableItemKey");
            var collectionScope = ReadOptionalString(selector, "collectionScope");
            if ((stableItemKey is null) != (collectionScope is null))
                throw new JsonException("Scoped item selectors require stableItemKey and collectionScope together.");
            return stableItemKey is not null
                ? MauiTestAgentSelectorScopeKey.ScopedItem(collectionScope!, stableItemKey, automationId.GetString()!)
                : MauiTestAgentSelectorScopeKey.FromSelector(new FlowSelector
                {
                    AutomationId = automationId.GetString(),
                })!;
        }

        if (selector.TryGetProperty("typeIndex", out var typeIndex) &&
            typeIndex.ValueKind == JsonValueKind.Object &&
            TryReadTypeIndex(typeIndex, out var nestedKey))
        {
            return nestedKey;
        }

        if (selector.TryGetProperty("selectorKind", out var selectorKind) &&
            selectorKind.ValueKind == JsonValueKind.String &&
            string.Equals(selectorKind.GetString(), "typeIndex", StringComparison.Ordinal) &&
            TryReadTypeIndex(selector, out var flatKey))
        {
            return flatKey;
        }

        throw new JsonException(
            "allowedSelectors objects must contain automationId or a non-negative typeIndex.");
    }

    private static string? ReadOptionalString(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) &&
           property.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;

    private static bool TryReadTypeIndex(JsonElement selector, out string key)
    {
        key = string.Empty;
        if (!selector.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(type.GetString()) ||
            !selector.TryGetProperty("index", out var index) ||
            !index.TryGetInt32(out var value) ||
            value < 0)
        {
            return false;
        }

        key = MauiTestAgentSelectorScopeKey.FromSelector(new FlowSelector
        {
            TypeIndex = new FlowTypeIndex { Type = type.GetString(), Index = value },
        })!;
        return true;
    }
}
