using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public sealed class WindowsAIToolEvaluationClientTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task GetResponseAsync_ProductionAdapter_CountsAndOverridesRealPhases(bool providerDefaults)
	{
		using var inner = new CapturingClient(
			"""{"reason":"The user requests the date.","decision":"tool_needed:get_date"}""",
			"""{"evidence":["date"],"arguments":{}}""",
			"Monday");
		var measured = new WindowsAIToolEvaluationClient(inner, providerDefaults, false);
		using var client = new WindowsAIToolCallingClient(measured)
			.AsBuilder().UseFunctionInvocation().Build();

		var response = await client.GetResponseAsync(
			[new(ChatRole.User, "date")],
			new ChatOptions { Tools = [AIFunctionFactory.Create(() => "Monday", "get_date")] });

		Assert.Equal("Monday", response.Text);
		Assert.Equal(3, measured.NativeRequests);
		Assert.Equal(2, measured.PhaseRequests);
		Assert.Equal(providerDefaults ? 2 : 0, measured.PhaseOverrides);
		Assert.Equal(0, measured.SamplingInvariantFailures);
	}

	[Theory]
	[InlineData("tool_selection")]
	[InlineData("batch_tool_plan")]
	[InlineData("evidence_tool_argument")]
	public async Task GetResponseAsync_ProviderDefaults_OverridesNamedPhaseRegardlessOfPrompt(string name)
	{
		using var inner = new CapturingClient();
		using var client = new WindowsAIToolEvaluationClient(inner, true, false);
		var options = Options(name);

		await client.GetResponseAsync([new(ChatRole.User, "A completely different prompt.")], options);

		Assert.Equal(1, client.NativeRequests);
		Assert.Equal(1, client.PhaseRequests);
		Assert.Equal(1, client.PhaseOverrides);
		Assert.Equal(0, client.SamplingInvariantFailures);
		Assert.Null(inner.Options!.Temperature);
		Assert.Null(inner.Options.TopP);
		Assert.Null(inner.Options.TopK);
		Assert.Equal(0, options.Temperature);
		Assert.Equal(1, options.TopP);
		Assert.Equal(1, options.TopK);
		Assert.Same(options.ResponseFormat, inner.Options.ResponseFormat);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task GetResponseAsync_FinalJson_DoesNotOverrideSampling(bool jsonHistory)
	{
		using var inner = new CapturingClient();
		using var client = new WindowsAIToolEvaluationClient(inner, true, jsonHistory);
		var options = Options("order_status");
		options.Temperature = .5f;

		await client.GetResponseAsync(
			[new(ChatRole.System, "Caller instructions."), new(ChatRole.User, "Answer as JSON.")], options);

		Assert.Equal(0, client.PhaseRequests);
		Assert.Equal(0, client.PhaseOverrides);
		Assert.Equal(.5f, inner.Options!.Temperature);
		Assert.Equal("Caller instructions.", inner.Messages![0].Text);
		Assert.Equal(jsonHistory, inner.Messages[1].Text!.StartsWith("Conversation transcript"));
	}

	[Fact]
	public async Task GetResponseAsync_ZeroTemperatureArm_KeepsPinnedSampling()
	{
		using var inner = new CapturingClient();
		using var client = new WindowsAIToolEvaluationClient(inner, false, false);

		await client.GetResponseAsync([new(ChatRole.User, "Prompt.")], Options("tool_selection"));

		Assert.Equal(1, client.PhaseRequests);
		Assert.Equal(0, client.PhaseOverrides);
		Assert.Equal(0, inner.Options!.Temperature);
	}

	[Fact]
	public async Task GetResponseAsync_UnpinnedPhase_FailsInstrumentationRatherThanPassingVacuously()
	{
		using var inner = new CapturingClient();
		using var client = new WindowsAIToolEvaluationClient(inner, true, false);
		var options = Options("tool_selection");
		options.Temperature = .9f;

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Prompt.")], options));

		Assert.Equal(1, client.SamplingInvariantFailures);
		Assert.Null(inner.Options);
	}

	private static ChatOptions Options(string name)
	{
		using var schema = JsonDocument.Parse("""{"type":"object","properties":{}}""");
		return new()
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema(schema.RootElement.Clone(), name),
			Temperature = 0,
			TopP = 1,
			TopK = 1
		};
	}

	private sealed class CapturingClient(params string[] replies) : IChatClient
	{
		private readonly Queue<string> remainingReplies = new(replies);
		public ChatOptions? Options { get; private set; }
		public ChatMessage[]? Messages { get; private set; }

		public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
			ChatOptions? options = null, CancellationToken cancellationToken = default)
		{
			Options = options?.Clone();
			Messages = [.. messages];
			return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
				remainingReplies.TryDequeue(out var reply) ? reply : "{}")));
		}

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			var response = await GetResponseAsync(messages, options, cancellationToken);
			yield return new() { Role = ChatRole.Assistant, Contents = [new TextContent(response.Text)] };
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;
		public void Dispose() { }
	}
}
