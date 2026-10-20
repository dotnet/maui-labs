using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Essentials.AI;

internal enum AppleJsonSchemaMode
{
	Response,
	Tool,
}

internal sealed class AppleJsonSchemaException(string path, string reason)
	: NotSupportedException($"Invalid Apple JSON schema at {path}: {reason}")
{
	internal string Path { get; } = path;
}

/// <summary>Compiles original JSON Schema into the one profile consumed by the Apple bridge.</summary>
internal sealed class AppleJsonSchemaCompiler(int capacity = 64)
{
	internal static AppleJsonSchemaCompiler Shared { get; } = new();

	private readonly object _gate = new();
	private readonly Dictionary<(string Source, AppleJsonSchemaMode Mode), string> _cache = new();
	private readonly Queue<(string Source, AppleJsonSchemaMode Mode)> _order = new();

	internal int CachedSchemaCount { get { lock (_gate) return _cache.Count; } }

	internal string Compile(JsonElement source, AppleJsonSchemaMode mode)
	{
		var key = (source.GetRawText(), mode);
		lock (_gate)
		{
			if (_cache.TryGetValue(key, out var cached))
				return cached;
		}

		var schema = Normalize(source, "$", mode, root: true)
			?? throw Error("$", "No value satisfies this schema; supply a nonempty, representable schema.");
		var nextName = 0;
		FinalizeProfile(schema, ref nextName);
		var result = schema.ToJsonString();

		// Retain only a bounded number of source strings, never the caller's JsonDocument or function.
		lock (_gate)
		{
			if (capacity > 0 && !_cache.ContainsKey(key))
			{
				while (_cache.Count >= capacity)
					_cache.Remove(_order.Dequeue());
				_cache.Add(key, result);
				_order.Enqueue(key);
			}
		}
		return result;
	}

	private static bool Annotation(string key) => key is
		"$schema" or "$id" or "$comment" or "$defs" or "definitions" or
		"title" or "description" or "default" or "examples" or "format";

	private static bool Supported(string key) => Annotation(key) || key is
		"type" or "properties" or "required" or "additionalProperties" or "items" or
		"enum" or "const" or "minimum" or "maximum" or "minItems" or "maxItems" or "anyOf";

