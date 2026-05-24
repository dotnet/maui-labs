// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

/// <summary>
/// Shared infrastructure for tool call logging tests.
/// </summary>
internal static class ToolCallLoggingHelpers
{
	/// <summary>
	/// Builds a pipeline: MockClient → FunctionInvokingChatClient → LoggingChatClient.
	/// </summary>
	public static (IChatClient Pipeline, LogCollector Logs, ChatOptions Options) BuildPipeline(
		LogLevel level, bool informationalOnly)
	{
		var logs = new LogCollector(level);
		var loggerFactory = new SingleLoggerFactory(logs);

		var mockClient = new MockToolCallClient();

		if (informationalOnly)
		{
			// Informational-only scenario: the model (e.g. Apple Intelligence on-device)
			// invoked the tool itself and returns call + result + summary in one response.
			// FICC sees InformationalOnly=true and skips local invocation.
			mockClient.AddFirstRoundContent(
				new FunctionCallContent("call-1", "GetWeather",
					new Dictionary<string, object?> { ["location"] = "Seattle" }) { InformationalOnly = true });
			mockClient.AddFirstRoundContent(new FunctionResultContent("call-1", "Sunny, 72°F"));
			mockClient.AddFirstRoundContent(new TextContent("The weather is sunny."));
		}
		else
		{
			// Invocable scenario: the model asks FICC to call the tool.
			// Round 1 — LLM returns only the function call; it has no result yet.
			mockClient.AddFirstRoundContent(
				new FunctionCallContent("call-1", "GetWeather",
					new Dictionary<string, object?> { ["location"] = "Seattle" }));

			// Round 2 — after FICC invokes the tool and appends the result to history,
			// the LLM gets called again and now produces the final text summary.
			mockClient.AddSecondRoundContent(new TextContent("The weather is sunny."));
		}

		var pipeline = new ChatClientBuilder(mockClient)
			.UseFunctionInvocation(loggerFactory)
			.UseLogging(loggerFactory)
			.Build();

		var options = new ChatOptions();
		if (!informationalOnly)
		{
			options.Tools = [AIFunctionFactory.Create(
				(string location) => $"Sunny, 72°F in {location}",
				name: "GetWeather",
				description: "Gets the weather")];
		}

		return (pipeline, logs, options);
	}

	public static string CombineLogs(LogCollector logs) =>
		string.Join("\n", logs.Entries.Select(e => e.Message));
}

/// <summary>
/// Collects log entries with minimum level filtering.
/// </summary>
internal class LogCollector : ILogger
{
	private readonly LogLevel _minimumLevel;

	public LogCollector(LogLevel minimumLevel) => _minimumLevel = minimumLevel;

	public List<LogEntry> Entries { get; } = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
	public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (IsEnabled(logLevel))
			Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
	}
}

internal record LogEntry(LogLevel Level, string Message);

/// <summary>
/// Minimal ILoggerFactory that returns a single shared logger instance.
/// Required by FunctionInvokingChatClient constructor.
/// </summary>
internal class SingleLoggerFactory : ILoggerFactory
{
	private readonly ILogger _logger;
	public SingleLoggerFactory(ILogger logger) => _logger = logger;
	public ILogger CreateLogger(string categoryName) => _logger;
	public void AddProvider(ILoggerProvider provider) { }
	public void Dispose() { }
}

/// <summary>
/// Mock chat client that simulates two-round LLM function-calling behaviour.
/// Round 1: returns <see cref="AddFirstRoundContent"/> items (the initial LLM response).
/// Round 2: when the conversation history contains a Tool message (i.e. FICC has already
///           invoked a function and appended its result), returns <see cref="AddSecondRoundContent"/> items.
/// This mirrors how real models work: the LLM never produces a text summary until it
/// actually receives the function result back from the caller.
/// </summary>
internal class MockToolCallClient : IChatClient
{
	private readonly List<AIContent> _firstRound = [];
	private readonly List<AIContent> _secondRound = [];

	public ChatClientMetadata Metadata => new("MockToolCallClient");

	public void AddFirstRoundContent(AIContent content) => _firstRound.Add(content);
	public void AddSecondRoundContent(AIContent content) => _secondRound.Add(content);

	// Keep these helpers for tests that build their own MockToolCallClient directly
	// (e.g. MultipleFunctionCalls_AllLoggedAtTrace).
	public void AddTextContent(string text) => _firstRound.Add(new TextContent(text));
	public void AddFunctionCallContent(string name, string callId, Dictionary<string, object?>? arguments = null) =>
		_firstRound.Add(new FunctionCallContent(callId, name, arguments));
	public void AddFunctionResultContent(string callId, object? result) =>
		_firstRound.Add(new FunctionResultContent(callId, result));

	public Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
	{
		var content = messages.Any(m => m.Role == ChatRole.Tool) ? _secondRound : _firstRound;
		return Task.FromResult(new ChatResponse(BuildMessages(content)));
	}

	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages, ChatOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var content = messages.Any(m => m.Role == ChatRole.Tool) ? _secondRound : _firstRound;
		foreach (var item in content)
		{
			await Task.Yield();
			yield return new ChatResponseUpdate
			{
				Role = item is FunctionResultContent ? ChatRole.Tool : ChatRole.Assistant,
				Contents = [item]
			};
		}
	}

	private static List<ChatMessage> BuildMessages(List<AIContent> content)
	{
		var messages = new List<ChatMessage>();
		var assistantBuffer = new List<AIContent>();

		foreach (var item in content)
		{
			if (item is FunctionResultContent)
			{
				if (assistantBuffer.Count > 0)
				{
					messages.Add(new ChatMessage(ChatRole.Assistant, [.. assistantBuffer]));
					assistantBuffer.Clear();
				}
				messages.Add(new ChatMessage(ChatRole.Tool, [item]));
			}
			else
			{
				assistantBuffer.Add(item);
			}
		}

		if (assistantBuffer.Count > 0)
			messages.Add(new ChatMessage(ChatRole.Assistant, [.. assistantBuffer]));

		return messages;
	}

	public object? GetService(Type serviceType, object? serviceKey = null) => null;
	public TService? GetService<TService>(object? key = null) where TService : class => null;
	public void Dispose() { }
}
