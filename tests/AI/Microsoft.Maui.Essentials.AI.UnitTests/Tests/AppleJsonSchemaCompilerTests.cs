using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

// Apple-specific profile policy and native identities are not cross-provider JSON Schema contracts.
public class AppleJsonSchemaCompilerTests
{
	private static string Compile(string json, AppleJsonSchemaMode mode = AppleJsonSchemaMode.Tool, AppleJsonSchemaCompiler? compiler = null)
	{
		using var document = JsonDocument.Parse(json);
		return (compiler ?? new()).Compile(document.RootElement, mode);
	}

	[Fact]
	public void Compile_NullableAndOptional_UsesOneCanonicalProfileWithDistinctRequiredModes()
	{
		const string source = """
			{"type":"object","title":"untrusted","properties":{
			  "label":{"type":["string","null"],"default":null},
			  "count":{"type":["integer","null"]}
			},"required":["count"]}
			""";
		const string expected = """
			{"type":"object","title":"AppleSchema0","properties":{"count":{"title":"AppleSchema1","anyOf":[{"type":"integer"},{"type":"null"}]},"label":{"title":"AppleSchema2","anyOf":[{"type":"string"},{"type":"null"}]}},"required":["count"],"additionalProperties":false}
			""";
		Assert.Equal(expected, Compile(source));
		Assert.Equal(expected.Replace("\"required\":[\"count\"]", "\"required\":[\"count\",\"label\"]"),
			Compile(source, AppleJsonSchemaMode.Response));
	}

	[Fact]
	public void Compile_ObjectsArraysAndUnions_AllocatesNamesInCanonicalPreorder()
	{
		const string source = """
			{"type":"object","title":"AppleSchema1","properties":{
			  "a.b":{"type":["object","null"],"title":"duplicate","properties":{"x":{"type":"string"}}},
			  "a":{"type":"array","items":{"anyOf":[
			    {"type":"object","title":"duplicate","properties":{}},
			    {"type":"object","title":"duplicate","properties":{"z":{"type":"integer"}}}
			  ]}}
			},"required":["a.b","a"]}
			""";
		Assert.Equal("""
			{"type":"object","title":"AppleSchema0","properties":{"a":{"type":"array","items":{"title":"AppleSchema1","anyOf":[{"type":"object","title":"AppleSchema2","properties":{},"required":[],"additionalProperties":false},{"type":"object","title":"AppleSchema3","properties":{"z":{"type":"integer"}},"required":[],"additionalProperties":false}]}},"a.b":{"title":"AppleSchema4","anyOf":[{"type":"object","title":"AppleSchema5","properties":{"x":{"type":"string"}},"required":[],"additionalProperties":false},{"type":"null"}]}},"required":["a","a.b"],"additionalProperties":false}
			""", Compile(source));
	}

	[Fact]
	public void Compile_ResponseMode_RequiresPropertiesRecursivelyButNotNullValues()
	{
		var result = Compile("""
			{"type":"object","properties":{"child":{"type":["object","null"],"properties":{
			"name":{"type":["string","null"]}}},"list":{"type":"array","items":{"type":"object","properties":{"value":{"type":"boolean"}}}}}}
			""", AppleJsonSchemaMode.Response);
		using var doc = JsonDocument.Parse(result);
		var properties = doc.RootElement.GetProperty("properties");
		Assert.Equal("[\"child\",\"list\"]", doc.RootElement.GetProperty("required").GetRawText());
		var child = properties.GetProperty("child").GetProperty("anyOf");
		Assert.Equal("null", child[1].GetProperty("type").GetString());
		Assert.Equal("[\"name\"]", child[0].GetProperty("required").GetRawText());
		Assert.Equal("[\"value\"]", properties.GetProperty("list").GetProperty("items").GetProperty("required").GetRawText());
	}

