using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Validates the supported tool-argument JSON schema subset and the values produced for it.
/// </summary>
internal static class WindowsAIToolSchema
{
	// Windows requires every property in constrained output. Nullable extraction properties
	// represent absence without forcing the model to invent an argument or overwrite a default.
	internal static JsonObject CreateExtractionSchema(JsonElement schema)
	{
		var extraction = JsonNode.Parse(schema.GetRawText())!.AsObject();
		MakePropertiesNullable(extraction);
		extraction.Remove("required");
		return extraction;
	}

	private static void MakePropertiesNullable(JsonObject schema)
	{
		if (schema["properties"] is JsonObject properties)
		{
			foreach (var property in properties)
			{
				var child = property.Value!.AsObject();
				MakePropertiesNullable(child);
				var type = child["type"]!.GetValue<string>();
				if (type != "null")
				{
					child["type"] = new JsonArray(type, "null");
					if (child["enum"] is JsonArray values && !values.Any(value => value is null))
						values.Add(null);
				}
			}
		}
		if (schema["items"] is JsonObject items)
			MakePropertiesNullable(items);
	}

	internal static JsonElement RemoveMissingValues(JsonElement value, JsonElement schema)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
			WritePresentValues(writer, value, schema);
		using var document = JsonDocument.Parse(stream.ToArray());
		return document.RootElement.Clone();
	}

	private static void WritePresentValues(Utf8JsonWriter writer, JsonElement value, JsonElement schema)
	{
		if (value.ValueKind == JsonValueKind.Object)
		{
			var hasProperties = schema.TryGetProperty("properties", out var properties);
			writer.WriteStartObject();
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var property in value.EnumerateObject())
			{
				if (!names.Add(property.Name))
					throw new InvalidOperationException($"Windows AI returned duplicate argument names: {property.Name}.");
				if (hasProperties && properties.TryGetProperty(property.Name, out var child))
				{
					if (property.Value.ValueKind == JsonValueKind.Null &&
						child.GetProperty("type").GetString() != "null")
						continue;
					if (!IsRequired(schema, property.Name) && IsAbsentObjectShell(property.Value, child))
						continue;
					writer.WritePropertyName(property.Name);
					WritePresentValues(writer, property.Value, child);
				}
				else
					property.WriteTo(writer);
			}
			writer.WriteEndObject();
		}
		else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
		{
			writer.WriteStartArray();
			foreach (var item in value.EnumerateArray())
				WritePresentValues(writer, item, items);
			writer.WriteEndArray();
		}
		else
			value.WriteTo(writer);
	}

	private static bool IsRequired(JsonElement schema, string name) =>
		schema.TryGetProperty("required", out var required) &&
			required.EnumerateArray().Any(property => property.GetString() == name);

	private static bool IsAbsentObjectShell(JsonElement value, JsonElement schema)
	{
		if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Any() ||
			!schema.TryGetProperty("properties", out var properties))
			return false;
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
		{
			// Unknown and duplicate keys must survive normalization so validation rejects them.
			if (!names.Add(property.Name) || !properties.TryGetProperty(property.Name, out var child))
				return false;
			if (property.Value.ValueKind == JsonValueKind.Null &&
				child.GetProperty("type").GetString() != "null")
				continue;
			if (!IsAbsentObjectShell(property.Value, child))
				return false;
		}
		return true;
	}

	/// <summary>Whether a parameter schema declares any properties to fill in.</summary>
	internal static bool HasProperties(JsonElement schema) =>
		schema.ValueKind == JsonValueKind.Object &&
		schema.TryGetProperty("properties", out var properties) &&
		properties.ValueKind == JsonValueKind.Object &&
		properties.EnumerateObject().Any();

	internal static void ValidateToolSchema(JsonElement schema)
	{
		if (schema.ValueKind != JsonValueKind.Object ||
			!schema.TryGetProperty("type", out var type) || type.GetString() != "object")
			throw new NotSupportedException("Tool arguments must have an object JSON schema.");

		ValidateSupportedSchema(schema);
	}

	private static void ValidateSupportedSchema(JsonElement schema)
	{
		if (schema.ValueKind != JsonValueKind.Object)
			throw new NotSupportedException("Tool argument schema must be an object.");

		foreach (var keyword in schema.EnumerateObject())
		{
			if (keyword.Name is not ("type" or "properties" or "required" or "items" or
				"additionalProperties" or "enum" or "description" or "title" or "$schema" or
				"default" or "minimum" or "maximum" or "minLength" or "maxLength" or "pattern"))
				throw new NotSupportedException($"Unsupported tool argument schema keyword: {keyword.Name}.");
		}

		if (!schema.TryGetProperty("type", out var type) ||
			type.ValueKind != JsonValueKind.String ||
			type.GetString() is not ("object" or "array" or "string" or "integer" or "number" or "boolean" or "null"))
			throw new NotSupportedException("Tool argument schema must declare a supported type.");

		if (schema.TryGetProperty("properties", out var properties))
		{
			if (type.GetString() != "object" || properties.ValueKind != JsonValueKind.Object)
				throw new NotSupportedException("Tool properties require an object schema.");
			foreach (var property in properties.EnumerateObject())
				ValidateSupportedSchema(property.Value);
		}

		if (schema.TryGetProperty("required", out var required) &&
			(type.GetString() != "object" || required.ValueKind != JsonValueKind.Array ||
				required.EnumerateArray().Any(name => name.ValueKind != JsonValueKind.String ||
					!schema.TryGetProperty("properties", out var declared) ||
					!declared.TryGetProperty(name.GetString()!, out _))))
			throw new NotSupportedException("Required tool arguments must name declared properties.");

		if (schema.TryGetProperty("items", out var items))
		{
			if (type.GetString() != "array")
				throw new NotSupportedException("Tool items require an array schema.");
			ValidateSupportedSchema(items);
		}
		else if (type.GetString() == "array")
			throw new NotSupportedException("Array tool arguments must declare an items schema.");

		if (schema.TryGetProperty("additionalProperties", out var additional) &&
			additional.ValueKind != JsonValueKind.False)
			throw new NotSupportedException("Tool argument schemas must prohibit additional properties.");

		if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind != JsonValueKind.Array)
			throw new NotSupportedException("Tool argument enum must be an array.");
		foreach (var keyword in new[] { "minLength", "maxLength" })
			if (schema.TryGetProperty(keyword, out var length) &&
				(type.GetString() != "string" || length.ValueKind != JsonValueKind.Number ||
					!length.TryGetInt32(out var count) || count < 0))
				throw new NotSupportedException($"Invalid tool argument {keyword}.");
		foreach (var keyword in new[] { "minimum", "maximum" })
			if (schema.TryGetProperty(keyword, out var bound) &&
				(type.GetString() is not ("integer" or "number") || bound.ValueKind != JsonValueKind.Number))
				throw new NotSupportedException($"Invalid tool argument {keyword}.");
		if (schema.TryGetProperty("pattern", out var pattern))
		{
			if (type.GetString() != "string" || pattern.ValueKind != JsonValueKind.String)
				throw new NotSupportedException("Tool argument pattern requires a string schema.");
			try
			{
				_ = new Regex(
					pattern.GetString()!,
					RegexOptions.ECMAScript | RegexOptions.CultureInvariant,
					TimeSpan.FromSeconds(1));
			}
			catch (ArgumentException ex)
			{
				throw new NotSupportedException("Tool argument pattern is invalid.", ex);
			}
		}
	}

	internal static void ValidateValue(
		JsonElement value, JsonElement schema, string path, bool enforceRequired = true)
	{
		var type = schema.GetProperty("type").GetString();
		var valid = type switch
		{
			"object" => value.ValueKind == JsonValueKind.Object,
			"array" => value.ValueKind == JsonValueKind.Array,
			"string" => value.ValueKind == JsonValueKind.String,
			"integer" => value.ValueKind == JsonValueKind.Number &&
				IsJsonInteger(value),
			"number" => value.ValueKind == JsonValueKind.Number,
			"boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
			"null" => value.ValueKind == JsonValueKind.Null,
			_ => false
		};
		if (!valid)
			throw new InvalidOperationException($"Windows AI returned an invalid {path}: expected {type}.");

		if (schema.TryGetProperty("enum", out var allowed) &&
			!allowed.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
			throw new InvalidOperationException($"Windows AI returned an invalid {path}: value is not in the allowed enum.");

		if (type == "object")
		{
			var properties = schema.TryGetProperty("properties", out var declared) ? declared : default;
			foreach (var property in value.EnumerateObject())
			{
				if (properties.ValueKind != JsonValueKind.Object ||
					!properties.TryGetProperty(property.Name, out var propertySchema))
					throw new InvalidOperationException($"Windows AI returned an unexpected tool argument: {path}.{property.Name}.");
				ValidateValue(property.Value, propertySchema, $"{path}.{property.Name}", enforceRequired);
			}

			if (enforceRequired && schema.TryGetProperty("required", out var required))
				foreach (var name in required.EnumerateArray())
					if (!value.TryGetProperty(name.GetString()!, out _))
						throw new InvalidOperationException($"Windows AI omitted required tool argument: {path}.{name.GetString()}.");
		}
		else if (type == "array")
		{
			foreach (var item in value.EnumerateArray())
				ValidateValue(item, schema.GetProperty("items"), $"{path}[]", enforceRequired);
		}
		else if (type == "string")
		{
			var text = value.GetString()!;
			var length = text.EnumerateRunes().Count();
			if ((schema.TryGetProperty("minLength", out var minimum) && length < minimum.GetInt32()) ||
				(schema.TryGetProperty("maxLength", out var maximum) && length > maximum.GetInt32()))
				throw new InvalidOperationException($"Windows AI returned an invalid string length for {path}.");
			if (schema.TryGetProperty("pattern", out var pattern))
			{
				try
				{
					if (!Regex.IsMatch(
						text,
						pattern.GetString()!,
						RegexOptions.ECMAScript | RegexOptions.CultureInvariant,
						TimeSpan.FromSeconds(1)))
						throw new InvalidOperationException($"Windows AI returned an invalid pattern for {path}.");
				}
				catch (RegexMatchTimeoutException ex)
				{
					throw new InvalidOperationException($"Windows AI pattern validation timed out for {path}.", ex);
				}
			}
		}
		else if (type is "integer" or "number")
		{
			if ((schema.TryGetProperty("minimum", out var minimum) &&
					CompareJsonNumbers(value, minimum) < 0) ||
				(schema.TryGetProperty("maximum", out var maximum) &&
					CompareJsonNumbers(value, maximum) > 0))
				throw new InvalidOperationException($"Windows AI returned an out-of-range number for {path}.");
		}
	}

	private static bool IsJsonInteger(JsonElement value)
	{
		var number = ParseJsonNumber(value);
		return number.Digits == "0" || number.Exponent >= BigInteger.Zero;
	}

	private static int CompareJsonNumbers(JsonElement left, JsonElement right)
	{
		var leftNumber = ParseJsonNumber(left);
		var rightNumber = ParseJsonNumber(right);
		if (leftNumber.Digits == "0")
			return rightNumber.Digits == "0" ? 0 : rightNumber.Negative ? 1 : -1;
		if (rightNumber.Digits == "0")
			return leftNumber.Negative ? -1 : 1;
		if (leftNumber.Negative != rightNumber.Negative)
			return leftNumber.Negative ? -1 : 1;

		var leftOrder = leftNumber.Exponent + leftNumber.Digits.Length;
		var rightOrder = rightNumber.Exponent + rightNumber.Digits.Length;
		var comparison = leftOrder.CompareTo(rightOrder);
		if (comparison == 0)
		{
			var width = Math.Max(leftNumber.Digits.Length, rightNumber.Digits.Length);
			var leftCoefficient = BigInteger.Parse(leftNumber.Digits, CultureInfo.InvariantCulture) *
				BigInteger.Pow(10, width - leftNumber.Digits.Length);
			var rightCoefficient = BigInteger.Parse(rightNumber.Digits, CultureInfo.InvariantCulture) *
				BigInteger.Pow(10, width - rightNumber.Digits.Length);
			comparison = leftCoefficient.CompareTo(rightCoefficient);
		}

		return leftNumber.Negative ? -comparison : comparison;
	}

	private static JsonNumber ParseJsonNumber(JsonElement value)
	{
		var raw = value.GetRawText();
		var negative = raw[0] == '-';
		if (negative)
			raw = raw[1..];

		var exponentIndex = raw.IndexOfAny('e', 'E');
		var exponent = exponentIndex >= 0
			? BigInteger.Parse(raw[(exponentIndex + 1)..], CultureInfo.InvariantCulture)
			: BigInteger.Zero;
		var significand = exponentIndex >= 0 ? raw[..exponentIndex] : raw;
		var decimalIndex = significand.IndexOf('.');
		if (decimalIndex >= 0)
		{
			exponent -= significand.Length - decimalIndex - 1;
			significand = significand.Remove(decimalIndex, 1);
		}

		var digits = significand.TrimStart('0');
		if (digits.Length == 0)
			return new(false, "0", BigInteger.Zero);
		while (digits.Length > 1 && digits[^1] == '0')
		{
			digits = digits[..^1];
			exponent++;
		}
		return new(negative, digits, exponent);
	}

	private readonly record struct JsonNumber(
		bool Negative, string Digits, BigInteger Exponent);
}
