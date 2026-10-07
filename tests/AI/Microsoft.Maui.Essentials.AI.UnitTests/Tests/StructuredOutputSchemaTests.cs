using Microsoft.Extensions.AI;
using System.Text.Json;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class StructuredOutputSchemaTests
{
	private sealed record Trip(string Destination, Activity[] Activities);
	private sealed record Activity(string Name, string? Notes);
	private sealed record NullableObject(Activity? Activity);

	[Fact]
	public void GetRequiredSchema_NestedNullableDto_TransformsSuccessfully()
	{
		var format = ChatResponseFormat.ForJsonSchema<Trip>();

		var transformed = StructuredOutputSchema.GetRequiredSchema(format);

		Assert.Equal("object", transformed.GetProperty("type").GetString());
		Assert.True(transformed.TryGetProperty("title", out _));
		var activity = transformed
			.GetProperty("properties")
			.GetProperty("activities")
			.GetProperty("items");
		Assert.True(activity.TryGetProperty("title", out _));
	}

	[Fact]
	public void ValidateResponse_ValidNestedDto_Succeeds()
	{
		var schema = StructuredOutputSchema.GetRequiredSchema(
			ChatResponseFormat.ForJsonSchema<Trip>());
		using var response = JsonDocument.Parse(
			"""
			{
			  "destination": "Seattle",
			  "activities": [
			    { "name": "Market", "notes": null }
			  ]
			}
			""");

		StructuredOutputSchema.ValidateResponse(response.RootElement, schema);
	}

	[Fact]
	public void ValidateResponse_MissingRequiredProperty_Throws()
	{
		var schema = StructuredOutputSchema.GetRequiredSchema(
			ChatResponseFormat.ForJsonSchema<Trip>());
		using var response = JsonDocument.Parse(
			"""
			{
			  "destination": "Seattle"
			}
			""");

		var exception = Assert.Throws<InvalidOperationException>(
			() => StructuredOutputSchema.ValidateResponse(response.RootElement, schema));

		Assert.Contains("activities", exception.Message);
	}

	[Fact]
	public void ValidateResponse_AdditionalProperty_Throws()
	{
		var schema = StructuredOutputSchema.GetRequiredSchema(
			ChatResponseFormat.ForJsonSchema<Trip>());
		using var response = JsonDocument.Parse(
			"""
			{
			  "destination": "Seattle",
			  "activities": [],
			  "extra": true
			}
			""");

		var exception = Assert.Throws<InvalidOperationException>(
			() => StructuredOutputSchema.ValidateResponse(response.RootElement, schema));

		Assert.Contains("extra", exception.Message);
	}

	[Fact]
	public void GetRequiredSchema_NullableObject_HasTitleAndAcceptsNull()
	{
		var schema = StructuredOutputSchema.GetRequiredSchema(ChatResponseFormat.ForJsonSchema<NullableObject>());
		var nested = schema.GetProperty("properties").GetProperty("activity");
		Assert.True(nested.TryGetProperty("title", out _));
		using var response = JsonDocument.Parse("""{"activity":null}""");
		StructuredOutputSchema.ValidateResponse(response.RootElement, schema);
	}

	[Theory]
	[InlineData("""{"type":"string","pattern":"x"}""")]
	[InlineData("""{"type":"string","minLength":1}""")]
	[InlineData("""{"type":"string","format":"date-time"}""")]
	[InlineData("""{"type":"string","anyOf":[{"type":"string"}]}""")]
	[InlineData("""{"type":"object","properties":{},"$ref":"#/$defs/item"}""")]
	[InlineData("""{"type":"object","properties":{},"$defs":{}}""")]
	[InlineData("""{"type":"object","properties":{},"additionalProperties":true}""")]
	[InlineData("""{"type":"object","properties":{},"additionalProperties":{"type":"string"}}""")]
	[InlineData("""{"type":"object","properties":{"item":{"type":["string","number"]}}}""")]
	[InlineData("""{"type":["object","array"],"properties":{}}""")]
	[InlineData("""{"type":[]}""")]
	[InlineData("""{"type":["string","string"]}""")]
	[InlineData("""{"type":["string",null]}""")]
	[InlineData("""{"properties":{}}""")]
	[InlineData("""{"type":"object"}""")]
	[InlineData("""{"type":"array","items":true}""")]
	[InlineData("""{"type":"array","items":{"type":"string"},"uniqueItems":true}""")]
	[InlineData("""{"type":"integer","multipleOf":2}""")]
	public void GetRequiredSchema_UnsupportedConstruct_ThrowsBeforeInference(string json)
	{
		using var schema = JsonDocument.Parse(json);
		var format = ChatResponseFormat.ForJsonSchema(schema.RootElement);
		Assert.Throws<NotSupportedException>(() => StructuredOutputSchema.GetRequiredSchema(format));
	}

	[Theory]
	[InlineData("""{"type":"number","minimum":"zero"}""")]
	[InlineData("""{"type":"number","maximum":1e999}""")]
	[InlineData("""{"type":"number","exclusiveMinimum":true}""")]
	[InlineData("""{"type":"number","minimum":3,"maximum":2}""")]
	[InlineData("""{"type":"number","minimum":3,"exclusiveMaximum":3}""")]
	[InlineData("""{"type":"array","items":{"type":"string"},"minItems":-1}""")]
	[InlineData("""{"type":"array","items":{"type":"string"},"maxItems":1.5}""")]
	[InlineData("""{"type":"array","items":{"type":"string"},"minItems":3,"maxItems":2}""")]
	[InlineData("""{"type":"object","properties":{},"required":["unknown"]}""")]
	[InlineData("""{"type":"object","properties":{"x":{"type":"string"}},"required":["x","x"]}""")]
	[InlineData("""{"type":"object","properties":{},"required":true}""")]
	[InlineData("""{"type":"integer","enum":[1.5]}""")]
	[InlineData("""{"type":"string","enum":[null]}""")]
	[InlineData("""{"type":"string","enum":[]}""")]
	[InlineData("""{"type":"number","enum":[1e999]}""")]
	[InlineData("""{"type":"string","title":0}""")]
	public void GetRequiredSchema_MalformedConstraint_Throws(string json)
	{
		using var schema = JsonDocument.Parse(json);
		var format = ChatResponseFormat.ForJsonSchema(schema.RootElement);
		Assert.Throws<NotSupportedException>(() => StructuredOutputSchema.GetRequiredSchema(format));
	}

	[Theory]
	[InlineData("""{"type":"integer","enum":[1,2]}""", "3")]
	[InlineData("""{"type":"number","enum":[1.5]}""", "1.6")]
	[InlineData("""{"type":"boolean","enum":[true]}""", "false")]
	[InlineData("""{"type":["string","null"],"enum":["a"]}""", "null")]
	[InlineData("""{"type":"integer"}""", "1.5")]
	[InlineData("""{"type":"integer"}""", "9007199254740992.5")]
	[InlineData("""{"type":"integer","maximum":9007199254740992}""", "9007199254740993")]
	[InlineData("""{"type":"number","minimum":-9007199254740992}""", "-9007199254740993")]
	[InlineData("""{"type":"number"}""", "1e999")]
	[InlineData("""{"type":"number","minimum":1,"maximum":3}""", "0")]
	[InlineData("""{"type":"number","minimum":1,"maximum":3}""", "4")]
	[InlineData("""{"type":"number","exclusiveMinimum":1}""", "1")]
	[InlineData("""{"type":"number","exclusiveMaximum":3}""", "3")]
	[InlineData("""{"type":"array","items":{"type":"string"},"minItems":1}""", "[]")]
	[InlineData("""{"type":"array","items":{"type":"string"},"maxItems":1}""", """["a","b"]""")]
	[InlineData("""{"type":"array","items":{"type":"string"}}""", "[1]")]
	[InlineData("""{"type":"object","properties":{"x":{"type":"integer"}}}""", """{"x":1,"x":2}""")]
	public void ValidateResponse_InvalidValue_Throws(string schemaJson, string responseJson)
	{
		using var schema = JsonDocument.Parse(schemaJson);
		using var response = JsonDocument.Parse(responseJson);
		Assert.Throws<InvalidOperationException>(
			() => StructuredOutputSchema.ValidateResponse(response.RootElement, schema.RootElement));
	}

	[Theory]
	[InlineData("""{"type":"integer","enum":[1,2]}""", "1.0")]
	[InlineData("""{"type":"integer","minimum":9223372036854775806}""", "9223372036854775807")]
	[InlineData("""{"type":"integer"}""", "10e-1")]
	[InlineData("""{"type":"number","enum":[1.5]}""", "15e-1")]
	[InlineData("""{"type":["integer","null"],"enum":[1,null]}""", "null")]
	[InlineData("""{"type":"boolean","enum":[true]}""", "true")]
	[InlineData("""{"type":"number","minimum":1,"maximum":3}""", "3")]
	[InlineData("""{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":2}""", """["a"]""")]
	public void ValidateResponse_ValidConstraintValue_Succeeds(string schemaJson, string responseJson)
	{
		using var schema = JsonDocument.Parse(schemaJson);
		using var response = JsonDocument.Parse(responseJson);
		StructuredOutputSchema.ValidateResponse(response.RootElement, schema.RootElement);
	}

	[Fact]
	public void GetRequiredSchema_SharedStrictTransformRetainsAppleDefaultsAndRequiredProperties()
	{
		using var original = JsonDocument.Parse(
			"""{"type":"object","properties":{"name":{"type":"string","default":"sample"}}}""");
		var schema = StructuredOutputSchema.GetRequiredSchema(ChatResponseFormat.ForJsonSchema(original.RootElement));

		Assert.Equal(JsonValueKind.False, schema.GetProperty("additionalProperties").ValueKind);
		Assert.Equal("name", schema.GetProperty("required")[0].GetString());
		var property = schema.GetProperty("properties").GetProperty("name");
		Assert.False(property.TryGetProperty("default", out _));
		Assert.Contains("sample", property.GetProperty("description").GetString());
	}
}
