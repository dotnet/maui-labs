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
	[InlineData("""{"type":"array","items":{"type":"string"}}""")]
	[InlineData("""{"type":"integer","minimum":1}""")]
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
	[InlineData("""{"type":"object","properties":{"ratio":{"type":"number","minimum":0.1,"maximum":0.30000000000000004}},"required":["ratio"]}""")]
	public void CollectionAndConstrainedSchemas_AreAcceptedBeforeInference(string json)
	{
		using var document = JsonDocument.Parse(json);
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(document.RootElement) };
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options);
		Assert.NotNull(request.Options.ResponseSchemaJson);
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

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ModelStatus_AdvertisesOnlyReportedStructuredCapability(bool structured)
	{
		var status = GeminiNanoChatClient.CreateModelStatus(3, false, false, structured);
		Assert.Equal(structured, status.Capabilities!.Contains(ChatModelCapability.StructuredOutput));
		Assert.DoesNotContain(ChatModelCapability.ToolCalling, status.Capabilities!);
	}
}
#endif
