using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Base class for ChatClient JSON schema response format tests.
/// Provides common tests for any IChatClient implementation that supports structured JSON output.
/// </summary>
/// <typeparam name="T">The concrete ChatClient type to test.</typeparam>
public abstract class ChatClientJsonSchemaTestsBase<T>
	where T : class, IChatClient, new()
{
	protected virtual bool SupportsStructuredOutput => true;
	// Null preserves legacy bindings, including their existing schema-free JSON overrides.
	protected virtual bool? RequiresExplicitJsonSchema => null;
	protected virtual bool SupportsBaselineSamplingOptions => false;

	private void ConfigureBaselineSamplingOptions(ChatOptions options, float temperature)
	{
		if (!SupportsBaselineSamplingOptions)
			return;

		options.Temperature = temperature;
		options.Reasoning = new() { Effort = ReasoningEffort.None };
	}

	// Record types for testing JSON schema responses
	public record WeatherResponse(string Location, int Temperature, string Condition);
	public record SimpleResponse(string Message);
	public record TripItinerary(
		string Destination,
		int Days,
		[property: MinLength(2), MaxLength(2)] Activity[] Activities);
	public record Activity(string Name, string Description, string Time);

	private const string ItineraryPrompt =
		"Return a 1-day trip to Seattle with exactly 2 activities, in this order: " +
		"Space Needle at 09:00, description View the city; " +
		"Pike Place Market at 12:00, description Explore the market. Use these exact values.";

	[Theory]
	[InlineData("weather", false)]
	[InlineData("weather", true)]
	[InlineData("simple", false)]
	[InlineData("simple", true)]
	[InlineData("itinerary", false)]
	[InlineData("itinerary", true)]
	[InlineData("custom-options", false)]
	[InlineData("custom-options", true)]
	[InlineData("typed-weather", false)]
	[InlineData("typed-simple", false)]
	[InlineData("typed-itinerary", false)]
	[InlineData("typed-options", false)]
	public async Task StructuredOutput_WhenUnsupported_RejectsBeforeInference(string scenario, bool streaming)
	{
		if (SupportsStructuredOutput)
			return;

		using var client = new T();
		ChatMessage[] messages = [new(ChatRole.User, "Return structured data.")];
		var options = new ChatOptions();
		if (scenario is "custom-options" or "typed-options")
			ConfigureBaselineSamplingOptions(options, 0.5f);
		if (!scenario.StartsWith("typed-", StringComparison.Ordinal))
		{
			options.ResponseFormat = scenario switch
			{
				"weather" => ChatResponseFormat.ForJsonSchema<WeatherResponse>(),
				"itinerary" => ChatResponseFormat.ForJsonSchema<TripItinerary>(),
				"custom-options" => ChatResponseFormat.ForJsonSchema<SimpleResponse>(JsonSerializerOptions.Web),
				_ => ChatResponseFormat.ForJsonSchema<SimpleResponse>()
			};
		}

		await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			if (scenario is "typed-weather" or "typed-options")
				await client.GetResponseAsync<WeatherResponse>(messages, options);
			else if (scenario == "typed-simple")
				await client.GetResponseAsync<SimpleResponse>(messages, options);
			else if (scenario == "typed-itinerary")
				await client.GetResponseAsync<TripItinerary>(messages, options);
			else if (streaming)
				await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages, options);
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SchemaFreeJson_WhenUnsupported_RejectsBeforeInference(bool streaming)
	{
		if (RequiresExplicitJsonSchema is not true && SupportsStructuredOutput)
			return;

		using var client = new T();
		ChatMessage[] messages = [new(ChatRole.User, "Return a JSON object.")];
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
		var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages, options);
		});
		if (RequiresExplicitJsonSchema is true)
			Assert.Contains("explicit JSON schema", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TypedPromptFallback_WhenUnsupported_RejectsBeforeInference()
	{
		if (RequiresExplicitJsonSchema is not true && SupportsStructuredOutput)
			return;

		using var client = new T();
		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync<SimpleResponse>(
				[new ChatMessage(ChatRole.User, "Return exactly hello in the message field.")],
				useJsonSchemaResponseFormat: false));
		if (RequiresExplicitJsonSchema is true)
			Assert.Contains("explicit JSON schema", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithJsonSchemaFormat_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return location Seattle, temperature 72, and condition sunny.")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<WeatherResponse>()
		};

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);

		// Verify the response can be parsed as the expected type
		var weatherResponse = JsonSerializer.Deserialize<WeatherResponse>(response.Text, JsonSerializerOptions.Web);
		Assert.NotNull(weatherResponse);
		Assert.NotNull(weatherResponse.Location);
		Assert.Equal("Seattle", weatherResponse.Location, ignoreCase: true);
		Assert.Equal(72, weatherResponse.Temperature);
		Assert.Equal("sunny", weatherResponse.Condition, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithJsonSchemaFormat_ReturnsValidJson()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return exactly hello in the message field.")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<SimpleResponse>()
		};

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);

		// Verify it's valid JSON
		using var doc = JsonDocument.Parse(response.Text);
		Assert.NotNull(doc);
		var simpleResponse = JsonSerializer.Deserialize<SimpleResponse>(response.Text, JsonSerializerOptions.Web);
		Assert.NotNull(simpleResponse);
		Assert.Equal("hello", simpleResponse.Message, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithJsonSchemaFormat_StreamsValidJson()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return location Boston, temperature 68, and condition sunny.")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<WeatherResponse>()
		};
		ConfigureBaselineSamplingOptions(options, 0);

		var textBuilder = new System.Text.StringBuilder();
		await foreach (var update in client.GetStreamingResponseAsync(messages, options))
		{
			foreach (var content in update.Contents)
			{
				if (content is TextContent textContent)
				{
					textBuilder.Append(textContent.Text);
				}
			}
		}

		var completeText = textBuilder.ToString();
		Assert.False(string.IsNullOrEmpty(completeText), "Should receive streaming response");

		// Verify the complete response is valid JSON
		var weatherResponse = JsonSerializer.Deserialize<WeatherResponse>(completeText, JsonSerializerOptions.Web);
		Assert.NotNull(weatherResponse);
		Assert.Equal("Boston", weatherResponse.Location, ignoreCase: true);
		Assert.Equal(68, weatherResponse.Temperature);
		Assert.Equal("sunny", weatherResponse.Condition, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public virtual async Task GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow()
	{
		if (RequiresExplicitJsonSchema is true || !SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object with a 'greeting' field containing 'hello'")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);
		Assert.False(string.IsNullOrWhiteSpace(response.Text), "Response text should not be empty");

		// Verify the response is valid JSON (will throw if invalid)
		using var doc = JsonDocument.Parse(response.Text);
		Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public virtual async Task GetResponseAsync_WithTypedPromptFallback_UsesSchemaFreeJson()
	{
		if (RequiresExplicitJsonSchema is not false || !SupportsStructuredOutput)
			return;

		using var client = new T();

		var response = await client.GetResponseAsync<SimpleResponse>(
			[new ChatMessage(ChatRole.User, "Return exactly hello in the message field.")],
			useJsonSchemaResponseFormat: false);

		Assert.Equal("hello", response.Result.Message, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public virtual async Task GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow()
	{
		if (RequiresExplicitJsonSchema is true || !SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object with a 'greeting' field containing 'hello'")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var textBuilder = new System.Text.StringBuilder();

		await foreach (var update in client.GetStreamingResponseAsync(messages, options))
		{
			if (!string.IsNullOrEmpty(update.Text))
			{
				textBuilder.Append(update.Text);
			}
		}

		var responseText = textBuilder.ToString();
		Assert.False(string.IsNullOrWhiteSpace(responseText), "Streaming response should not be empty");

		// Verify the complete response is valid JSON (will throw if invalid)
		using var doc = JsonDocument.Parse(responseText);
		Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithJsonSchemaFormatAndCustomOptions_Works()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return exactly hello in the message field.")
		};

		var jsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<SimpleResponse>(jsonOptions)
		};
		ConfigureBaselineSamplingOptions(options, 0.5f);

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);

		// Verify the response can be parsed
		var simpleResponse = JsonSerializer.Deserialize<SimpleResponse>(response.Text, jsonOptions);
		Assert.NotNull(simpleResponse);
		Assert.Equal("hello", simpleResponse.Message, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithComplexJsonSchema_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "You are a travel assistant. Always respond with structured itinerary data."),
			new(ChatRole.User, ItineraryPrompt)
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<TripItinerary>()
		};
		ConfigureBaselineSamplingOptions(options, 0);

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Text);

		var itinerary = JsonSerializer.Deserialize<TripItinerary>(response.Text, JsonSerializerOptions.Web);
		AssertItinerary(itinerary);
	}

	[Fact]
	public async Task GetResponseAsync_WithNullMessages_ThrowsArgumentNullException()
	{
		var client = new T();

		await Assert.ThrowsAsync<ArgumentNullException>(
			() => client.GetResponseAsync(null!));
	}

	[Fact]
	public async Task GetStreamingResponseAsync_WithNullMessages_ThrowsArgumentNullException()
	{
		var client = new T();

		await Assert.ThrowsAsync<ArgumentNullException>(async () =>
		{
			await foreach (var update in client.GetStreamingResponseAsync(null!))
			{
				// Should not reach here
			}
		});
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var message = new ChatMessage(ChatRole.User, "Return location Seattle, temperature 72, and condition sunny.");

		var response = await client.GetResponseAsync<WeatherResponse>(message);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		Assert.NotNull(response.Result.Location);
		Assert.Equal("Seattle", response.Result.Location, ignoreCase: true);
		Assert.Equal(72, response.Result.Temperature);
		Assert.Equal("sunny", response.Result.Condition, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithSimpleType_ReturnsDeserializedResult()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var message = new ChatMessage(ChatRole.User, "Return exactly hello in the message field.");

		var response = await client.GetResponseAsync<SimpleResponse>(message);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		Assert.NotNull(response.Result.Message);
		Assert.Equal("hello", response.Result.Message, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithComplexType_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "You are a travel assistant. Always respond with structured itinerary data."),
			new(ChatRole.User, ItineraryPrompt)
		};

		var options = new ChatOptions();
		ConfigureBaselineSamplingOptions(options, 0);
		var response = await client.GetResponseAsync<TripItinerary>(messages, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		AssertItinerary(response.Result);
	}

	private static void AssertItinerary(TripItinerary? itinerary)
	{
		Assert.NotNull(itinerary);
		Assert.Equal("Seattle", itinerary.Destination, ignoreCase: true);
		Assert.Equal(1, itinerary.Days);
		Assert.NotNull(itinerary.Activities);
		Assert.Equal(2, itinerary.Activities.Length);
		Assert.Collection(itinerary.Activities,
			activity =>
			{
				Assert.Equal("Space Needle", activity.Name, ignoreCase: true);
				Assert.Equal("View the city", activity.Description, ignoreCase: true);
				Assert.Equal("09:00", activity.Time);
			},
			activity =>
			{
				Assert.Equal("Pike Place Market", activity.Name, ignoreCase: true);
				Assert.Equal("Explore the market", activity.Description, ignoreCase: true);
				Assert.Equal("12:00", activity.Time);
			});
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithChatOptions_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var message = new ChatMessage(ChatRole.User, "Return location Boston, temperature 68, and condition sunny.");
		var options = new ChatOptions { MaxOutputTokens = 128 };
		ConfigureBaselineSamplingOptions(options, 0.5f);
		var response = await client.GetResponseAsync<WeatherResponse>(message, options);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		Assert.NotNull(response.Result.Location);
		Assert.Equal("Boston", response.Result.Location, ignoreCase: true);
		Assert.Equal(68, response.Result.Temperature);
		Assert.Equal("sunny", response.Result.Condition, ignoreCase: true);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithMessageList_ReturnsStructuredResponse()
	{
		if (!SupportsStructuredOutput)
			return;

		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Return location Chicago, temperature 65, and condition cloudy.")
		};

		var response = await client.GetResponseAsync<WeatherResponse>(messages);

		Assert.NotNull(response);
		Assert.NotNull(response.Result);
		Assert.NotNull(response.Result.Location);
		Assert.Equal("Chicago", response.Result.Location, ignoreCase: true);
		Assert.Equal(65, response.Result.Temperature);
		Assert.Equal("cloudy", response.Result.Condition, ignoreCase: true);
	}

	[Fact]
	public async Task GetResponseAsyncT_WithNullMessage_ThrowsArgumentNullException()
	{
		var client = new T();

		await Assert.ThrowsAsync<NullReferenceException>(
			() => client.GetResponseAsync<SimpleResponse>((ChatMessage)null!));
	}

	[Fact]
	public async Task GetResponseAsyncT_WithNullMessages_ThrowsArgumentNullException()
	{
		var client = new T();

		await Assert.ThrowsAsync<ArgumentNullException>(
			() => client.GetResponseAsync<SimpleResponse>((IList<ChatMessage>)null!));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsyncT_WithCancellationToken_SupportsCancellation()
	{
		var client = new T();
		var message = new ChatMessage(ChatRole.User, "Say hello in a message field.");
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => client.GetResponseAsync<SimpleResponse>(message, cancellationToken: cts.Token));
	}
}
