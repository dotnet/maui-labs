#if IOS || MACCATALYST
using System.Diagnostics;
using Foundation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientToolCallLoggingTests
{
	// Apple's callback adapter owns log correlation; Activity identity across awaits is covered by the shared function suite.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task ToolCallbackLogs_InheritChatTraceAndSpan(bool streaming)
	{
		const string sourceName = "AppleToolCorrelationTests";
		Activity? chatSpan = null;
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == sourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
			ActivityStopped = activity => chatSpan = activity,
		};
		ActivitySource.AddActivityListener(listener);
		var logCollector = new DeviceTestLogCollector(LogLevel.Debug);
		using var client = new AppleIntelligenceChatClient(logCollector).AsBuilder()
			.UseOpenTelemetry(sourceName: sourceName, configure: telemetry => telemetry.EnableSensitiveData = false)
			.Build();
		var weatherTool = AIFunctionFactory.Create(async (string location) =>
		{
			await Task.Yield();
			return $"Clear skies in {location}";
		}, name: "GetWeather", description: "Gets the weather for a location");
		var options = new ChatOptions { Tools = [weatherTool] };
		ChatMessage[] messages = [new(ChatRole.User, "Use GetWeather to get the weather in Seattle.")];
		if (streaming)
		{
			await foreach (var _ in client.GetStreamingResponseAsync(messages, options)) { }
		}
		else
		{
			await client.GetResponseAsync(messages, options);
		}

		Assert.NotNull(chatSpan);
		Assert.NotEmpty(logCollector.Entries);
		Assert.All(logCollector.Entries, entry =>
		{
			Assert.Equal(chatSpan.TraceId, entry.TraceId);
			Assert.Equal(chatSpan.SpanId, entry.SpanId);
		});
	}

	// ====================================================================
	// Single tool, Debug level
	// Expected: exactly 2 entries
	//   [0] Debug: "Invoking GetWeather."
	//   [1] Debug: "GetWeather invocation completed. Duration: {timespan}"
	// ====================================================================
	// Apple's callback logger owns these exact Debug messages and redaction rules; cloud invocation middleware logs separately.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_SingleTool_Debug_ProducesExactly2Entries()
	{
		var logCollector = new DeviceTestLogCollector(LogLevel.Debug);
		var client = new AppleIntelligenceChatClient(logCollector);

		var weatherTool = AIFunctionFactory.Create(
			(string location) => $"Clear skies, 72°F in {location}",
			name: "GetWeather",
			description: "Gets the weather for a location");

		var options = new ChatOptions { Tools = [weatherTool] };
		await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "What's the weather in Seattle?")], options);

		var logs = logCollector.Entries;
		Assert.Equal(2, logs.Count);

		// Entry 0: "Invoking GetWeather."
		Assert.Equal(LogLevel.Debug, logs[0].Level);
		Assert.Equal("Invoking GetWeather.", logs[0].Message);

		// Entry 1: "GetWeather invocation completed. Duration: ..."
		Assert.Equal(LogLevel.Debug, logs[1].Level);
		Assert.StartsWith("GetWeather invocation completed. Duration: ", logs[1].Message, StringComparison.Ordinal);

		// Debug must NOT leak arguments or results
		var allText = string.Join("\n", logs.Select(l => l.Message));
		Assert.DoesNotContain("Seattle", allText, StringComparison.Ordinal);
		Assert.DoesNotContain("72°F", allText, StringComparison.Ordinal);
	}

	// ====================================================================
	// Single tool, Trace level
	// Expected: exactly 2 entries
	//   [0] Trace: "Invoking GetWeather({"location": "Seattle"})."
	//   [1] Trace: "GetWeather invocation completed. Duration: {ts}. Result: \"Clear skies, 72°F in Seattle\""
	// ====================================================================
	// Apple's callback logger emits this exact sensitive-data Trace format; it is not an IChatClient logging contract.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_SingleTool_Trace_ProducesExactly2EntriesWithSensitiveData()
	{
		var logCollector = new DeviceTestLogCollector(LogLevel.Trace);
		var client = new AppleIntelligenceChatClient(logCollector);

		var weatherTool = AIFunctionFactory.Create(
			(string location) => $"Clear skies, 72°F in {location}",
			name: "GetWeather",
			description: "Gets the weather for a location");

		var options = new ChatOptions { Tools = [weatherTool] };
		await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "What's the weather in Seattle?")], options);

		var logs = logCollector.Entries;
		Assert.Equal(2, logs.Count);

		// Entry 0: Trace with arguments in parentheses
		Assert.Equal(LogLevel.Trace, logs[0].Level);
		Assert.StartsWith("Invoking GetWeather(", logs[0].Message, StringComparison.Ordinal);
		Assert.EndsWith(").", logs[0].Message, StringComparison.Ordinal);
		Assert.Contains("Seattle", logs[0].Message, StringComparison.Ordinal);

		// Entry 1: Trace with duration AND result
		Assert.Equal(LogLevel.Trace, logs[1].Level);
		Assert.StartsWith("GetWeather invocation completed. Duration: ", logs[1].Message, StringComparison.Ordinal);
		Assert.Contains(". Result: ", logs[1].Message, StringComparison.Ordinal);
		Assert.Contains("72°F", logs[1].Message, StringComparison.Ordinal);
	}

	// ====================================================================
	// No tools — must produce zero log entries even at Trace
	// ====================================================================
	// Apple's injected ILoggerFactory is used only for native tool callbacks, unlike provider-wide cloud HTTP logging.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_NoTools_ProducesZeroEntries()
	{
		var logCollector = new DeviceTestLogCollector(LogLevel.Trace);
		var client = new AppleIntelligenceChatClient(logCollector);

		await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "What is 2+2?")]);

		Assert.Empty(logCollector.Entries);
	}

	// ====================================================================
	// Information level — tool calls happen but produce zero log entries
	// (our logging is Debug/Trace only)
	// ====================================================================
	// Apple's callback logger filters successful invocations below Information; other providers choose different levels.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_InformationLevel_ProducesZeroEntries()
	{
		var logCollector = new DeviceTestLogCollector(LogLevel.Information);
		var client = new AppleIntelligenceChatClient(logCollector);

		var weatherTool = AIFunctionFactory.Create(
			(string location) => $"Clear skies, 72°F in {location}",
			name: "GetWeather",
			description: "Gets the weather for a location");

		var options = new ChatOptions { Tools = [weatherTool] };
		await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "What's the weather in Seattle?")], options);

		Assert.Empty(logCollector.Entries);
	}

	// ====================================================================
	// Streaming, Debug — same 2 entries as non-streaming
	// ====================================================================
	// Asserts Apple's native streaming callback logger's exact two-entry Debug format, not shared invocation behavior.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_SingleTool_Debug_ProducesExactly2Entries()
	{
		var logCollector = new DeviceTestLogCollector(LogLevel.Debug);
		var client = new AppleIntelligenceChatClient(logCollector);

		var weatherTool = AIFunctionFactory.Create(
			(string location) => $"Clear skies, 72°F in {location}",
			name: "GetWeather",
			description: "Gets the weather for a location");

		var options = new ChatOptions { Tools = [weatherTool] };
		await foreach (var _ in client.GetStreamingResponseAsync(
			[new ChatMessage(ChatRole.User, "What's the weather in Seattle?")], options))
		{ }

		var logs = logCollector.Entries;
		Assert.Equal(2, logs.Count);

		Assert.Equal(LogLevel.Debug, logs[0].Level);
		Assert.Equal("Invoking GetWeather.", logs[0].Message);

		Assert.Equal(LogLevel.Debug, logs[1].Level);
		Assert.StartsWith("GetWeather invocation completed. Duration: ", logs[1].Message, StringComparison.Ordinal);
	}

	// ====================================================================
	// Streaming, Trace — same 2 entries as non-streaming, with sensitive data
	// ====================================================================
	// Asserts Apple's native streaming callback logger's sensitive-data Trace format, which cloud middleware does not promise.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_SingleTool_Trace_ProducesExactly2EntriesWithSensitiveData()
	{
		var logCollector = new DeviceTestLogCollector(LogLevel.Trace);
		var client = new AppleIntelligenceChatClient(logCollector);

		var weatherTool = AIFunctionFactory.Create(
			(string location) => $"Clear skies, 72°F in {location}",
			name: "GetWeather",
			description: "Gets the weather for a location");

		var options = new ChatOptions { Tools = [weatherTool] };
		await foreach (var _ in client.GetStreamingResponseAsync(
			[new ChatMessage(ChatRole.User, "What's the weather in Seattle?")], options))
		{ }

		var logs = logCollector.Entries;
		Assert.Equal(2, logs.Count);

		Assert.Equal(LogLevel.Trace, logs[0].Level);
		Assert.StartsWith("Invoking GetWeather(", logs[0].Message, StringComparison.Ordinal);
		Assert.Contains("Seattle", logs[0].Message, StringComparison.Ordinal);

		Assert.Equal(LogLevel.Trace, logs[1].Level);
		Assert.Contains(". Result: ", logs[1].Message, StringComparison.Ordinal);
		Assert.Contains("72°F", logs[1].Message, StringComparison.Ordinal);
	}

	// ====================================================================
	// Ordering: for a single tool, "Invoking" must come before "completed"
	// ====================================================================
	// Orders Apple's adapter-owned log messages around a native callback; common tool-result ordering is covered in the base.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_InvokingIsLoggedBeforeCompleted()
	{
		var logCollector = new DeviceTestLogCollector(LogLevel.Debug);
		var client = new AppleIntelligenceChatClient(logCollector);

		var weatherTool = AIFunctionFactory.Create(
			(string location) => $"Clear skies, 72°F in {location}",
			name: "GetWeather",
			description: "Gets the weather for a location");

		var options = new ChatOptions { Tools = [weatherTool] };
		await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "What's the weather in Seattle?")], options);

		var logs = logCollector.Entries;
		Assert.Equal(2, logs.Count);
		Assert.StartsWith("Invoking GetWeather", logs[0].Message, StringComparison.Ordinal);
		Assert.StartsWith("GetWeather invocation completed", logs[1].Message, StringComparison.Ordinal);
	}

	// ====================================================================
	// Multiple tools — native framework invokes concurrently.
	// Exactly 4 entries: Invoking + completed for each tool.
	// Order across tools is non-deterministic, but each tool's
	// "Invoking" must precede its "completed".
	// ====================================================================
	// Apple's framework invokes native tools concurrently; asserts its callback logger's entry count and per-tool ordering.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_MultipleTools_ProducesExactly4Entries()
	{
		var logCollector = new DeviceTestLogCollector(LogLevel.Debug);
		var client = new AppleIntelligenceChatClient(logCollector);

		var weatherTool = AIFunctionFactory.Create(
			(string location) => $"Clear skies, 72°F in {location}",
			name: "GetWeather",
			description: "Gets the weather for a location");

		var timeTool = AIFunctionFactory.Create(
			(string timezone) => $"3:00 PM in {timezone}",
			name: "GetTime",
			description: "Gets the current time in a timezone");

		var options = new ChatOptions { Tools = [weatherTool, timeTool] };
		await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "What's the weather in Seattle and the time in EST?")], options);

		var logs = logCollector.Entries;
		var messages = logs.Select(l => l.Message).ToList();

		// Exactly 4 entries: 2 per tool (Invoking + completed)
		Assert.Equal(4, logs.Count);

		// All entries at Debug level
		Assert.All(logs, l => Assert.Equal(LogLevel.Debug, l.Level));

		// Exactly 1 "Invoking GetWeather." and 1 "GetWeather invocation completed..."
		Assert.Single(messages, m => m == "Invoking GetWeather.");
		Assert.Single(messages, m => m.StartsWith("GetWeather invocation completed. Duration: ", StringComparison.Ordinal));

		// Exactly 1 "Invoking GetTime." and 1 "GetTime invocation completed..."
		Assert.Single(messages, m => m == "Invoking GetTime.");
		Assert.Single(messages, m => m.StartsWith("GetTime invocation completed. Duration: ", StringComparison.Ordinal));

		// Each tool's "Invoking" precedes its "completed" (ordering within each tool)
		var weatherInvoking = messages.FindIndex(m => m == "Invoking GetWeather.");
		var weatherCompleted = messages.FindIndex(m => m.StartsWith("GetWeather invocation completed", StringComparison.Ordinal));
		Assert.True(weatherInvoking < weatherCompleted, "GetWeather: Invoking should come before completed");

		var timeInvoking = messages.FindIndex(m => m == "Invoking GetTime.");
		var timeCompleted = messages.FindIndex(m => m.StartsWith("GetTime invocation completed", StringComparison.Ordinal));
		Assert.True(timeInvoking < timeCompleted, "GetTime: Invoking should come before completed");
	}

	// ====================================================================
	// Tool failure — exactly 2 entries: Invoking (Debug) + failed (Error)
	// ====================================================================
	// Apple's native invocation policy faults the chat request rather than returning an error tool result to the model.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public Task GetResponseAsync_ToolFailure_Produces2EntriesWithErrorLevel() =>
		AssertNativeToolFailureAsync(streaming: false, wrapWithFunctionInvocation: false);

	[Theory]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public Task NativeToolFailure_WithOrWithoutFunctionInvocation_FaultsRequest(
		bool streaming, bool wrapWithFunctionInvocation) =>
		AssertNativeToolFailureAsync(streaming, wrapWithFunctionInvocation);

	private static async Task AssertNativeToolFailureAsync(bool streaming, bool wrapWithFunctionInvocation)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
		var logCollector = new DeviceTestLogCollector(LogLevel.Debug);
		var native = new AppleIntelligenceChatClient(logCollector);
		using IChatClient client = wrapWithFunctionInvocation
			? native.AsBuilder().UseFunctionInvocation().Build()
			: native;
		var calls = 0;
		var failingTool = AIFunctionFactory.Create(
			string (string location) =>
			{
				Interlocked.Increment(ref calls);
				throw new InvalidOperationException("API is down");
			},
			name: "GetWeather",
			description: "Gets the weather for a location");

		var options = new ChatOptions { Tools = [failingTool] };
		ChatMessage[] messages = [new(ChatRole.User, "Call GetWeather for Seattle.")];
		var updates = new List<ChatResponseUpdate>();
		var error = await Assert.ThrowsAsync<NSErrorException>(async () =>
		{
			if (streaming)
			{
				await foreach (var update in client.GetStreamingResponseAsync(messages, options, timeout.Token))
					updates.Add(update);
			}
			else
			{
				await client.GetResponseAsync(messages, options, timeout.Token);
			}
		});
		Assert.Equal("ChatClientNative", error.Error.Domain);
		Assert.Equal(1, calls);
		if (streaming)
		{
			var call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
			Assert.True(call.InformationalOnly);
			Assert.DoesNotContain(updates.SelectMany(update => update.Contents), content => content is FunctionResultContent);
			Assert.DoesNotContain(updates, update => update.FinishReason is not null);
		}

		var logs = logCollector.Entries;
		Assert.Equal(2, logs.Count);

		// Entry 0: Debug-level "Invoking GetWeather."
		Assert.Equal(LogLevel.Debug, logs[0].Level);
		Assert.Equal("Invoking GetWeather.", logs[0].Message);

		// Entry 1: Error-level "GetWeather invocation failed."
		Assert.Equal(LogLevel.Error, logs[1].Level);
		Assert.Equal("GetWeather invocation failed.", logs[1].Message);
	}

	/// <summary>
	/// Thread-safe log collector for device tests. Uses lock because native
	/// Apple Intelligence invokes tools concurrently from different threads.
	/// </summary>
	private class DeviceTestLogCollector : ILoggerFactory, ILogger
	{
		private readonly LogLevel _minimumLevel;
		private readonly object _lock = new();

		public DeviceTestLogCollector(LogLevel minimumLevel) => _minimumLevel = minimumLevel;

		public List<DeviceTestLogEntry> Entries { get; } = [];

		public ILogger CreateLogger(string categoryName) => this;
		public void AddProvider(ILoggerProvider provider) { }
		void IDisposable.Dispose() { }

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (IsEnabled(logLevel))
			{
				var activity = Activity.Current;
				var entry = new DeviceTestLogEntry(logLevel, formatter(state, exception), activity?.TraceId, activity?.SpanId);
				lock (_lock)
				{
					Entries.Add(entry);
				}
			}
		}
	}

	private record DeviceTestLogEntry(LogLevel Level, string Message, ActivityTraceId? TraceId, ActivitySpanId? SpanId);
}

#endif