	private static JsonObject? Normalize(JsonElement source, string path, AppleJsonSchemaMode mode, bool root = false)
	{
		if (source.ValueKind is JsonValueKind.True or JsonValueKind.False)
			throw Error(path, "Boolean schemas are not representable; supply an explicit type or simple anyOf.");
		RequireKind(source, JsonValueKind.Object, path, "a schema object");
		var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		foreach (var field in source.EnumerateObject())
		{
			var fieldPath = At(path, field.Name);
			if (!fields.TryAdd(field.Name, field.Value))
				throw Malformed(fieldPath, "Duplicate schema keywords are not allowed.");
			if (field.Name is "$ref" or "$dynamicRef" or "$recursiveRef" or "$dynamicAnchor" or "$recursiveAnchor")
				throw Error(fieldPath, $"'{field.Name}' is not supported; inline the referenced schema.");
			if (field.Name == "pattern")
				throw Error(fieldPath, "pattern is not supported by Apple's current JSON Schema mapping; native regex guides do not faithfully implement JSON Schema patterns. Use already-filtered string enum/const choices or validate the generated string separately.");
			if (!Supported(field.Name))
				throw Error(fieldPath, $"'{field.Name}' is not supported; remove it or supply a representable type or simple anyOf.");
		}

		string? description = null;
		if (fields.TryGetValue("description", out var desc))
			description = String(desc, At(path, "description"));

		if (fields.TryGetValue("anyOf", out var anyOf))
		{
			foreach (var field in fields.Keys)
				if (field != "anyOf" && !Annotation(field))
					throw Error(At(path, field), "anyOf supports only annotation siblings; move each assertion into a complete branch.");
			RequireKind(anyOf, JsonValueKind.Array, At(path, "anyOf"), "a nonempty array of schemas");
			if (anyOf.GetArrayLength() == 0)
				throw Malformed(At(path, "anyOf"), "anyOf must contain at least one schema.");
			var branches = new List<JsonObject>();
			var index = 0;
			foreach (var branch in anyOf.EnumerateArray())
			{
				var normalized = Normalize(branch, $"{At(path, "anyOf")}[{index++}]", mode);
				if (normalized is not null)
					branches.Add(normalized);
			}
			return Union(branches, description);
		}

		List<string?>? literals = null;
		if (fields.TryGetValue("enum", out var enumValue))
		{
			RequireKind(enumValue, JsonValueKind.Array, At(path, "enum"), "a nonempty array of string or null literals");
			if (enumValue.GetArrayLength() == 0)
				throw Malformed(At(path, "enum"), "enum must not be empty.");
			literals = [];
			var index = 0;
			foreach (var value in enumValue.EnumerateArray())
			{
				var literal = Literal(value, $"{At(path, "enum")}[{index++}]");
				if (!literals.Contains(literal, StringComparer.Ordinal))
					literals.Add(literal);
			}
		}
		var hasConst = fields.TryGetValue("const", out var constant);
		if (hasConst)
		{
			var literal = Literal(constant, At(path, "const"));
			literals = literals is null ? [literal] : literals.Where(v => StringComparer.Ordinal.Equals(v, literal)).ToList();
		}

		List<string> types = [];
		if (fields.TryGetValue("type", out var typeValue))
		{
			if (typeValue.ValueKind == JsonValueKind.String)
				types.Add(String(typeValue, At(path, "type")));
			else
			{
				RequireKind(typeValue, JsonValueKind.Array, At(path, "type"), "a type string or nonempty array of distinct type strings");
				var index = 0;
				foreach (var type in typeValue.EnumerateArray())
					types.Add(String(type, $"{At(path, "type")}[{index++}]"));
			}
			if (types.Count == 0 || types.Distinct(StringComparer.Ordinal).Count() != types.Count)
				throw Malformed(At(path, "type"), "Specify a type string or nonempty array of distinct type strings.");
		}
		else if (literals is not null)
		{
			if (literals.Any(v => v is not null)) types.Add("string");
			if (literals.Contains(null)) types.Add("null");
		}
		else if (root && mode == AppleJsonSchemaMode.Tool && fields.Keys.All(Annotation))
			types.Add("object");
		else
			throw Error(path, "Specify an explicit type or simple anyOf; unconstrained schemas and type inference from constraints are not supported.");

		for (var typeIndex = 0; typeIndex < types.Count; typeIndex++)
		{
			var type = types[typeIndex];
			if (type is not ("object" or "array" or "string" or "integer" or "number" or "boolean" or "null"))
				throw Error(typeValue.ValueKind == JsonValueKind.Array ? $"{At(path, "type")}[{typeIndex}]" : At(path, "type"),
					$"Unsupported type '{type}'; use object, array, string, integer, number, boolean, or null.");
		}

		void Needs(string keyword, params string[] matchingTypes)
		{
			if (fields.ContainsKey(keyword) && !types.Any(matchingTypes.Contains))
				throw Error(At(path, keyword), $"'{keyword}' requires an explicit {string.Join(" or ", matchingTypes)} type.");
		}
		foreach (var key in new[] { "properties", "required", "additionalProperties" }) Needs(key, "object");
		foreach (var key in new[] { "items", "minItems", "maxItems" }) Needs(key, "array");
		Needs("minimum", "integer", "number");
		Needs("maximum", "integer", "number");

		// Validate every assertion and child before pruning an impossible branch.
		JsonObject? objectSchema = null;
		if (types.Contains("object"))
		{
			if (fields.TryGetValue("additionalProperties", out var additional) && additional.ValueKind != JsonValueKind.False)
				throw Error(At(path, "additionalProperties"), "Open objects and dictionaries are not supported; use named properties and additionalProperties: false.");
			var properties = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
			if (fields.TryGetValue("properties", out var propertyValue))
			{
				RequireKind(propertyValue, JsonValueKind.Object, At(path, "properties"), "an object of named schemas");
				foreach (var property in propertyValue.EnumerateObject())
					if (!properties.TryAdd(property.Name, property.Value))
						throw Malformed(At(At(path, "properties"), property.Name), "Duplicate property schemas are not allowed.");
			}
			else if (!(fields.TryGetValue("additionalProperties", out additional) && additional.ValueKind == JsonValueKind.False) &&
				!(root && mode == AppleJsonSchemaMode.Tool && fields.Keys.All(Annotation)))
				throw Error(path, "An object needs named properties or explicit additionalProperties: false; arbitrary objects are not supported.");

			var required = new SortedSet<string>(StringComparer.Ordinal);
			if (fields.TryGetValue("required", out var requiredValue))
			{
				RequireKind(requiredValue, JsonValueKind.Array, At(path, "required"), "an array of distinct property names");
				var index = 0;
				foreach (var value in requiredValue.EnumerateArray())
				{
					var name = String(value, $"{At(path, "required")}[{index++}]");
					if (!required.Add(name))
						throw Malformed(At(path, "required"), $"Duplicate required property '{name}'.");
					if (!properties.ContainsKey(name))
						throw Error(At(path, "required"), $"Required property '{name}' has no schema; define every required property.");
				}
			}
			if (mode == AppleJsonSchemaMode.Response)
				required.UnionWith(properties.Keys);
			var outputProperties = new JsonObject();
			var impossible = false;
			foreach (var property in properties)
			{
				var normalized = Normalize(property.Value, At(At(path, "properties"), property.Key), mode);
				if (normalized is null)
					impossible |= required.Contains(property.Key);
				else
					outputProperties.Add(property.Key, normalized);
			}
			if (!impossible)
			{
				objectSchema = Typed("object", description);
				objectSchema["properties"] = outputProperties;
				objectSchema["required"] = new JsonArray(required.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
				objectSchema["additionalProperties"] = false;
			}
		}

		JsonObject? arraySchema = null;
		if (types.Contains("array"))
		{
			var minimum = ItemBound(fields, "minItems", path);
			var maximum = ItemBound(fields, "maxItems", path);
			if (!fields.TryGetValue("items", out var items))
				throw Error(At(path, "items"), "Arrays require a complete items schema; tuple and unconstrained items are not supported.");
			var itemSchema = Normalize(items, At(path, "items"), mode);
			if (itemSchema is null && minimum.GetValueOrDefault() == 0 && !(maximum < minimum))
				throw Error(At(path, "items"), "An impossible items schema permitting only an empty array cannot be mapped; use maxItems: 0 with a representable items schema.");
			if (!(maximum < minimum) && itemSchema is not null)
			{
				arraySchema = Typed("array", description);
				arraySchema["items"] = itemSchema;
				if (minimum.HasValue) arraySchema["minItems"] = minimum.Value;
				if (maximum.HasValue) arraySchema["maxItems"] = maximum.Value;
			}
		}

		var lower = Bound(fields, "minimum", path);
		var upper = Bound(fields, "maximum", path);
		var choices = new List<JsonObject>();
		foreach (var type in types)
		{
			JsonObject? branch = null;
			switch (type)
			{
				case "object": branch = objectSchema; break;
				case "array": branch = arraySchema; break;
				case "string":
					var strings = literals?.Where(v => v is not null).ToArray();
					if (strings is not { Length: 0 })
					{
						branch = Typed(type, description);
						if (strings is not null)
						{
							if (hasConst) branch["const"] = strings[0];
							else branch["enum"] = new JsonArray(strings.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
						}
					}
					break;
				case "integer":
					var minInt = lower?.IntegerBound(minimum: true, At(path, "minimum"));
					var maxInt = upper?.IntegerBound(minimum: false, At(path, "maximum"));
					ValidateIntegerTransport(minInt, At(path, "minimum"));
					ValidateIntegerTransport(maxInt, At(path, "maximum"));
					if (!(minInt > maxInt))
					{
						branch = Typed(type, description);
						if (minInt.HasValue) branch["minimum"] = minInt.Value;
						if (maxInt.HasValue) branch["maximum"] = maxInt.Value;
					}
					break;
				case "number":
					var minDouble = lower?.DoubleBound(At(path, "minimum"));
					var maxDouble = upper?.DoubleBound(At(path, "maximum"));
					if (!(minDouble > maxDouble))
					{
						branch = Typed(type, description);
						if (minDouble.HasValue) branch["minimum"] = minDouble.Value;
						if (maxDouble.HasValue) branch["maximum"] = maxDouble.Value;
					}
					break;
				case "boolean": branch = Typed(type, description); break;
				case "null":
					if (literals is null || literals.Contains(null)) branch = Typed(type, description);
					break;
			}
			if (branch is not null && (literals is null || type is "string" or "null"))
				choices.Add(branch);
		}
		return Union(choices, description);
	}

	private static JsonObject Typed(string type, string? description)
	{
		var result = new JsonObject { ["type"] = type };
		if (description is not null) result["description"] = description;
		return result;
	}

	private static JsonObject? Union(List<JsonObject> branches, string? description)
	{
		if (branches.Count == 0) return null;
		if (branches.Count == 1)
		{
			if (description is not null) branches[0]["description"] = description;
			return branches[0];
		}
		var union = new JsonObject();
		if (description is not null) union["description"] = description;
		union["anyOf"] = new JsonArray(branches.Cast<JsonNode?>().ToArray());
		return union;
	}

	private static void FinalizeProfile(JsonObject schema, ref int next, bool isProperty = false)
	{
		if (schema.ContainsKey("anyOf") || schema["type"]?.GetValue<string>() == "object")
		{
			// Insert identity immediately after type, or first for unions.
			schema.Insert(schema.ContainsKey("type") ? 1 : 0, "title", JsonValue.Create($"AppleSchema{next++}"));
		}
		else if (!isProperty)
			schema.Remove("description");
		if (schema["properties"] is JsonObject properties)
			foreach (var property in properties) FinalizeProfile((JsonObject)property.Value!, ref next, isProperty: true);
		if (schema["items"] is JsonObject items) FinalizeProfile(items, ref next);
		if (schema["anyOf"] is JsonArray branches)
			foreach (var branch in branches) FinalizeProfile((JsonObject)branch!, ref next);
	}

	private static string? Literal(JsonElement value, string path) => value.ValueKind switch
	{
		JsonValueKind.String => value.GetString(),
		JsonValueKind.Null => null,
		_ => throw Error(path, "Only string or null enum/const literals are supported; replace other literal types with representable schemas."),
	};

	private static string String(JsonElement value, string path)
	{
		RequireKind(value, JsonValueKind.String, path, "a string");
		return value.GetString()!;
	}

	private static void RequireKind(JsonElement value, JsonValueKind kind, string path, string expected)
	{
		if (value.ValueKind != kind)
			throw Malformed(path, $"Expected {expected}, not {value.ValueKind}.");
	}

	private static AppleJsonSchemaException Error(string path, string reason) => new(path, reason);
	private static JsonException Malformed(string path, string reason) =>
		new($"Malformed Apple JSON schema at {path}: {reason}", path, null, null);

	private static string At(string path, string key) =>
		key.Length > 0 && (char.IsAsciiLetter(key[0]) || key[0] == '_') &&
		key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
			? $"{path}.{key}"
			: $"{path}[\"{JsonEncodedText.Encode(key)}\"]";

	private static void ValidateIntegerTransport(long? value, string path)
	{
		if (value is < -9007199254740991 or > 9007199254740991)
			throw Error(path, "Apple's native generated JSON cannot reliably preserve integer bounds outside [-9007199254740991,9007199254740991]; use a bound inside this exact transport interval or model the value as a string.");
	}

	/// <summary>Checks only the compiled profile, guarding against native guide/transport violations.</summary>
	internal static void ValidateOutput(string canonicalSchema, string json, string context = "Generated response")
	{
		using var schema = JsonDocument.Parse(canonicalSchema);
		try
		{
			using var output = JsonDocument.Parse(json);
			if (ValidateValue(schema.RootElement, output.RootElement, "$", "$") is { } failure)
				throw new InvalidDataException($"{context} violates the Apple JSON schema: {failure}");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException($"{context} is not valid JSON at {error.Path ?? "$"}.", error);
		}
	}

	private static string? ValidateValue(JsonElement schema, JsonElement value, string schemaPath, string valuePath)
	{
		string Invalid(string reason, string? keyword = null) =>
			$"value at {valuePath}, schema at {(keyword is null ? schemaPath : At(schemaPath, keyword))}: {reason}";
		if (schema.TryGetProperty("anyOf", out var branches))
		{
			var index = 0;
			foreach (var branch in branches.EnumerateArray())
				if (ValidateValue(branch, value, $"{schemaPath}.anyOf[{index++}]", valuePath) is null)
					return null;
			return Invalid("No canonical anyOf branch accepts this value.", "anyOf");
		}

		var type = schema.GetProperty("type").GetString();
		switch (type)
		{
			case "object":
				if (value.ValueKind != JsonValueKind.Object) return Invalid("Expected an object.");
				var properties = schema.GetProperty("properties");
				foreach (var required in schema.GetProperty("required").EnumerateArray())
					if (!value.TryGetProperty(required.GetString()!, out _))
						return Invalid($"Required property '{required.GetString()}' is missing.", "required");
				var names = new HashSet<string>(StringComparer.Ordinal);
				foreach (var property in value.EnumerateObject())
				{
					if (!names.Add(property.Name))
						return Invalid($"Duplicate property '{property.Name}'.");
					if (!properties.TryGetProperty(property.Name, out var propertySchema))
						return Invalid($"Property '{property.Name}' is not allowed in this closed object.", "additionalProperties");
					var failure = ValidateValue(propertySchema, property.Value,
						At(At(schemaPath, "properties"), property.Name), At(valuePath, property.Name));
					if (failure is not null) return failure;
				}
				return null;
			case "array":
				if (value.ValueKind != JsonValueKind.Array) return Invalid("Expected an array.");
				var count = value.GetArrayLength();
				if (schema.TryGetProperty("minItems", out var minItems) && count < minItems.GetInt64())
					return Invalid("Array has fewer items than minItems.", "minItems");
				if (schema.TryGetProperty("maxItems", out var maxItems) && count > maxItems.GetInt64())
					return Invalid("Array has more items than maxItems.", "maxItems");
				var index = 0;
				foreach (var item in value.EnumerateArray())
				{
					var failure = ValidateValue(schema.GetProperty("items"), item, $"{schemaPath}.items", $"{valuePath}[{index++}]");
					if (failure is not null) return failure;
				}
				return null;
			case "string":
				if (value.ValueKind != JsonValueKind.String) return Invalid("Expected a string.");
				var text = value.GetString();
				if (schema.TryGetProperty("const", out var constant) && !StringComparer.Ordinal.Equals(text, constant.GetString()))
					return Invalid("String does not equal const.", "const");
				if (schema.TryGetProperty("enum", out var choices) &&
					!choices.EnumerateArray().Any(choice => StringComparer.Ordinal.Equals(text, choice.GetString())))
					return Invalid("String is not an enum choice.", "enum");
				return null;
			case "integer":
			case "number":
				if (value.ValueKind != JsonValueKind.Number) return Invalid($"Expected a {type}.");
				var number = ExactDecimal.Parse(value, valuePath);
				if (type == "integer")
				{
					if (!number.IsInteger) return Invalid("Expected an integral value.");
					// Int64 is the native schema type even when no explicit bounds are present.
					try { number.IntegerBound(minimum: true, valuePath); }
					catch (AppleJsonSchemaException) { return Invalid("Expected an Int64 integer."); }
				}
				if (schema.TryGetProperty("minimum", out var minimum) && number.CompareTo(ExactDecimal.Parse(minimum, schemaPath)) < 0)
					return Invalid("Number is less than minimum.", "minimum");
				if (schema.TryGetProperty("maximum", out var maximum) && number.CompareTo(ExactDecimal.Parse(maximum, schemaPath)) > 0)
					return Invalid("Number is greater than maximum.", "maximum");
				return null;
			case "boolean":
				return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : Invalid("Expected a boolean.");
			case "null":
				return value.ValueKind == JsonValueKind.Null ? null : Invalid("Expected null.");
			default: throw new InvalidOperationException("Output validation requires a compiled Apple profile.");
		}
	}

	private static ExactDecimal? Bound(Dictionary<string, JsonElement> fields, string key, string path) =>
		fields.TryGetValue(key, out var value) ? ExactDecimal.Parse(value, At(path, key)) : null;

	private static long? ItemBound(Dictionary<string, JsonElement> fields, string key, string path)
	{
		var value = Bound(fields, key, path);
		if (value is null) return null;
		if (!value.Value.IsInteger || value.Value.Negative)
			throw Error(At(path, key), "Array bounds must be exact nonnegative Int64 integers; supply an integral bound.");
		return value.Value.IntegerBound(minimum: true, At(path, key));
	}

	// A normalized decimal coefficient/exponent preserves every source digit, including beyond
	// System.Decimal precision. Huge exponents are compared, never expanded into huge powers of ten.
	private readonly record struct ExactDecimal(bool Negative, string Digits, BigInteger Exponent)
	{
		internal bool IsInteger => Digits == "0" || Exponent >= 0;

		internal static ExactDecimal Parse(JsonElement value, string path)
		{
			RequireKind(value, JsonValueKind.Number, path, "a JSON number");
			return Parse(value.GetRawText());
		}

		private static ExactDecimal Parse(string token)
		{
			var negative = token.StartsWith('-');
			var parts = token.TrimStart('-').Split(['e', 'E']);
			var point = parts[0].IndexOf('.');
			var exponent = (parts.Length == 2 ? BigInteger.Parse(parts[1], CultureInfo.InvariantCulture) : BigInteger.Zero)
				- (point >= 0 ? parts[0].Length - point - 1 : 0);
			var digits = parts[0].Replace(".", "").TrimStart('0');
			if (digits.Length == 0) return new(false, "0", 0);
			var trimmed = digits.TrimEnd('0');
			exponent += digits.Length - trimmed.Length;
			return new(negative, trimmed, exponent);
		}

		internal long IntegerBound(bool minimum, string path)
		{
			var integralDigits = (BigInteger)Digits.Length + Exponent;
			if (Digits != "0" && integralDigits > 19)
				throw Error(path, "The rounded integer bound is outside Int64; supply an Int64-representable bound.");
			BigInteger magnitude;
			var fraction = Exponent < 0 && Digits != "0";
			if (Digits == "0" || integralDigits <= 0)
				magnitude = 0;
			else if (Exponent >= 0)
				magnitude = BigInteger.Parse(Digits, CultureInfo.InvariantCulture) * BigInteger.Pow(10, (int)Exponent);
			else
				magnitude = BigInteger.Parse(Digits[..(int)integralDigits], CultureInfo.InvariantCulture);
			if (fraction && minimum != Negative) magnitude++;
			var result = Negative ? -magnitude : magnitude;
			if (result < long.MinValue || result > long.MaxValue)
				throw Error(path, "The rounded integer bound is outside Int64; supply an Int64-representable bound.");
			return (long)result;
		}

		internal double DoubleBound(string path)
		{
			var token = (Negative ? "-" : "") + Digits + "e" + Exponent.ToString(CultureInfo.InvariantCulture);
			if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
				!double.IsFinite(value) || Parse(value.ToString("R", CultureInfo.InvariantCulture)) != this)
				throw Error(path, "The number bound cannot preserve its exact decimal token as a finite Double; supply a shortest-round-trip representable bound.");
			return value;
		}

		internal int CompareTo(ExactDecimal other)
		{
			if (Negative != other.Negative) return Negative ? -1 : 1;
			var sign = Negative ? -1 : 1;
			if (Digits == "0" || other.Digits == "0")
				return sign * (Digits == other.Digits ? 0 : Digits == "0" ? -1 : 1);
			var order = ((BigInteger)Digits.Length + Exponent).CompareTo((BigInteger)other.Digits.Length + other.Exponent);
			if (order != 0) return sign * order;
			for (var index = 0; index < Math.Max(Digits.Length, other.Digits.Length); index++)
			{
				var a = index < Digits.Length ? Digits[index] : '0';
				var b = index < other.Digits.Length ? other.Digits[index] : '0';
				if (a != b) return sign * a.CompareTo(b);
			}
			return 0;
		}
	}

}
