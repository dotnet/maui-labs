#if WINDOWS
using Microsoft.Extensions.AI;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class WindowsAIChatClientConformanceTests
{
	// Windows textual history must retain Assistant turns even after every optional item was omitted.
	[Theory]
	[InlineData("user")]
	[InlineData("assistant")]
	public void ConvertToPrompt_BlankAndUnknownOnlyMessages_RetainsEachTurn(string role)
	{
		ChatMessage[] messages =
		[
			new(new ChatRole(role), []),
			new(new ChatRole(role), [new UnrecognizedContent()]),
			new(new ChatRole(role), [new TextContent(null)]),
			new(new ChatRole(role), [new TextContent("")])
		];
		var label = role == "user" ? "User: " : "Assistant: ";
		Assert.Equal(string.Join(Environment.NewLine + Environment.NewLine,
			Enumerable.Repeat(label, messages.Length)), WindowsAIChatClient.ConvertToPrompt(messages));
	}

	// WinRT LanguageModel is text-only; recognized images cannot be omitted along with unknown content.
	[Theory]
	[InlineData("system", false)]
	[InlineData("user", false)]
	[InlineData("assistant", false)]
	[InlineData("tool", false)]
	[InlineData("user", true)]
	public void NormalizeChatMessages_RecognizedImage_Rejects(string role, bool uri)
	{
		AIContent image = uri ? new UriContent("https://example.invalid/image.png", "image/png") :
			new DataContent(new byte[] { 0 }, "IMAGE/PNG");
		AIContent neighbor = role == "tool" ? new FunctionResultContent("call-1", "Keep") : new TextContent("Keep");
		var error = Assert.Throws<NotSupportedException>(() =>
			WindowsAIChatClient.NormalizeChatMessages([new(new ChatRole(role), [neighbor, image])]));
		Assert.Contains("image", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	// Unsupported custom roles are discarded before Windows content validation, not reinterpreted as user prompts.
	[Fact]
	public void NormalizeChatMessages_UnknownRole_DoesNotValidateDiscardedImage()
	{
		var (system, history) = WindowsAIChatClient.NormalizeChatMessages(
			[new(new ChatRole("custom"), [new DataContent(new byte[] { 0 }, "image/png")])]);
		Assert.Empty(system);
		Assert.Empty(history);
	}

	// Windows has no native tool envelopes; the textual replay must distinguish calls to the same named function.
	[Fact]
	public void ConvertToPrompt_InterleavedToolHistory_PreservesIdsNamesValuesAndOrder()
	{
		ChatMessage[] messages =
		[
			new(ChatRole.Assistant, [
				new TextContent("Before"),
				new FunctionCallContent("first", "lookup"),
				new TextContent("Between"),
				new FunctionCallContent("second", "lookup")]),
			new(ChatRole.Tool, [new FunctionResultContent("second", "second-result"),
				new FunctionResultContent("first", "first-result")]),
			new(ChatRole.User, "After")
		];
		var prompt = WindowsAIChatClient.ConvertToPrompt(messages);
		var expected = new[] { "Before", "Call ID: first", "Name: lookup", "{}", "Between",
			"Call ID: second", "Name: lookup", "{}", "Call ID: second", "Name: lookup",
			"second-result", "Call ID: first", "Name: lookup", "first-result", "After" };
		var position = 0;
		foreach (var value in expected)
		{
			var index = prompt.IndexOf(value, position, StringComparison.Ordinal);
			Assert.True(index >= position, $"Missing/out-of-order '{value}' in '{prompt}'.");
			position = index + value.Length;
		}
	}

	public static IEnumerable<object[]> InvalidOptions()
	{
		foreach (var value in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
			yield return [nameof(ChatOptions.Temperature), value];
		foreach (var value in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
			yield return [nameof(ChatOptions.TopP), value];
		foreach (var value in new[] { -1f, 32065f })
			yield return [nameof(ChatOptions.TopK), value];
	}

	// These are Windows LanguageModelOptions bounds, not OpenAI sampling ranges.
	[Theory]
	[MemberData(nameof(InvalidOptions))]
	public async Task InvalidNativeOptions_RejectBeforeModelInitialization(string name, float value)
	{
		var options = new ChatOptions();
		if (name == nameof(ChatOptions.Temperature)) options.Temperature = value;
		else if (name == nameof(ChatOptions.TopP)) options.TopP = value;
		else options.TopK = (int)value;

		using var client = new WindowsAIChatClient();
		var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello")], options));
		Assert.Contains(name, error.Message, StringComparison.Ordinal);
	}

	// Only the three fields exposed by LanguageModelOptions are validated or mapped.
	[Theory]
	[InlineData(0f, 0f, 0)]
	[InlineData(2f, 1f, 32064)]
	[InlineData(100f, 0.5f, 10)]
	public void ConvertOptions_ValidNativeBoundsAndUnsupportedOptions_PreservesValues(float temperature, float topP, int topK)
	{
		var options = new ChatOptions
		{
			Temperature = temperature, TopP = topP, TopK = topK,
			MaxOutputTokens = -1, Seed = -1, FrequencyPenalty = float.NaN,
			PresencePenalty = float.PositiveInfinity, AllowMultipleToolCalls = true
		};
		WindowsAIChatClient.ValidateOptions(options);
		var native = WindowsAIChatClient.ConvertToLanguageModelOptions(options);
		Assert.Equal(temperature, native.Temperature);
		Assert.Equal(topP, native.TopP);
		Assert.Equal((uint)topK, native.TopK);
	}

	// Unset native options retain the actual Windows App SDK defaults.
	[Fact]
	public void ConvertOptions_Unset_PreservesNativeDefaults()
	{
		var defaults = new LanguageModelOptions();
		var mapped = WindowsAIChatClient.ConvertToLanguageModelOptions(new ChatOptions());
		Assert.Equal(defaults.Temperature, mapped.Temperature);
		Assert.Equal(defaults.TopP, mapped.TopP);
		Assert.Equal(defaults.TopK, mapped.TopK);
	}

	// Neither Required nor named Required can be honored by Windows LanguageModel.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ValidateOptions_RequiredToolMode_Rejects(bool named)
	{
		Assert.Throws<NotSupportedException>(() => WindowsAIChatClient.ValidateOptions(new ChatOptions
		{
			ToolMode = named ? ChatToolMode.RequireSpecific("lookup") : ChatToolMode.RequireAny,
			Tools = [AIFunctionFactory.Create(() => "value", "lookup")]
		}));
	}

	private sealed class UnrecognizedContent : AIContent;
}

public class WindowsAIModelStatusTests(ITestOutputHelper output)
{
	// The public helper reads the native state directly; it neither instantiates nor downloads the model.
	[Fact]
	public async Task GetModelStatusAsync_MatchesNativeState()
	{
		var state = LanguageModel.GetReadyState();
		output.WriteLine($"Native LanguageModel.GetReadyState(): {state}");
		var first = await WindowsAIChatClient.GetModelStatusAsync();
		var second = await WindowsAIChatClient.GetModelStatusAsync();
		Assert.NotSame(first, second);
		Assert.Equal(state switch
		{
			AIFeatureReadyState.Ready => ChatModelAvailability.Available,
			AIFeatureReadyState.NotReady => ChatModelAvailability.NotReady,
			AIFeatureReadyState.NotSupportedOnCurrentSystem or
				AIFeatureReadyState.NotCompatibleWithSystemHardware => ChatModelAvailability.NotSupported,
			AIFeatureReadyState.DisabledByUser => ChatModelAvailability.Disabled,
			AIFeatureReadyState.CapabilityMissing or
				AIFeatureReadyState.OSUpdateNeeded => ChatModelAvailability.RequirementsNotMet,
			_ => ChatModelAvailability.Unknown,
		}, second.Availability);
		Assert.Equal(state is AIFeatureReadyState.Ready ? null : state.ToString(), second.UnavailableReason);
		Assert.Equal([ChatModelCapability.Text, ChatModelCapability.Streaming, ChatModelCapability.StructuredOutput],
			second.Capabilities);
	}

	// An already-ready Windows model must not be prepared again.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task EnsureReadyAsync_AlreadyReady_PreservesNativeState()
	{
		var state = LanguageModel.GetReadyState();
		output.WriteLine($"Native state before explicit EnsureReadyAsync: {state}");
		Assert.Equal(AIFeatureReadyState.Ready, state);
		await WindowsAIChatClient.EnsureReadyAsync();
		output.WriteLine($"Native state after explicit EnsureReadyAsync: {LanguageModel.GetReadyState()}");
		Assert.Equal(AIFeatureReadyState.Ready, LanguageModel.GetReadyState());
	}

	[Fact]
	public async Task GetModelStatusAsync_PreCancelled_Throws()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(
			() => WindowsAIChatClient.GetModelStatusAsync(cts.Token));
	}

	// Setup cancellation must prevent native access.
	[Fact]
	public async Task EnsureReadyAsync_PreCancelled_DoesNotReadOrDownload()
	{
		using var source = new CancellationTokenSource();
		source.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WindowsAIChatClient.EnsureReadyAsync(source.Token));
	}
}
#endif
