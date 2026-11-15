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
	[InlineData("""{"type":"array","items":{"type":"string"}}""")]
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
	public void StructuredOutput_RejectsActiveToolsAndThinkingButAllowsDisabledTools()
	{
		var tool = AIFunctionFactory.Create(() => "value", "lookup");
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema<Reply>(), Tools = [tool] };
		Assert.Throws<NotSupportedException>(() => GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options));
		options.ToolMode = ChatToolMode.None;
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options);
		Assert.Null(request.Options.ToolsJson);
		Assert.NotNull(request.Options.ResponseSchemaJson);
		options.Reasoning = new() { Output = ReasoningOutput.Full };
		Assert.Throws<NotSupportedException>(() => GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options));
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
