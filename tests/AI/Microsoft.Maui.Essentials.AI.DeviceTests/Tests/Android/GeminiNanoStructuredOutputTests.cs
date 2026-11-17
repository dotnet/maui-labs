#if ANDROID
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Collection(nameof(GeminiNanoCollection))]
public sealed class GeminiNanoStructuredOutputTests(ITestOutputHelper output)
{
	public record Reply(string Answer);
	public record Continents(string[] Names);
	public record SecretReport(string Secret, string Key);
	public record Weather(string City, int TemperatureC, string Condition);
	public record Person(string Name, int Age, string City);

	[Fact]
	public void ToNative_MapsNormalizedSchemaWithoutMutatingCallerOptions()
	{
		var format = Assert.IsType<ChatResponseFormatJson>(ChatResponseFormat.ForJsonSchema<Reply>());
		var original = format.Schema!.Value.GetRawText();
		var options = new ChatOptions { ResponseFormat = format };
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options);
		using var schema = JsonDocument.Parse(request.Options.ResponseSchemaJson!);
		Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
		Assert.Equal("answer", Assert.Single(schema.RootElement.GetProperty("required").EnumerateArray()).GetString());
		Assert.Same(format, options.ResponseFormat);
		Assert.Equal(original, format.Schema.Value.GetRawText());
	}

	[Theory]
	[InlineData("""{"type":"object","properties":{"value":{"anyOf":[{"type":"string"},{"type":"integer"}]}},"required":["value"]}""")]
	[InlineData("""{"type":"object","properties":{"value":{"type":"string","minLength":2}},"required":["value"]}""")]
	[InlineData("""{"type":"object","additionalProperties":{"type":"integer"}}""")]
	[InlineData("""{"type":"object","properties":{"next":{"$ref":"#"}},"required":["next"]}""")]
	[InlineData("""{"type":["string","null"]}""")]
	public void UnsupportedSchema_RejectsBeforeInference(string json)
	{
		using var document = JsonDocument.Parse(json);
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(document.RootElement) };
		Assert.Throws<NotSupportedException>(() =>
			GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options));
	}

	// MEAI wraps collection results in a required data property; nullable items come from custom schemas.
	[Theory]
	[InlineData("""{"type":"object","properties":{"data":{"type":"array","items":{"type":"array","items":{"type":"string"}}}},"required":["data"],"additionalProperties":false}""")]
	[InlineData("""{"type":"object","properties":{"data":{"type":["array","null"],"items":{"type":["array","null"],"items":{"type":["string","null"]}}}},"required":["data"],"additionalProperties":false}""")]
	[InlineData("""{"type":"object","properties":{"data":{"type":"array","items":{"type":"integer","maximum":9223372036854775807},"maxItems":3}},"required":["data"]}""")]
	[InlineData("""{"type":"object","properties":{"code":{"type":"string","pattern":"^[a-z]+$","format":"custom"}},"required":["code"]}""")]
	[InlineData("""{"type":"array","items":{"type":"string"}}""")]
	[InlineData("""{"type":"string","enum":["red","green"]}""")]
	[InlineData("""{"type":"integer","minimum":1}""")]
	public void CollectionAndConstrainedSchemas_AreAcceptedBeforeInference(string json)
	{
		using var document = JsonDocument.Parse(json);
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(document.RootElement) };
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options);
		Assert.NotNull(request.Options.ResponseSchemaJson);
	}

	[Theory]
	[InlineData("gemini-nano-full")]
	[InlineData("gemini-nano-preview-fast")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ArbitrarySchemas_GenerateNativelyWithoutShapeLimits(string modelId)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
		using var client = new GeminiNanoChatClient();

		var deep = """{"type":"string","description":"The name of the planet we live on."}""";
		for (var level = 0; level < 20; level++)
			deep = $$"""{"type":"object","properties":{"level{{level}}":{{deep}}},"required":["level{{level}}"],"additionalProperties":false}""";
		using var deepSchema = JsonDocument.Parse(deep);
		var deepResponse = await client.GetResponseAsync([new(ChatRole.User, "Which planet do we live on?")],
			new ChatOptions { ModelId = modelId, Temperature = 0, ResponseFormat = ChatResponseFormat.ForJsonSchema(deepSchema.RootElement) },
			timeout.Token);
		output.WriteLine($"DEEP {modelId}: {deepResponse.Text}");
		var value = JsonDocument.Parse(deepResponse.Text).RootElement;
		for (var level = 19; level >= 0; level--)
			value = value.GetProperty($"level{level}");
		Assert.Contains("earth", value.GetString(), StringComparison.OrdinalIgnoreCase);

		using var arraySchema = JsonDocument.Parse("""{"type":"object","properties":{"colors":{"type":"array","items":{"type":["string","null"]},"minItems":3,"maxItems":3}},"required":["colors"],"additionalProperties":false}""");
		var arrayResponse = await client.GetResponseAsync([new(ChatRole.User, "List the three primary colors of light.")],
			new ChatOptions { ModelId = modelId, Temperature = 0, ResponseFormat = ChatResponseFormat.ForJsonSchema(arraySchema.RootElement) },
			timeout.Token);
		output.WriteLine($"ARRAY {modelId}: {arrayResponse.Text}");
		Assert.Equal(3, JsonDocument.Parse(arrayResponse.Text).RootElement.GetProperty("colors").GetArrayLength());
	}

	[Theory]
	[InlineData("gemini-nano-full")]
	[InlineData("gemini-nano-preview-fast")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task StructuredStreaming_EmitsIncrementalJsonThatMatchesTheFinalResponse(string modelId)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		using var client = new GeminiNanoChatClient();
		var options = new ChatOptions
		{
			ModelId = modelId,
			Temperature = 0,
			ResponseFormat = ChatResponseFormat.ForJsonSchema<Continents>(),
		};
		List<ChatResponseUpdate> updates = [];
		await foreach (var update in client.GetStreamingResponseAsync(
			[new(ChatRole.User, "List the names of the seven continents.")], options, timeout.Token))
			updates.Add(update);
		var text = string.Concat(updates.Select(update => update.Text));
		output.WriteLine($"STREAM {modelId} {updates.Count(update => update.Text.Length > 0)} updates: {text}");
		Assert.True(updates.Count(update => update.Text.Length > 0) > 1);
		Assert.InRange(JsonSerializer.Deserialize<Continents>(text, JsonSerializerOptions.Web)!.Names.Length, 5, 8);
	}

	// Mirrors Microsoft.Extensions.DataIngestion batching: system prompt plus one user message with one text per chunk.
	[Theory]
	[InlineData("gemini-nano-full")]
	[InlineData("gemini-nano-preview-fast")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task DataIngestionBatchSchemas_ReturnTypedSummaryAndKeywordCollections(string modelId)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
		using var client = new GeminiNanoChatClient();
		var options = new ChatOptions { ModelId = modelId, Temperature = 0 };
		ChatMessage chunks = new(ChatRole.User,
		[
			new TextContent("Build cross-platform mobile apps with .NET MAUI, then deploy them to Android and iOS."),
			new TextContent("Use Azure to host secure cloud APIs and store application data."),
		]);

		var summaries = await client.GetResponseAsync<string[]>(
		[
			new(ChatRole.System, "For each of the following texts, write a summary text with no more than 24 words."),
			chunks,
		], options, cancellationToken: timeout.Token);
		output.WriteLine($"SUMMARIES {modelId} {summaries.ModelId}: {summaries.Text}");
		Assert.NotEmpty(summaries.Result);
		Assert.All(summaries.Result, summary => Assert.False(string.IsNullOrWhiteSpace(summary)));

		var keywords = await client.GetResponseAsync<string[][]>(
		[
			new(ChatRole.System, "You are a keyword extraction expert. For each of the following texts, extract up to 3 most relevant keywords. " +
				"Focus on extracting keywords from the following predefined list: AI, Cloud, Data, Mobile, Web, Security. " +
				"Exclude keywords with confidence score below 0.7."),
			chunks,
		], options, cancellationToken: timeout.Token);
		output.WriteLine($"KEYWORDS {modelId} {keywords.ModelId}: {keywords.Text}");
		Assert.NotEmpty(keywords.Result);
		Assert.All(keywords.Result, Assert.NotNull);
		Assert.Contains(keywords.Result.SelectMany(values => values),
			keyword => keyword.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
				keyword.Contains("Cloud", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void StructuredOutput_CombinesWithToolsAndThinking()
	{
		var tool = AIFunctionFactory.Create(() => "value", "lookup");
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<Reply>(),
			Tools = [tool],
			Reasoning = new() { Output = ReasoningOutput.Full },
		};
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options);
		Assert.NotNull(request.Options.ToolsJson);
		Assert.NotNull(request.Options.ResponseSchemaJson);
		Assert.True(request.Options.EnableThinking);
	}

	// AICore emits no function calls under a schema; the bridge lets the model call tools first.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ToolsAndSchema_CallToolsThenReturnTypedResult(bool streaming)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
		using var client = new GeminiNanoChatClient();
		using var invoking = client.AsBuilder().UseFunctionInvocation().Build();
		var secretCalls = 0;
		var weatherCalls = 0;
		var options = new ChatOptions
		{
			ModelId = "gemini-nano-preview-fast",
			Temperature = 0,
			Tools =
			[
				AIFunctionFactory.Create((string key) =>
				{
					Interlocked.Increment(ref secretCalls);
					return "ORCHARD_SECRET_7291";
				}, "lookup_secret", "Reads a secret from the local database by key."),
				AIFunctionFactory.Create((string city) =>
				{
					Interlocked.Increment(ref weatherCalls);
					return city.Contains("paris", StringComparison.OrdinalIgnoreCase)
						? """{"temperatureC":18,"condition":"cloudy"}"""
						: """{"temperatureC":10,"condition":"rainy"}""";
				}, "get_weather", "Gets the current weather for a city."),
			],
		};

		async Task<T> Run<T>(string prompt)
		{
			ChatMessage[] messages = [new(ChatRole.User, prompt)];
			if (!streaming)
			{
				var response = await invoking.GetResponseAsync<T>(messages, options, cancellationToken: timeout.Token);
				output.WriteLine($"{typeof(T).Name}: {response.Text}");
				return response.Result;
			}
			var streamOptions = options.Clone();
			streamOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<T>(JsonSerializerOptions.Web);
			var text = "";
			await foreach (var update in invoking.GetStreamingResponseAsync(messages, streamOptions, timeout.Token))
				text += update.Text;
			output.WriteLine($"{typeof(T).Name} stream: {text}");
			return JsonSerializer.Deserialize<T>(text, JsonSerializerOptions.Web)!;
		}

		var secret = await Run<SecretReport>("Use lookup_secret with key orchard and report the secret.");
		Assert.Equal("ORCHARD_SECRET_7291", secret.Secret);
		Assert.Equal(1, secretCalls);

		var weather = await Run<Weather>("What is the weather in Paris right now?");
		Assert.Equal(18, weather.TemperatureC);
		Assert.Equal("cloudy", weather.Condition, StringComparer.OrdinalIgnoreCase);
		Assert.Equal(1, weatherCalls);

		var person = await Run<Person>("Extract the person: Alice is 30 years old and lives in Oslo.");
		Assert.Equal(new Person("Alice", 30, "Oslo"), person);
		Assert.Equal(1, secretCalls);
		Assert.Equal(1, weatherCalls);
	}

	// A tool turn that hits the token limit without a call may have been about to call a tool, so it must not
	// lead to a schema answer. (A call cut off midway already fails inside AICore.)
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ToolsAndSchema_TruncatedToolTurnFailsInsteadOfAnswering(bool streaming)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		using var client = new GeminiNanoChatClient();
		var calls = 0;
		var options = new ChatOptions
		{
			ModelId = "gemini-nano-preview-fast",
			Temperature = 0,
			MaxOutputTokens = 32,
			ResponseFormat = ChatResponseFormat.ForJsonSchema<Reply>(JsonSerializerOptions.Web),
			Tools = [AIFunctionFactory.Create((string key) =>
			{
				Interlocked.Increment(ref calls);
				return "ORCHARD_SECRET_7291";
			}, "lookup_secret", "Reads a secret from the local database by key.")],
		};
		ChatMessage[] messages = [new(ChatRole.User, "Write a long poem about the sea.")];
		var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages, options, timeout.Token).ToChatResponseAsync(timeout.Token);
			else
				await client.GetResponseAsync(messages, options, timeout.Token);
		});
		output.WriteLine(error.Message);
		Assert.Contains("output token limit while deciding whether to call tools", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, calls);
	}

	// Function invocation replays the first turn's reasoning as history on the second round.
	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ThinkingAndTools_ReplayReasoningAcrossRounds(bool streaming, bool schema)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
		using var client = new GeminiNanoChatClient();
		using var invoking = client.AsBuilder().UseFunctionInvocation().Build();
		var calls = 0;
		var options = new ChatOptions
		{
			ModelId = "gemini-nano-preview-fast",
			Temperature = 0,
			MaxOutputTokens = 2048,
			Reasoning = new() { Output = ReasoningOutput.Full },
			Tools = [AIFunctionFactory.Create((string key) =>
			{
				Interlocked.Increment(ref calls);
				return "ORCHARD_SECRET_7291";
			}, "lookup_secret", "Reads a secret from the local database by key.")],
		};
		if (schema)
			options.ResponseFormat = ChatResponseFormat.ForJsonSchema<SecretReport>(JsonSerializerOptions.Web);
		ChatMessage[] messages = [new(ChatRole.User, "Use lookup_secret with key orchard and report the secret.")];
		var response = streaming
			? await invoking.GetStreamingResponseAsync(messages, options, timeout.Token).ToChatResponseAsync(timeout.Token)
			: await invoking.GetResponseAsync(messages, options, timeout.Token);
		var thoughts = response.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Count();
		output.WriteLine($"REPLAY streaming={streaming} schema={schema} calls={calls} thoughts={thoughts}: {response.Text}");
		Assert.Equal(1, calls);
		Assert.True(thoughts > 0);
		if (schema)
			Assert.Equal("ORCHARD_SECRET_7291", JsonSerializer.Deserialize<SecretReport>(response.Text, JsonSerializerOptions.Web)!.Secret);
		else
			Assert.Contains("ORCHARD_SECRET_7291", response.Text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("gemini-nano-preview-fast", false)]
	[InlineData("gemini-nano-preview-fast", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ThinkingAndSchema_ReturnThoughtsAndTypedResult(string modelId, bool streaming)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
		using var client = new GeminiNanoChatClient();
		var options = new ChatOptions
		{
			ModelId = modelId,
			Temperature = 0,
			Reasoning = new() { Output = ReasoningOutput.Full },
			ResponseFormat = ChatResponseFormat.ForJsonSchema<Person>(JsonSerializerOptions.Web),
		};
		ChatMessage[] messages = [new(ChatRole.User, "Extract the person: Alice is 30 years old and lives in Oslo.")];
		var response = streaming
			? await client.GetStreamingResponseAsync(messages, options, timeout.Token).ToChatResponseAsync(timeout.Token)
			: await client.GetResponseAsync(messages, options, timeout.Token);
		var thoughts = string.Concat(response.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>()
			.Select(content => content.Text));
		output.WriteLine($"THINKING {modelId} {streaming}: {thoughts.Length} thought chars, {response.Text}");
		Assert.NotEmpty(thoughts);
		Assert.Equal(new Person("Alice", 30, "Oslo"), JsonSerializer.Deserialize<Person>(response.Text, JsonSerializerOptions.Web));
	}

	[Theory]
	[InlineData("gemini-nano-full", false)]
	[InlineData("gemini-nano-preview-fast", false)]
	[InlineData("gemini-nano-preview-fast", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task NonObjectRootSchemas_ReturnTheBareValue(string modelId, bool streaming)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
		using var client = new GeminiNanoChatClient();
		async Task<JsonElement> Generate(string schema, string prompt)
		{
			using var document = JsonDocument.Parse(schema);
			var options = new ChatOptions { ModelId = modelId, Temperature = 0, ResponseFormat = ChatResponseFormat.ForJsonSchema(document.RootElement) };
			ChatMessage[] messages = [new(ChatRole.User, prompt)];
			string text;
			if (streaming)
			{
				List<string> updates = [];
				await foreach (var update in client.GetStreamingResponseAsync(messages, options, timeout.Token))
					if (update.Text.Length > 0)
						updates.Add(update.Text);
				text = Assert.Single(updates);
			}
			else
			{
				text = (await client.GetResponseAsync(messages, options, timeout.Token)).Text;
			}
			output.WriteLine($"ROOT {modelId} {schema}: {text}");
			return JsonDocument.Parse(text).RootElement.Clone();
		}

		var colors = await Generate("""{"type":"array","items":{"type":"string","enum":["red","green","blue"]},"minItems":3,"maxItems":3}""",
			"List the three primary colors of light.");
		Assert.Equal(3, colors.GetArrayLength());
		Assert.All(colors.EnumerateArray(), color => Assert.Contains(color.GetString(), new[] { "red", "green", "blue" }));

		var mood = await Generate("""{"type":"string","enum":["positive","negative","neutral"]}""",
			"Classify the sentiment: I absolutely love this phone!");
		Assert.Equal("positive", mood.GetString());

		var count = await Generate("""{"type":"integer","minimum":1,"maximum":20}""", "How many legs does a spider have?");
		// The bounds are enforced; Nano V3 does not always answer this correctly.
		Assert.InRange(count.GetInt32(), 1, 20);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ModelStatus_AdvertisesOnlyReportedStructuredCapability(bool structured)
	{
		var status = GeminiNanoChatClient.CreateModelStatus(3, false, false, structured);
		Assert.Equal(structured, status.Capabilities!.Contains(ChatModelCapability.StructuredOutput));
		Assert.DoesNotContain(ChatModelCapability.ToolCalling, status.Capabilities!);
	}

	[Theory]
	[InlineData("gemini-nano-full", false)]
	[InlineData("gemini-nano-full", true)]
	[InlineData("gemini-nano-preview-fast", false)]
	[InlineData("gemini-nano-preview-fast", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task StructuredResponse_PreservesHistoryAndModelUsage(string modelId, bool streaming)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		using var client = new GeminiNanoChatClient();
		var options = new ChatOptions
		{
			ModelId = modelId,
			Temperature = 0,
			MaxOutputTokens = 128,
			ResponseFormat = ChatResponseFormat.ForJsonSchema<Reply>(),
		};
		ChatMessage[] messages =
		[
			new(ChatRole.System, "Extract the exact requested answer from the conversation."),
			new(ChatRole.User, "The secret word is azure."),
			new(ChatRole.Assistant, "I will remember azure."),
			new(ChatRole.User, "Return the secret word in the answer field."),
		];
		var response = streaming
			? await client.GetStreamingResponseAsync(messages, options, timeout.Token).ToChatResponseAsync()
			: await client.GetResponseAsync(messages, options, timeout.Token);
		Assert.Equal("azure", JsonSerializer.Deserialize<Reply>(response.Text, JsonSerializerOptions.Web)!.Answer,
			ignoreCase: true);
		Assert.False(string.IsNullOrWhiteSpace(response.ModelId));
		Assert.True(response.Usage?.InputTokenCount > 0);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ConcurrentSchemasAndCancellation_DoNotLeakSchemaState()
	{
		using var client = new GeminiNanoChatClient();
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync<Reply>(
			[new(ChatRole.User, "Return hello.")], cancellationToken: cancelled.Token));

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		var first = client.GetResponseAsync<Reply>(
			[new(ChatRole.User, "Return exactly hello in the answer field.")], cancellationToken: timeout.Token);
		var second = client.GetResponseAsync<ChatClientJsonSchemaTestsBase<GeminiNanoChatClient>.WeatherResponse>(
			[new(ChatRole.User, "Return location Seattle, temperature 72, and condition sunny.")], cancellationToken: timeout.Token);
		Assert.Equal("hello", (await first).Result.Answer, ignoreCase: true);
		var weather = (await second).Result;
		Assert.Equal("Seattle", weather.Location, ignoreCase: true);
		Assert.Equal(72, weather.Temperature);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task CancellingStructuredInference_ReleasesSchemaForTheNextRequest()
	{
		using var client = new GeminiNanoChatClient();
		using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync<Reply>(
			[new(ChatRole.User, "Write a long answer about every country in Europe.")],
			cancellationToken: cancelled.Token));
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		var next = await client.GetResponseAsync<Reply>(
			[new(ChatRole.User, "Return exactly hello in the answer field.")],
			cancellationToken: timeout.Token);
		Assert.Equal("hello", next.Result.Answer, ignoreCase: true);
	}
}
#endif
