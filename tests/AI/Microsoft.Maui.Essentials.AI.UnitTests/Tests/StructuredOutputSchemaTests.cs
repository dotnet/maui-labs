using Microsoft.Extensions.AI;
using System.Text.Json;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class StructuredOutputSchemaTests
{
	private sealed record Trip(string Destination, Activity[] Activities);
	private sealed record Activity(string Name, string? Notes);

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
}
