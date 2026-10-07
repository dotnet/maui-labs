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

		if (format.Schema is { } original)
			ValidateSchema(original, "$");

		if (StrictSchemaTransformCache.GetOrCreateTransformedSchema(format) is { } schema)
		{
			ValidateSchema(schema, "$");
			return schema;
		}

		throw format.Schema is null
			? new InvalidOperationException("A JSON schema is required for structured responses.")
			: new InvalidOperationException("Failed to transform the JSON schema for structured responses.");
	}

	internal static void ValidateResponse(JsonElement value, JsonElement schema)
	{
		ValidateSchema(schema, "$");
		ValidateResponse(value, schema, "$");
	}

	private static void ValidateSchema(JsonElement schema, string path)
	{
		if (schema.ValueKind != JsonValueKind.Object)
			throw Unsupported(path, "schemas must be objects with an explicit type");

		var types = GetTypes(schema);
		if (types.Length == 0 || types.Distinct(StringComparer.Ordinal).Count() != types.Length ||
			types.Count(type => type != "null") > 1)
			throw Unsupported(path, "only a single type optionally unioned with null is supported");

		var type = types.FirstOrDefault(candidate => candidate != "null") ?? "null";
		if (type is not ("object" or "array" or "string" or "integer" or "number" or "boolean" or "null"))
			throw Unsupported(path, $"type '{type}' is not supported");

		var keywords = new HashSet<string>(StringComparer.Ordinal);
		foreach (var keyword in schema.EnumerateObject())
		{
			if (!keywords.Add(keyword.Name))
				throw Unsupported(path, $"duplicate keyword '{keyword.Name}'");
			var supported = keyword.Name switch
			{
				"type" or "title" or "description" or "$schema" or "default" => true,
				"properties" or "required" or "additionalProperties" => type == "object",
				"items" or "minItems" or "maxItems" => type == "array",
				"minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" => type is "number" or "integer",
				"enum" => type is "string" or "number" or "integer" or "boolean" or "null",
				_ => false,
			};
			if (!supported)
				throw Unsupported(path, $"keyword '{keyword.Name}' is not supported for type '{type}'");
			if (keyword.Name is "title" or "description" or "$schema" &&
				keyword.Value.ValueKind != JsonValueKind.String)
				throw Unsupported(path, $"'{keyword.Name}' must be a string");
		}

		if (type == "object")
		{
			if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
				throw Unsupported(path, "objects must define 'properties'");
			var propertyNames = new HashSet<string>(StringComparer.Ordinal);
			foreach (var property in properties.EnumerateObject())
			{
				if (!propertyNames.Add(property.Name))
					throw Unsupported(path, $"duplicate property '{property.Name}'");
				ValidateSchema(property.Value, $"{path}.{property.Name}");
			}

			if (schema.TryGetProperty("additionalProperties", out var additional) &&
				additional.ValueKind != JsonValueKind.False)
				throw Unsupported(path, "'additionalProperties' must be false");
			if (schema.TryGetProperty("required", out var required))
			{
				if (required.ValueKind != JsonValueKind.Array)
					throw Unsupported(path, "'required' must be an array of property names");
				var names = new HashSet<string>(StringComparer.Ordinal);
				foreach (var name in required.EnumerateArray())
				{
					if (name.ValueKind != JsonValueKind.String ||
						!properties.TryGetProperty(name.GetString()!, out _) ||
						!names.Add(name.GetString()!))
						throw Unsupported(path, "'required' must contain unique, declared property names");
				}
			}
		}
		else if (type == "array")
		{
			if (!schema.TryGetProperty("items", out var items))
				throw Unsupported(path, "arrays must define 'items'");
			ValidateSchema(items, $"{path}[]");
			foreach (var name in new[] { "minItems", "maxItems" })
			{
				if (schema.TryGetProperty(name, out var limit) &&
					(limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt32(out var count) || count < 0))
					throw Unsupported(path, $"'{name}' must be a non-negative 32-bit integer");
			}
			if (schema.TryGetProperty("minItems", out var min) && schema.TryGetProperty("maxItems", out var max) &&
				min.GetInt32() > max.GetInt32())
				throw Unsupported(path, "'minItems' must not exceed 'maxItems'");
		}
		else if (type is "number" or "integer")
		{
			foreach (var name in new[] { "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum" })
			{
				if (schema.TryGetProperty(name, out var bound))
				{
					if (bound.ValueKind != JsonValueKind.Number || !bound.TryGetDouble(out var number) || !double.IsFinite(number))
						throw Unsupported(path, $"'{name}' must be a finite number");
					NormalizeNumber(bound);
				}
			}
			foreach (var lower in new[] { "minimum", "exclusiveMinimum" })
			foreach (var upper in new[] { "maximum", "exclusiveMaximum" })
			{
				if (schema.TryGetProperty(lower, out var lowerBound) && schema.TryGetProperty(upper, out var upperBound))
				{
					var comparison = CompareNumbers(lowerBound, upperBound);
					if (comparison > 0 || comparison == 0 && (lower == "exclusiveMinimum" || upper == "exclusiveMaximum"))
						throw Unsupported(path, "numeric bounds must allow at least one value");
				}
			}
		}

		if (schema.TryGetProperty("enum", out var values))
		{
			if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0)
				throw Unsupported(path, "'enum' must be a non-empty array");
			foreach (var value in values.EnumerateArray())
			{
				var matches = value.ValueKind switch
				{
					JsonValueKind.Null => types.Contains("null", StringComparer.Ordinal),
					JsonValueKind.String => type == "string",
					JsonValueKind.True or JsonValueKind.False => type == "boolean",
					JsonValueKind.Number => type is "number" or "integer" &&
						value.TryGetDouble(out var number) && double.IsFinite(number) &&
						(type != "integer" || IsInteger(value)),
					_ => false,
				};
				if (!matches)
					throw Unsupported(path, "'enum' values must match the declared primitive type");
				if (value.ValueKind == JsonValueKind.Number)
					NormalizeNumber(value);
			}
		}
	}

	private static void ValidateResponse(
		JsonElement value,
		JsonElement schema,
		string path)
	{
		if (schema.ValueKind != JsonValueKind.Object)
			throw new NotSupportedException($"JSON schema at '{path}' must be an object.");

		var types = GetTypes(schema);
		if (schema.TryGetProperty("enum", out var allowed) &&
			!allowed.EnumerateArray().Any(item => JsonElement.DeepEquals(item, value)))
			throw Invalid(path, "value is not in the allowed enum");

		if (value.ValueKind == JsonValueKind.Null)
		{
			if (types.Contains("null", StringComparer.Ordinal))
				return;

			throw Invalid(path, "null is not allowed");
		}

		var type = types.FirstOrDefault(candidate => candidate != "null") ?? "null";
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
			case "null":
				throw Invalid(path, "expected null");
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
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
		{
			if (!seen.Add(property.Name))
				throw Invalid(path, $"duplicate property '{property.Name}'");
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
	}

	private static void ValidateNumber(
		JsonElement value,
		JsonElement schema,
		string path,
		bool integer)
	{
		if (value.ValueKind != JsonValueKind.Number)
			throw Invalid(path, integer ? "expected an integer" : "expected a number");

		if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
			throw Invalid(path, "expected a finite number");
		try
		{
			NormalizeNumber(value);
		}
		catch (NotSupportedException)
		{
			throw Invalid(path, "numeric exponents must fit a 32-bit integer");
		}
		if (integer && !IsInteger(value))
			throw Invalid(path, "expected an integer");
		if (schema.TryGetProperty("minimum", out var minimum) &&
			CompareNumbers(value, minimum) < 0)
			throw Invalid(path, $"value is below minimum {minimum.GetDouble()}");
		if (schema.TryGetProperty("maximum", out var maximum) &&
			CompareNumbers(value, maximum) > 0)
			throw Invalid(path, $"value is above maximum {maximum.GetDouble()}");
		if (schema.TryGetProperty("exclusiveMinimum", out var exclusiveMinimum) &&
			CompareNumbers(value, exclusiveMinimum) <= 0)
			throw Invalid(path, $"value must exceed {exclusiveMinimum.GetDouble()}");
		if (schema.TryGetProperty("exclusiveMaximum", out var exclusiveMaximum) &&
			CompareNumbers(value, exclusiveMaximum) >= 0)
			throw Invalid(path, $"value must be below {exclusiveMaximum.GetDouble()}");
	}

	private static bool IsInteger(JsonElement value)
	{
		var (_, digits, exponent) = NormalizeNumber(value);
		return digits == "0" || exponent >= 0;
	}

	private static int CompareNumbers(JsonElement left, JsonElement right)
	{
		var (leftNegative, leftDigits, leftExponent) = NormalizeNumber(left);
		var (rightNegative, rightDigits, rightExponent) = NormalizeNumber(right);
		if (leftNegative != rightNegative)
			return leftNegative ? -1 : 1;
		var comparison = leftDigits == "0" || rightDigits == "0"
			? (leftDigits != "0").CompareTo(rightDigits != "0")
			: (leftDigits.Length + leftExponent).CompareTo(rightDigits.Length + rightExponent);
		if (comparison == 0)
		{
			var length = Math.Max(leftDigits.Length, rightDigits.Length);
			comparison = string.CompareOrdinal(leftDigits.PadRight(length, '0'), rightDigits.PadRight(length, '0'));
		}
		return leftNegative ? -comparison : comparison;
	}

	// Compare JSON numbers exactly; double rounding can otherwise accept fractional
	// integers or values outside bounds, particularly beyond Int64's exact double range.
	private static (bool Negative, string Digits, long Exponent) NormalizeNumber(JsonElement value)
	{
		var raw = value.GetRawText();
		var negative = raw[0] == '-';
		var exponentIndex = raw.IndexOfAny(['e', 'E']);
		var exponent = 0;
		if (exponentIndex >= 0 && !int.TryParse(raw.AsSpan(exponentIndex + 1),
			System.Globalization.NumberStyles.AllowLeadingSign,
			System.Globalization.CultureInfo.InvariantCulture, out exponent))
			throw new NotSupportedException("JSON numeric exponents must fit a 32-bit integer.");
		var significand = exponentIndex < 0 ? raw : raw[..exponentIndex];
		if (negative)
			significand = significand[1..];
		var decimalIndex = significand.IndexOf('.');
		var fractionLength = decimalIndex < 0 ? 0 : significand.Length - decimalIndex - 1;
		var digits = significand.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
		if (digits.Length == 0)
			return (false, "0", 0);
		var trimmed = digits.TrimEnd('0');
		return (negative, trimmed, (long)exponent - fractionLength + digits.Length - trimmed.Length);
	}

	private static string[] GetTypes(JsonElement schema)
	{
		if (!schema.TryGetProperty("type", out var type))
			throw new NotSupportedException("JSON schemas must declare an explicit 'type'.");

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

	private static NotSupportedException Unsupported(string path, string reason) =>
		new($"Gemini Nano JSON schema at '{path}' is not supported: {reason}.");

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
