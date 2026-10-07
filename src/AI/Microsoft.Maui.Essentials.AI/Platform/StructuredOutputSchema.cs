using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

internal static class StructuredOutputSchema
{
	internal static AIJsonSchemaTransformCache StrictSchemaTransformCache { get; } =
		new(new()
		{
			DisallowAdditionalProperties = true,
			ConvertBooleanSchemas = true,
			MoveDefaultKeywordToDescription = true,
			RequireAllProperties = true,
			TransformSchemaNode = (ctx, node) =>
			{
				if (node is JsonObject obj &&
					obj.TryGetPropertyValue("type", out var typeNode) &&
					IsObjectType(typeNode) &&
					!obj.ContainsKey("title"))
				{
					obj["title"] = Guid.NewGuid().ToString("N");
				}

				return node;
			},
		});

	internal static JsonElement GetRequiredSchema(ChatResponseFormatJson format)
	{
		ArgumentNullException.ThrowIfNull(format);

		if (StrictSchemaTransformCache.GetOrCreateTransformedSchema(format) is { } schema)
		{
			return schema;
		}

		throw format.Schema is null
			? new InvalidOperationException("A JSON schema is required for structured responses.")
			: new InvalidOperationException("Failed to transform the JSON schema for structured responses.");
	}

	internal static void ValidateResponse(JsonElement value, JsonElement schema) =>
		ValidateResponse(value, schema, "$");

	private static void ValidateResponse(
		JsonElement value,
		JsonElement schema,
		string path)
	{
		if (schema.ValueKind != JsonValueKind.Object)
			throw new NotSupportedException($"JSON schema at '{path}' must be an object.");

		var types = GetTypes(schema);
		if (value.ValueKind == JsonValueKind.Null)
		{
			if (types.Contains("null", StringComparer.Ordinal))
				return;

			throw Invalid(path, "null is not allowed");
		}

		var type = types.FirstOrDefault(candidate => candidate != "null") ?? "object";
		switch (type)
		{
			case "object":
				ValidateObject(value, schema, path);
				break;
			case "array":
				ValidateArray(value, schema, path);
				break;
			case "string":
				ValidateString(value, schema, path);
				break;
			case "integer":
				ValidateNumber(value, schema, path, integer: true);
				break;
			case "number":
				ValidateNumber(value, schema, path, integer: false);
				break;
			case "boolean":
				if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
					throw Invalid(path, "expected a boolean");
				break;
			default:
				throw new NotSupportedException($"JSON schema type '{type}' at '{path}' is not supported.");
		}
	}

	private static void ValidateObject(
		JsonElement value,
		JsonElement schema,
		string path)
	{
		if (value.ValueKind != JsonValueKind.Object)
			throw Invalid(path, "expected an object");
		if (!schema.TryGetProperty("properties", out var properties) ||
			properties.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidOperationException($"Object schema at '{path}' is missing 'properties'.");
		}

		if (schema.TryGetProperty("required", out var required))
		{
			foreach (var propertyName in required.EnumerateArray())
			{
				var name = propertyName.GetString()!;
				if (!value.TryGetProperty(name, out _))
					throw Invalid(path, $"missing required property '{name}'");
			}
		}

		var propertySchemas = properties.EnumerateObject()
			.ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
		{
			if (!propertySchemas.TryGetValue(property.Name, out var propertySchema))
			{
				if (schema.TryGetProperty("additionalProperties", out var additional) &&
					additional.ValueKind == JsonValueKind.False)
				{
					throw Invalid(path, $"property '{property.Name}' is not allowed");
				}

				continue;
			}

			ValidateResponse(property.Value, propertySchema, $"{path}.{property.Name}");
		}
	}

	private static void ValidateArray(
		JsonElement value,
		JsonElement schema,
		string path)
	{
		if (value.ValueKind != JsonValueKind.Array)
			throw Invalid(path, "expected an array");
		if (!schema.TryGetProperty("items", out var itemSchema))
			throw new InvalidOperationException($"Array schema at '{path}' is missing 'items'.");

		var length = value.GetArrayLength();
		if (schema.TryGetProperty("minItems", out var minItems) &&
			length < minItems.GetInt32())
			throw Invalid(path, $"expected at least {minItems.GetInt32()} items");
		if (schema.TryGetProperty("maxItems", out var maxItems) &&
			length > maxItems.GetInt32())
			throw Invalid(path, $"expected at most {maxItems.GetInt32()} items");

		var index = 0;
		foreach (var item in value.EnumerateArray())
			ValidateResponse(item, itemSchema, $"{path}[{index++}]");
	}

	private static void ValidateString(
		JsonElement value,
		JsonElement schema,
		string path)
	{
		if (value.ValueKind != JsonValueKind.String)
			throw Invalid(path, "expected a string");

		if (schema.TryGetProperty("enum", out var enumValues) &&
			!enumValues.EnumerateArray().Any(item =>
				item.ValueKind == JsonValueKind.String &&
				item.ValueEquals(value.GetString())))
		{
			throw Invalid(path, $"value '{value.GetString()}' is not in the allowed enum");
		}
	}

	private static void ValidateNumber(
		JsonElement value,
		JsonElement schema,
		string path,
		bool integer)
	{
		if (value.ValueKind != JsonValueKind.Number)
			throw Invalid(path, integer ? "expected an integer" : "expected a number");

		var number = value.GetDouble();
		if (integer && Math.Truncate(number) != number)
			throw Invalid(path, "expected an integer");
		if (schema.TryGetProperty("minimum", out var minimum) &&
			number < minimum.GetDouble())
			throw Invalid(path, $"value is below minimum {minimum.GetDouble()}");
		if (schema.TryGetProperty("maximum", out var maximum) &&
			number > maximum.GetDouble())
			throw Invalid(path, $"value is above maximum {maximum.GetDouble()}");
	}

	private static string[] GetTypes(JsonElement schema)
	{
		if (!schema.TryGetProperty("type", out var type))
			return ["object"];

		return type.ValueKind switch
		{
			JsonValueKind.String => [type.GetString()!],
			JsonValueKind.Array => [.. type.EnumerateArray().Select(item =>
				item.ValueKind == JsonValueKind.String
					? item.GetString()!
					: throw new NotSupportedException("JSON schema type arrays must contain strings."))],
			_ => throw new NotSupportedException("JSON schema 'type' must be a string or string array."),
		};
	}

	private static InvalidOperationException Invalid(string path, string reason) =>
		new($"Gemini Nano JSON response does not conform to the requested schema at '{path}': {reason}.");

	private static bool IsObjectType(JsonNode? node) =>
		node switch
		{
			JsonValue value when value.TryGetValue<string>(out var type) => type == "object",
			JsonArray array => array.Any(item =>
				item is JsonValue value &&
				value.TryGetValue<string>(out var type) &&
				type == "object"),
			_ => false,
		};
}
