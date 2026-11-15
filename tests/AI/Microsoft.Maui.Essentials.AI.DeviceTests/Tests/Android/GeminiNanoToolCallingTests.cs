#if ANDROID
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

// Native function availability varies by the selected AICore model, not just Android version.
[Collection(nameof(GeminiNanoCollection))]
public sealed class GeminiNanoToolCallingTests(ITestOutputHelper output)
{
	[Theory]
	[InlineData("gemini-nano", false)]
	[InlineData("gemini-nano-preview-fast", false)]
	[InlineData("gemini-nano-preview-fast", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task Generate_NativeToolCall_ExecutesAndUsesRealResult(string modelId, bool streaming)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		using var client = new GeminiNanoChatClient();
		var status = await GeminiNanoChatClient.GetModelStatusAsync(modelId, timeout.Token);
		output.WriteLine($"NATIVE_TOOLS {modelId}: {status.Availability}, {string.Join(", ", status.Capabilities ?? [])}");
		var calls = 0;
		string Lookup(string key)
		{
			Assert.Equal("orchard", key);
			Interlocked.Increment(ref calls);
			return "ORCHARD_SECRET_7291";
		}
		var options = new ChatOptions
		{
			ModelId = modelId,
			Temperature = 0,
			MaxOutputTokens = 256,
			Instructions = "Use lookup_secret to retrieve the secret. After the tool result, answer with that exact result and do not call again.",
			Tools = [AIFunctionFactory.Create(Lookup, "lookup_secret", "Reads a secret from the local database by key.")],
		};
		if (status.Capabilities?.Contains(ChatModelCapability.ToolCalling) != true)
		{
			var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(
				[new(ChatRole.User, "Use lookup_secret with key orchard and tell me the secret.")], options, timeout.Token));
			output.WriteLine("UNSUPPORTED " + error.Message);
			Assert.Equal(0, calls);
			return;
		}
		using var invoking = client.AsBuilder().UseFunctionInvocation().Build();
		ChatMessage[] messages = [new(ChatRole.User, "Use lookup_secret with key orchard and tell me the secret.")];
		var response = streaming
			? await CaptureStream(invoking.GetStreamingResponseAsync(messages, options, timeout.Token)).ToChatResponseAsync(cancellationToken: timeout.Token)
			: await invoking.GetResponseAsync(messages, options, timeout.Token);
		output.WriteLine(JsonSerializer.Serialize(new { RequestedModel = modelId, response.ModelId, response.Text, response.FinishReason, Calls = calls }));
		Assert.Equal(1, calls);
		Assert.Contains("ORCHARD_SECRET_7291", response.Text, StringComparison.Ordinal);
	}

	// [AIToolSource] and strict MEAI tools close every object schema with additionalProperties=false.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task Generate_ClosedObjectToolSchemas_ExecuteNatively(bool streaming)
	{
		const string modelId = "gemini-nano-preview-fast";
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		using var client = new GeminiNanoChatClient();
		var status = await GeminiNanoChatClient.GetModelStatusAsync(modelId, timeout.Token);
		Assert.True(status.Capabilities?.Contains(ChatModelCapability.ToolCalling) == true,
			$"{modelId} must be downloaded and advertise native tool calling: {status.Availability}.");
		var calls = 0;
		AIFunction Strict(Delegate method, string name, string description)
		{
			var function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions
			{
				Name = name,
				Description = description,
				JsonSchemaCreateOptions = new() { TransformOptions = new() { DisallowAdditionalProperties = true } },
			});
			Assert.False(function.JsonSchema.GetProperty("additionalProperties").GetBoolean());
			return function;
		}
		var lookup = Strict((string key) =>
		{
			Assert.Equal("orchard", key);
			Interlocked.Increment(ref calls);
			return "ORCHARD_SECRET_7291";
		}, "lookup_secret", "Reads a secret from the local database by key.");
		var clock = Strict(() => "12:00", "current_time", "Returns the current local time.");
		var options = new ChatOptions
		{
			ModelId = modelId,
			Temperature = 0,
			MaxOutputTokens = 256,
			Instructions = "Use lookup_secret to retrieve the secret. After the tool result, answer with that exact result and do not call again.",
			Tools = [clock, lookup],
		};
		using var invoking = client.AsBuilder().UseFunctionInvocation().Build();
		ChatMessage[] messages = [new(ChatRole.User, "Use lookup_secret with key orchard and tell me the secret.")];
		var response = streaming
			? await CaptureStream(invoking.GetStreamingResponseAsync(messages, options, timeout.Token)).ToChatResponseAsync(cancellationToken: timeout.Token)
			: await invoking.GetResponseAsync(messages, options, timeout.Token);
		output.WriteLine(JsonSerializer.Serialize(new { response.ModelId, response.Text, Calls = calls }));
		Assert.Equal(1, calls);
		Assert.Contains("ORCHARD_SECRET_7291", response.Text, StringComparison.Ordinal);
	}

	private async IAsyncEnumerable<ChatResponseUpdate> CaptureStream(IAsyncEnumerable<ChatResponseUpdate> updates)
	{
		await foreach (var update in updates)
		{
			Assert.DoesNotContain("<ctrl42>", update.Text, StringComparison.Ordinal);
			Assert.DoesNotContain("<ctrl43>", update.Text, StringComparison.Ordinal);
			yield return update;
		}
	}
}
#endif
