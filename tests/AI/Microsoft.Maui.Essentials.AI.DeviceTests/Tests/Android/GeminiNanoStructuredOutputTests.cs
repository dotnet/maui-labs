#if ANDROID
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Collection(nameof(GeminiNanoCollection))]
public sealed class GeminiNanoStructuredOutputTests
{
	public record Reply(string Answer);

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
	[InlineData("""{"type":"string"}""")]
	[InlineData("""{"type":"object","properties":{"value":{"type":"integer","maximum":9223372036854775807}},"required":["value"]}""")]
	[InlineData("""{"type":"object","properties":{"value":{"type":"string","pattern":"abc"}},"required":["value"]}""")]
	public void UnsupportedSchema_RejectsBeforeInference(string json)
	{
		using var document = JsonDocument.Parse(json);
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(document.RootElement) };
		Assert.Throws<NotSupportedException>(() =>
			GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options));
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
