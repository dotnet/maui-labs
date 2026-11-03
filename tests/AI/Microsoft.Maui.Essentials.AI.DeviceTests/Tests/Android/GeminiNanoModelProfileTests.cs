#if ANDROID
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Collection(nameof(GeminiNanoCollection))]
public sealed class GeminiNanoModelProfileTests(ITestOutputHelper output)
{
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_ModelSelectionChangesPerRequestWithoutChangingTheDefault()
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		using var client = new GeminiNanoChatClient();
		foreach (var modelId in new string?[] { "gemini-nano", "gemini-nano-preview-fast", "gemini-nano-full", null })
		{
			var status = await GeminiNanoChatClient.GetModelStatusAsync(modelId, timeout.Token);
			if (status.Availability != ChatModelAvailability.Available)
			{
				output.WriteLine($"PROFILE_SWITCH {modelId ?? "(default)"}: {status.Availability}; {status.UnavailableReason}");
				continue;
			}
			var expectedModel = await GeminiNanoChatClient.GetBaseModelNameAsync(modelId, timeout.Token);
			var response = await client.GetResponseAsync(
				[new ChatMessage(ChatRole.User, "Reply with OK.")],
				new ChatOptions { ModelId = modelId, Temperature = 0, MaxOutputTokens = 16 },
				timeout.Token);
			output.WriteLine("PROFILE_SWITCH " + JsonSerializer.Serialize(new
			{
				RequestedModelId = modelId,
				ActualModelId = response.ModelId,
				response.Text,
			}));
			Assert.Equal(expectedModel, response.ModelId);
		}
		Assert.Equal("gemini-nano", client.GetService<ChatClientMetadata>()?.DefaultModelId);
	}

	[Theory]
	[InlineData("gemini-nano")]
	[InlineData("gemini-nano-full")]
	[InlineData("gemini-nano-fast")]
	[InlineData("gemini-nano-preview-full")]
	[InlineData("gemini-nano-preview-fast")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task CheckAndRunProfile_ReportsActualNativeModel(string modelId)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
		ChatModelStatus status;
		try
		{
			status = await GeminiNanoChatClient.GetModelStatusAsync(modelId, timeout.Token);
		}
		catch (InvalidOperationException exception) when (exception.Data["MLKitErrorCode"] is 606)
		{
			output.WriteLine("PROFILE_UNAVAILABLE " + JsonSerializer.Serialize(new
			{
				RequestedModelId = modelId,
				Error = exception.Message,
			}));
			var nameError = await Assert.ThrowsAsync<InvalidOperationException>(
				() => GeminiNanoChatClient.GetBaseModelNameAsync(modelId, timeout.Token));
			Assert.Equal(606, nameError.Data["MLKitErrorCode"]);
			return;
		}
		string? modelName = null;
		string? modelNameError = null;
		try
		{
			modelName = await GeminiNanoChatClient.GetBaseModelNameAsync(modelId, timeout.Token);
		}
		catch (InvalidOperationException exception)
		{
			modelNameError = exception.Message;
		}
		output.WriteLine("PROFILE_STATUS " + JsonSerializer.Serialize(new
		{
			RequestedModelId = modelId,
			BaseModelName = modelName,
			ModelNameError = modelNameError,
			Availability = status.Availability.ToString(),
			status.UnavailableReason,
			Capabilities = status.Capabilities?.Select(capability => capability.ToString()).ToArray(),
		}));
		if (status.Availability != ChatModelAvailability.Available)
			return;

		using var client = new GeminiNanoChatClient();
		var elapsed = Stopwatch.StartNew();
		var response = await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "Briefly introduce yourself, then calculate 17 times 23.")],
			new ChatOptions { ModelId = modelId, Temperature = 0, MaxOutputTokens = 96, Seed = 42 },
			timeout.Token);
		output.WriteLine("PROFILE_RESPONSE " + JsonSerializer.Serialize(new
		{
			RequestedModelId = modelId,
			ActualModelId = response.ModelId,
			response.Text,
			FinishReason = response.FinishReason?.ToString(),
			ElapsedMilliseconds = elapsed.ElapsedMilliseconds,
		}));
		Assert.False(string.IsNullOrWhiteSpace(response.ModelId));
		Assert.Equal(modelName, response.ModelId);
	}
}
#endif