	[Fact]
	public void Compile_RealMeaiResponseSchema_StripsFormatsWithoutWeakeningValueTypes()
	{
		var format = Assert.IsType<ChatResponseFormatJson>(ChatResponseFormat.ForJsonSchema<FormattedResponse>(JsonSerializerOptions.Web));
		var source = format.Schema!.Value;
		Assert.Contains("\"uuid\"", source.GetRawText(), StringComparison.Ordinal);
		Assert.Contains("\"date-time\"", source.GetRawText(), StringComparison.Ordinal);
		var result = new AppleJsonSchemaCompiler().Compile(source, AppleJsonSchemaMode.Response);
		Assert.DoesNotContain("\"format\"", result, StringComparison.Ordinal);
		using var doc = JsonDocument.Parse(result);
		var properties = doc.RootElement.GetProperty("properties");
		Assert.Equal("string", properties.GetProperty("id").GetProperty("type").GetString());
		Assert.Equal("string", properties.GetProperty("timestamp").GetProperty("type").GetString());
		Assert.Contains("nested", doc.RootElement.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
		Assert.Contains("description", result, StringComparison.Ordinal);
	}

	[Fact]
	public void Compile_RealMeaiFunctionSchema_RetainsOptionalDefaultsAndNullableIndependently()
	{
		static string Capture(Guid id, DateTimeOffset timestamp, int? count, string? label = null) => "unused";
		var tool = AIFunctionFactory.Create(Capture, "capture");
		var result = new AppleJsonSchemaCompiler().Compile(tool.JsonSchema, AppleJsonSchemaMode.Tool);
		using var doc = JsonDocument.Parse(result);
		Assert.Equal("[\"count\",\"id\",\"timestamp\"]", doc.RootElement.GetProperty("required").GetRawText());
		Assert.Equal("null", doc.RootElement.GetProperty("properties").GetProperty("label").GetProperty("anyOf")[1].GetProperty("type").GetString());
		Assert.DoesNotContain("\"default\"", result, StringComparison.Ordinal);
		Assert.DoesNotContain("\"format\"", result, StringComparison.Ordinal);
	}

	[Fact]
	public void Compile_RealMeaiNoArgumentFunction_EmitsClosedEmptyObject()
	{
		var tool = AIFunctionFactory.Create(() => "unused", "empty");
		Assert.Equal(Compile("{}"), new AppleJsonSchemaCompiler().Compile(tool.JsonSchema, AppleJsonSchemaMode.Tool));
		Assert.Equal("""{"type":"object","title":"AppleSchema0","properties":{},"required":[],"additionalProperties":false}""", Compile("{}"));
	}

	[Fact]
	public void Compile_RealMeaiDictionarySchemas_RejectsOriginalAdditionalProperties()
	{
		static string Capture(Dictionary<string, string> data) => "unused";
		var tool = AIFunctionFactory.Create(Capture, "dictionary");
		var error = Assert.Throws<AppleJsonSchemaException>(() => new AppleJsonSchemaCompiler().Compile(tool.JsonSchema, AppleJsonSchemaMode.Tool));
		Assert.Equal("$.properties.data.additionalProperties", error.Path);
		var format = Assert.IsType<ChatResponseFormatJson>(ChatResponseFormat.ForJsonSchema<Dictionary<string, string>>());
		error = Assert.Throws<AppleJsonSchemaException>(() => new AppleJsonSchemaCompiler().Compile(format.Schema!.Value, AppleJsonSchemaMode.Response));
		Assert.Equal("$.additionalProperties", error.Path);
	}

	[Theory]
	[InlineData("""{"type":"object"}""", "$")]
	[InlineData("""{"type":"object","properties":{},"additionalProperties":true}""", "$.additionalProperties")]
	[InlineData("""{"type":"object","properties":{},"additionalProperties":{"type":"string"}}""", "$.additionalProperties")]
	[InlineData("""{"type":"object","properties":{"nested":{}}}""", "$.properties.nested")]
	[InlineData("""true""", "$")]
	[InlineData("""false""", "$")]
	[InlineData("""{"minimum":1}""", "$")]
	[InlineData("""{"properties":{}}""", "$")]
	public void Compile_UnconstrainedAndOpenSchemas_RejectsWithoutInferredFallback(string source, string path)
	{
		Assert.Equal(path, Assert.Throws<AppleJsonSchemaException>(() => Compile(source)).Path);
	}

	[Fact]
	public void Compile_ResponseEmptySchema_IsNotTheNoArgumentToolConvention()
	{
		Assert.Throws<AppleJsonSchemaException>(() => Compile("{}", AppleJsonSchemaMode.Response));
		Assert.Equal(Compile("{}"), Compile("""{"type":"object","additionalProperties":false}"""));
	}

	[Theory]
	[InlineData("$ref")]
	[InlineData("$dynamicRef")]
	[InlineData("$recursiveRef")]
	public void Compile_ReferencesAlwaysFailWithInlineCorrection(string keyword)
	{
		var error = Assert.Throws<AppleJsonSchemaException>(() => Compile(
			"""{"type":"object","properties":{"a.b":{"REFERENCE":"#/$defs/T"}},"$defs":{"T":{"type":"string"}}}""".Replace("REFERENCE", keyword)));
		Assert.Equal($"$.properties[\"a.b\"][\"{keyword}\"]", error.Path);
		Assert.Contains("inline", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("allOf", "[{\"type\":\"string\"}]")]
	[InlineData("oneOf", "[{\"type\":\"string\"}]")]
	[InlineData("not", "{}")]
	[InlineData("if", "{}")]
	[InlineData("then", "{}")]
	[InlineData("else", "{}")]
	[InlineData("dependentSchemas", "{}")]
	[InlineData("dependentRequired", "{}")]
	[InlineData("dependencies", "{}")]
	[InlineData("exclusiveMinimum", "0")]
	[InlineData("exclusiveMaximum", "1")]
	[InlineData("multipleOf", "1")]
	[InlineData("minLength", "0")]
	[InlineData("maxLength", "5")]
	[InlineData("minProperties", "0")]
	[InlineData("maxProperties", "5")]
	[InlineData("patternProperties", "{}")]
	[InlineData("propertyNames", "{}")]
	[InlineData("unevaluatedProperties", "false")]
	[InlineData("prefixItems", "[]")]
	[InlineData("additionalItems", "false")]
	[InlineData("uniqueItems", "true")]
	[InlineData("contains", "{}")]
	[InlineData("minContains", "1")]
	[InlineData("maxContains", "5")]
	[InlineData("unevaluatedItems", "false")]
	[InlineData("customConstraint", "1")]
	[InlineData("pattern", "\"A\"")]
	public void Compile_UnsupportedKeyword_ReportsExactSourcePathEvenInImpossibleBranch(string keyword, string value)
	{
		var error = Assert.Throws<AppleJsonSchemaException>(() => Compile(
			$$"""{"anyOf":[{"type":"integer","minimum":2,"maximum":1,"{{keyword}}":{{value}}},{"type":"string"}]}"""));
		Assert.Equal($"$.anyOf[0].{keyword}", error.Path);
		Assert.Contains(keyword, error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("type", "\"string\"")]
	[InlineData("minimum", "1")]
	[InlineData("enum", "[\"x\"]")]
	public void Compile_AnyOfAssertionSiblings_AreRejectedAtSibling(string key, string value)
	{
		Assert.Equal($"$.{key}", Assert.Throws<AppleJsonSchemaException>(() => Compile(
			$$"""{"anyOf":[{"type":"string"},{"type":"integer"}],"{{key}}":{{value}}}""")).Path);
	}

	[Theory]
	[InlineData("""{"type":"date"}""", "$.type")]
	[InlineData("""{"type":["string","date"]}""", "$.type[1]")]
	[InlineData("""{"type":"array"}""", "$.items")]
	public void Compile_InvalidTypeOrMissingArrayItems_ReportsSourceLocation(string schema, string path)
	{
		Assert.Equal(path, Assert.Throws<AppleJsonSchemaException>(() => Compile(schema)).Path);
	}

	[Fact]
	public void Compile_AnnotationsAndUnusedDefinitions_AreDiscardedButDescriptionsConsumed()
	{
		Assert.Equal("""{"type":"string"}""", Compile("""
			{"type":"string","description":"unused","title":"ignored","format":"uuid","default":"value",
			"examples":["example"],"$schema":"ignored","$id":"ignored","$comment":"ignored",
			"$defs":{"unreferenced":{"$ref":"external","unsupported":true}},"definitions":{"unused":false}}
			"""));
	}

	[Fact]
	public void Compile_Descriptions_OnlyRetainsNativeObjectUnionAndPropertyHints()
	{
		Assert.Equal("""
			{"type":"object","title":"AppleSchema0","description":"object","properties":{"array":{"type":"array","description":"property","items":{"type":"string"}},"choice":{"title":"AppleSchema1","description":"union","anyOf":[{"type":"string"},{"type":"null"}]}},"required":[],"additionalProperties":false}
			""", Compile("""
			{"type":"object","description":"object","properties":{
			  "array":{"type":"array","description":"property","items":{"type":"string","description":"unused item hint"}},
			  "choice":{"type":["string","null"],"description":"union"}
			}}
			"""));
		Assert.Equal("""{"type":"array","items":{"type":"string"}}""",
			Compile("""{"type":"array","description":"unused root hint","items":{"type":"string"}}"""));
	}

	[Fact]
	public void Compile_ImpossibleUnionBranchesAndOptionalProperties_ArePrunedBeforeNaming()
	{
		Assert.Equal("""
			{"type":"object","title":"AppleSchema0","properties":{"keep":{"type":"string"}},"required":["keep"],"additionalProperties":false}
			""", Compile("""
			{"anyOf":[
			  {"type":"object","properties":{"bad":{"type":"integer","minimum":2,"maximum":1}},"required":["bad"]},
			  {"type":"object","properties":{"drop":{"type":"string","enum":["a"],"const":"b"},"keep":{"type":"string"}},"required":["keep"]}
			]}
			"""));
		Assert.Equal("""{"type":"null"}""", Compile("""{"type":["integer","null"],"minimum":1.1,"maximum":1.9}"""));
		Assert.Equal("""{"type":"string"}""", Compile("""{"anyOf":[{"type":"array","minItems":2,"maxItems":1,"items":{"type":"boolean"}},{"type":"string"}]}"""));
	}

	[Fact]
	public void Compile_ImpossibleRequiredFields_InvalidateResponseEvenWhenSourceOptional()
	{
		const string schema = """{"type":"object","properties":{"x":{"type":"string","enum":["a"],"const":"b"}}}""";
		Assert.Equal(Compile("{}"), Compile(schema));
		Assert.Throws<AppleJsonSchemaException>(() => Compile(schema, AppleJsonSchemaMode.Response));
		Assert.Throws<AppleJsonSchemaException>(() => Compile("""{"type":"integer","minimum":0.1,"maximum":0.9}"""));
	}

	[Fact]
	public void Compile_ImpossibleObjects_DoNotHideUnsupportedOptionalSiblingSchemas()
	{
		var error = Assert.Throws<AppleJsonSchemaException>(() => Compile("""
			{"anyOf":[{"type":"object","properties":{
			"a":{"type":"integer","minimum":2,"maximum":1},
			"z":{"type":"string","minLength":1}},"required":["a"]},{"type":"null"}]}
			"""));
		Assert.Equal("$.anyOf[0].properties.z.minLength", error.Path);
	}

	[Fact]
	public void Compile_StringEnumConstIntersection_IsOrdinalAndNullIsIndependent()
	{
		Assert.Equal("""{"type":"string","const":"x"}""", Compile("""{"type":["string","null"],"enum":["x",null],"const":"x"}"""));
		Assert.Equal("""{"type":"null"}""", Compile("""{"enum":["x",null],"const":null}"""));
		Assert.Equal("""{"type":"string","const":"e\u0301"}""", Compile("""{"enum":["\u00e9","e\u0301"],"const":"e\u0301"}"""));
		var json = Compile("""{"enum":["\u00e9","e\u0301","\u00e9",null]}""");
		using var doc = JsonDocument.Parse(json);
		var choices = doc.RootElement.GetProperty("anyOf")[0].GetProperty("enum");
		Assert.Equal(2, choices.GetArrayLength());
		Assert.NotEqual(choices[0].GetString(), choices[1].GetString());
		Assert.Throws<AppleJsonSchemaException>(() => Compile("""{"enum":["\u00e9"],"const":"e\u0301"}"""));
	}

	[Theory]
	[InlineData("1")]
	[InlineData("true")]
	[InlineData("[]")]
	[InlineData("{}")]
	public void Compile_NonStringOrNullLiteral_RejectsBothEnumAndConst(string value)
	{
		Assert.Equal("$.const", Assert.Throws<AppleJsonSchemaException>(() => Compile($$"""{"const":{{value}}}""")).Path);
		Assert.Equal("$.enum[0]", Assert.Throws<AppleJsonSchemaException>(() => Compile($$"""{"enum":[{{value}}]}""")).Path);
	}

	[Theory]
	[InlineData("1.25", "2")]
	[InlineData("-1.25", "-1")]
	[InlineData("-0.1", "0")]
	[InlineData("1.0000000000000000000000000000000000000001", "2")]
	[InlineData("10000000000000000000000000000000000000001e-40", "2")]
	[InlineData("9007199254740990.999999999999999999999999", "9007199254740991")]
	[InlineData("-9007199254740991.99999999999999999999999", "-9007199254740991")]
	[InlineData("1e-999999999999999999999999", "1")]
	[InlineData("9.007199254740991e15", "9007199254740991")]
	public void Compile_IntegerMinimum_UsesExactCeiling(string token, string expected)
	{
		Assert.Equal($$"""{"type":"integer","minimum":{{expected}}}""", Compile($$"""{"type":"integer","minimum":{{token}}}"""));
	}

	[Theory]
	[InlineData("4.75", "4")]
	[InlineData("-4.75", "-5")]
	[InlineData("0.9999999999999999999999999999999999999999", "0")]
	[InlineData("-1.0000000000000000000000000000000000000001", "-2")]
	[InlineData("9007199254740991.99999999999999999999999", "9007199254740991")]
	[InlineData("-9007199254740990.99999999999999999999999", "-9007199254740991")]
	[InlineData("-1e-999999999999999999999999", "-1")]
	[InlineData("-9.007199254740991e15", "-9007199254740991")]
	public void Compile_IntegerMaximum_UsesExactFloor(string token, string expected)
	{
		Assert.Equal($$"""{"type":"integer","maximum":{{expected}}}""", Compile($$"""{"type":"integer","maximum":{{token}}}"""));
	}

	[Theory]
	[InlineData("9223372036854775808")]
	[InlineData("-9223372036854775809")]
	[InlineData("1e999999999999999999999999")]
	public void Compile_IntegerOverflow_RejectsAtBound(string token)
	{
		foreach (var key in new[] { "minimum", "maximum" })
			Assert.Equal($"$.{key}", Assert.Throws<AppleJsonSchemaException>(() => Compile($$"""{"type":"integer","{{key}}":{{token}}}""")).Path);
	}

	[Theory]
	[InlineData("9007199254740992")]
	[InlineData("9007199254740993")]
	[InlineData("-9007199254740992")]
	[InlineData("-9007199254740993")]
	[InlineData("9223372036854775807")]
	[InlineData("-9223372036854775808")]
	[InlineData("9223372036854775806.999999999999999999999999")]
	public void Compile_Int64BoundsOutsideNativeExactTransportInterval_RejectAfterExactRounding(string token)
	{
		foreach (var key in new[] { "minimum", "maximum" })
		{
			var error = Assert.Throws<AppleJsonSchemaException>(() => Compile($$"""{"type":"integer","{{key}}":{{token}}}"""));
			Assert.Equal($"$.{key}", error.Path);
			Assert.Contains("native generated JSON", error.Message, StringComparison.Ordinal);
		}
	}

	[Theory]
	[InlineData("9223372036854775807.000000000000000000000001")]
	[InlineData("-9223372036854775808.99999999999999999999999")]
	public void Compile_ExtremeFractionalBounds_AreRoundedExactlyBeforeNativePrecisionPolicy(string token)
	{
		var ceiling = Assert.Throws<AppleJsonSchemaException>(() => Compile($$"""{"type":"integer","minimum":{{token}}}"""));
		var floor = Assert.Throws<AppleJsonSchemaException>(() => Compile($$"""{"type":"integer","maximum":{{token}}}"""));
		Assert.Contains(token.StartsWith('-') ? "native generated JSON" : "outside Int64", ceiling.Message, StringComparison.Ordinal);
		Assert.Contains(token.StartsWith('-') ? "outside Int64" : "native generated JSON", floor.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("0.1", "0.1")]
	[InlineData("1.2500e0", "1.25")]
	[InlineData("1000e-3", "1")]
	[InlineData("-0.000e99999999", "0")]
	[InlineData("1e200", "1E+200")]
	[InlineData("1e-200", "1E-200")]
	[InlineData("1.7976931348623157e308", "1.7976931348623157E+308")]
	[InlineData("5e-324", "5E-324")]
	[InlineData("9007199254740992", "9007199254740992")]
	public void Compile_NumberBounds_PreserveShortestDoubleDecimalValue(string token, string expected)
	{
		Assert.Equal($$"""{"type":"number","minimum":{{expected}}}""", Compile($$"""{"type":"number","minimum":{{token}}}"""));
	}

	[Theory]
	[InlineData("0.10000000000000000000000000000000000000001")]
	[InlineData("0.1234567890123456789012345678")]
	[InlineData("9007199254740993")]
	[InlineData("1e309")]
	[InlineData("1e-400")]
	[InlineData("4.9406564584124654e-324")]
	public void Compile_NumberRoundingUnderflowOverflow_RejectsOriginalToken(string token)
	{
		Assert.Equal("$.maximum", Assert.Throws<AppleJsonSchemaException>(() => Compile($$"""{"type":"number","maximum":{{token}}}""")).Path);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("9223372036854775807")]
	[InlineData("10e-1")]
	[InlineData("2.000000000000000000000000000000000000000")]
	public void Compile_ArrayBound_RequiresExactNonnegativeInt64(string token)
	{
		var normalized = token.StartsWith("922", StringComparison.Ordinal) ? "9223372036854775807" : token == "0" ? "0" : token == "10e-1" ? "1" : "2";
		Assert.Equal($$"""{"type":"array","items":{"type":"string"},"minItems":{{normalized}}}""",
			Compile($$"""{"type":"array","items":{"type":"string"},"minItems":{{token}}}"""));
	}

	[Theory]
	[InlineData("-1")]
	[InlineData("1.0000000000000000000000000000000000000001")]
	[InlineData("9223372036854775808")]
	public void Compile_InvalidArrayBounds_RejectAtBound(string token)
	{
		Assert.Equal("$.maxItems", Assert.Throws<AppleJsonSchemaException>(() => Compile(
			$$"""{"type":"array","items":{"type":"string"},"maxItems":{{token}}}""")).Path);
	}

	[Theory]
	[InlineData("")]
	[InlineData("^$")]
	[InlineData("^A[0-9]{2}$")]
	[InlineData("a?b*c+d{0}e{1,2}f{0,}")]
	[InlineData("[-a-zA-Z0-9_ ]+")]
	[InlineData("[a-z-]")]
	[InlineData("hello world_-")]
	[InlineData("a{9223372036854775807}")]
	public void Compile_PatternsIncludingFormerlySupportedGrammar_AreRejected(string pattern)
	{
		var source = $$"""{"type":"string","pattern":{{JsonSerializer.Serialize(pattern)}}}""";
		var error = Assert.Throws<AppleJsonSchemaException>(() => Compile(source));
		Assert.Equal("$.pattern", error.Path);
		Assert.Contains("current JSON Schema mapping", error.Message, StringComparison.Ordinal);
		Assert.Contains("enum/const", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("\\d+")]
	[InlineData("\\w")]
	[InlineData("\\s")]
	[InlineData("\\u0041")]
	[InlineData("é")]
	[InlineData(".")]
	[InlineData("[^a]")]
	[InlineData("[]")]
	[InlineData("[z-a]")]
	[InlineData("[a-b-c]")]
	[InlineData("(a)")]
	[InlineData("a|b")]
	[InlineData("(?=a)")]
	[InlineData("(?i)a")]
	[InlineData("a*?")]
	[InlineData("a++")]
	[InlineData("a??")]
	[InlineData("a{01}")]
	[InlineData("a{2,1}")]
	[InlineData("a{9223372036854775808}")]
	[InlineData("a{1}{2}")]
	[InlineData("a{,1}")]
	[InlineData("[")]
	[InlineData("^a$b")]
	public void Compile_ArbitraryRegexSyntax_IsRejectedWithoutInterpretation(string pattern)
	{
		var source = $$"""{"type":"string","pattern":{{JsonSerializer.Serialize(pattern)}}}""";
		Assert.Equal("$.pattern", Assert.Throws<AppleJsonSchemaException>(() => Compile(source)).Path);
	}

	[Theory]
	[InlineData("\"enum\":[\"x\"]")]
	[InlineData("\"const\":\"x\"")]
	public void Compile_PatternWithChoices_RejectsInsteadOfDroppingConstraint(string choices)
	{
		Assert.Equal("$.pattern", Assert.Throws<AppleJsonSchemaException>(() => Compile(
			$$"""{"type":"string","pattern":"x",{{choices}}}""")).Path);
	}

	[Theory]
	[InlineData("""{"type":[]}""", "$.type")]
	[InlineData("""{"type":["string","string"]}""", "$.type")]
	[InlineData("""{"type":1}""", "$.type")]
	[InlineData("""{"enum":[]}""", "$.enum")]
	[InlineData("""{"anyOf":[]}""", "$.anyOf")]
	[InlineData("""{"anyOf":{}}""", "$.anyOf")]
	[InlineData("""{"type":"integer","minimum":"1"}""", "$.minimum")]
	[InlineData("""{"type":"string","description":null}""", "$.description")]
	[InlineData("""{"type":"object","properties":{"x\".[\\":{"type":1}}}""", "$.properties[\"x\\u0022.[\\\\\"].type")]
	public void Compile_MalformedSchemas_AreDistinctFromUnsupportedConstraints(string source, string path)
	{
		Assert.Equal(path, Assert.Throws<JsonException>(() => Compile(source)).Path);
	}

	[Fact]
	public void Compile_CacheIsBoundedModeSpecificAndDoesNotRetainSourceDocument()
	{
		var compiler = new AppleJsonSchemaCompiler(2);
		const string source = """{"type":"object","properties":{"name":{"type":"string"}}}""";
		var tool = Compile(source, AppleJsonSchemaMode.Tool, compiler);
		var response = Compile(source, AppleJsonSchemaMode.Response, compiler);
		Assert.NotEqual(tool, response);
		Assert.Equal(2, compiler.CachedSchemaCount);
		Assert.Equal(tool, Compile(source, AppleJsonSchemaMode.Tool, compiler));
		Compile("""{"type":"integer"}""", compiler: compiler);
		Assert.Equal(2, compiler.CachedSchemaCount);
		Assert.Equal(tool, Compile(source, AppleJsonSchemaMode.Tool, compiler));
		var document = CompileCollectibleDocument(compiler);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.False(document.IsAlive);
		Assert.Equal("""{"type":"string"}""", Compile("""{"type":"string","description":"lifetime"}""", compiler: compiler));
	}

	[Theory]
	[InlineData("""{"type":"number","minimum":1e-200}""", "0", "$.minimum")]
	[InlineData("""{"type":"number","maximum":-1e-200}""", "-0", "$.maximum")]
	[InlineData("""{"type":"number","minimum":5e-324}""", "0", "$.minimum")]
	[InlineData("""{"type":"number","minimum":0.1}""", "0.0999999999999999999999999999999999999999", "$.minimum")]
	[InlineData("""{"type":"integer","minimum":9007199254740991}""", "9007199254740990", "$.minimum")]
	[InlineData("""{"type":"integer","maximum":1}""", "1.1", "$")]
	[InlineData("""{"type":"integer"}""", "9223372036854775808", "$")]
	[InlineData("""{"type":"string","enum":["a","b"]}""", "\"c\"", "$.enum")]
	[InlineData("""{"const":"\u00e9"}""", "\"e\\u0301\"", "$.const")]
	[InlineData("""{"type":"boolean"}""", "\"true\"", "$")]
	[InlineData("""{"type":"null"}""", "false", "$")]
	[InlineData("""{"type":"array","items":{"type":"integer"},"minItems":2}""", "[1]", "$.minItems")]
	[InlineData("""{"type":"array","items":{"type":"integer"},"maxItems":1}""", "[1,2]", "$.maxItems")]
	[InlineData("""{"type":"array","items":{"type":"integer","minimum":1}}""", "[1,0]", "$.items.minimum")]
	[InlineData("""{"type":"object","properties":{"x":{"type":"integer"}},"required":["x"]}""", "{}", "$.required")]
	[InlineData("""{"type":"object","properties":{}}""", "{\"extra\":1}", "$.additionalProperties")]
	[InlineData("""{"type":"object","properties":{"x":{"type":"integer"}}}""", "{\"x\":1,\"x\":2}", "$")]
	[InlineData("""{"type":["string","null"]}""", "1", "$.anyOf")]
	public void ValidateOutput_NativeConstraintViolations_FailClosedWithProfilePath(string source, string value, string path)
	{
		var error = Assert.Throws<InvalidDataException>(() => AppleJsonSchemaCompiler.ValidateOutput(Compile(source), value));
		Assert.Contains($"schema at {path}", error.Message, StringComparison.Ordinal);
		Assert.Contains("Generated response", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("""{"type":"number","minimum":1e-200}""", "1e-200")]
	[InlineData("""{"type":"number","maximum":1e200}""", "9.999999999999999e199")]
	[InlineData("""{"type":"number","minimum":0.1,"maximum":0.2}""", "0.1000000000000000000000000000000000000000")]
	[InlineData("""{"type":"integer","minimum":-9007199254740991,"maximum":9007199254740991}""", "9007199254740991")]
	[InlineData("""{"type":"integer"}""", "1.0000000000000000000000000000000000000000")]
	[InlineData("""{"enum":["\u00e9","e\u0301"]}""", "\"e\\u0301\"")]
	[InlineData("""{"type":["string","null"]}""", "null")]
	[InlineData("""{"type":["string","integer"]}""", "2")]
	[InlineData("""{"type":"array","items":{"type":"integer"},"minItems":0,"maxItems":0}""", "[]")]
	[InlineData("""{"type":"object","properties":{"x":{"type":["string","null"]}}}""", "{}")]
	[InlineData("""{"type":"object","properties":{"x":{"type":["string","null"]}}}""", "{\"x\":null}")]
	public void ValidateOutput_CanonicalValues_AreAccepted(string source, string value)
	{
		AppleJsonSchemaCompiler.ValidateOutput(Compile(source), value);
	}

	[Fact]
	public void ValidateOutput_InvalidJsonOrToolArguments_IncludeContextWithoutRepair()
	{
		var profile = Compile("""{"type":"object","properties":{"a.b":{"type":"array","items":{"type":"number","minimum":1e-200}}}}""");
		var error = Assert.Throws<InvalidDataException>(() => AppleJsonSchemaCompiler.ValidateOutput(profile,
			"""{"a.b":[0]}""", "Tool 'capture' arguments"));
		Assert.Contains("Tool 'capture' arguments", error.Message, StringComparison.Ordinal);
		Assert.Contains("""value at $["a.b"][0]""", error.Message, StringComparison.Ordinal);
		Assert.Contains("""schema at $.properties["a.b"].items.minimum""", error.Message, StringComparison.Ordinal);
		Assert.Throws<InvalidDataException>(() => AppleJsonSchemaCompiler.ValidateOutput(profile, """{"a.b":"""));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ValidateOutput_NativeViolation_FaultsCompletionEvenAfterStreamingPartialProgress(bool streaming)
	{
		var schema = Compile("""{"type":"number","minimum":1e-200}""");
		if (!streaming)
		{
			var handler = new NonStreamingResponseHandler();
			try
			{
				AppleJsonSchemaCompiler.ValidateOutput(schema, "0");
				handler.Complete(new ChatResponse(new ChatMessage(ChatRole.Assistant, "0")));
			}
			catch (InvalidDataException error) { handler.CompleteWithError(error); }
			await Assert.ThrowsAsync<InvalidDataException>(() => handler.Task);
		}
		else
		{
			var handler = new StreamingResponseHandler("apple-intelligence");
			handler.ProcessContent("0");
			try
			{
				AppleJsonSchemaCompiler.ValidateOutput(schema, "0");
				handler.Complete();
			}
			catch (InvalidDataException error) { handler.CompleteWithError(error); }
			var deltas = new List<ChatResponseUpdate>();
			await Assert.ThrowsAsync<InvalidDataException>(async () =>
			{
				await foreach (var update in handler.ReadAllAsync(default)) deltas.Add(update);
			});
			Assert.Equal("0", Assert.Single(deltas).Text);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CompileCollectibleDocument(AppleJsonSchemaCompiler compiler)
	{
		var document = JsonDocument.Parse("""{"type":"string","description":"lifetime"}""");
		compiler.Compile(document.RootElement, AppleJsonSchemaMode.Tool);
		return new(document);
	}

	public sealed class FormattedResponse
	{
		[Description("Stable identifier")]
		public Guid Id { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public NestedResponse? Nested { get; set; }
	}

	public sealed class NestedResponse
	{
		public int? Count { get; set; }
		public string? Label { get; set; }
	}
}
