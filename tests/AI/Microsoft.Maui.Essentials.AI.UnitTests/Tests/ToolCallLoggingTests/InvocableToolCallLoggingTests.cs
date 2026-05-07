// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Xunit;
using static Microsoft.Maui.Essentials.AI.UnitTests.ToolCallLoggingHelpers;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

/// <summary>
/// Tests the pipeline with normal (invocable) tool calls.
/// FunctionInvokingChatClient detects the FunctionCallContent, invokes the
/// matching tool, and logs the invocation at Debug/Trace.
/// Pipeline: MockClient → FunctionInvokingChatClient → LoggingChatClient
/// </summary>
public class InvocableToolCallLoggingTests
{
	[Fact]
	public async Task Trace_FICCLogsInvocationWithArguments()
	{
		var (pipeline, logs, options) = BuildPipeline(LogLevel.Trace, informationalOnly: false);

		var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "weather?")], options);

		var allLogs = CombineLogs(logs);

		// FICC logs "Invoking GetWeather({arguments})" at Trace
		Assert.Contains("Invoking GetWeather", allLogs, StringComparison.Ordinal);

		// FICC logs "GetWeather invocation completed. Duration: ..." at Debug (captured at Trace min)
		Assert.Contains("invocation completed", allLogs, StringComparison.Ordinal);
		Assert.Contains("Duration", allLogs, StringComparison.Ordinal);

		// FICC invoked the tool, re-called the model, and the Round 2 text is in the final response
		Assert.Contains("The weather is sunny.", response.Text ?? string.Empty, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Debug_FICCLogsInvocationWithDuration()
	{
		var (pipeline, logs, options) = BuildPipeline(LogLevel.Debug, informationalOnly: false);

		var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "weather?")], options);

		var debugMessages = logs.Entries.Where(e => e.Level == LogLevel.Debug).Select(e => e.Message).ToList();

		// FICC logs "GetWeather invocation completed. Duration: ..." at Debug
		Assert.Contains(debugMessages, m => m.Contains("GetWeather", StringComparison.Ordinal)
			&& m.Contains("invocation completed", StringComparison.Ordinal)
			&& m.Contains("Duration", StringComparison.Ordinal));

		// LoggingChatClient lifecycle still present
		Assert.Contains(debugMessages, m => m.Contains("GetResponseAsync invoked", StringComparison.Ordinal));

		// Tool was invoked and Round 2 model response is present
		Assert.Contains("The weather is sunny.", response.Text ?? string.Empty, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Streaming_Trace_FICCLogsInvocation()
	{
		var (pipeline, logs, options) = BuildPipeline(LogLevel.Trace, informationalOnly: false);

		var responseText = new System.Text.StringBuilder();
		await foreach (var update in pipeline.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "weather?")], options))
		{
			if (update.Text is not null)
				responseText.Append(update.Text);
		}

		var allLogs = CombineLogs(logs);
		Assert.Contains("Invoking GetWeather", allLogs, StringComparison.Ordinal);

		// Round 2 model text is present in the streamed response
		Assert.Contains("The weather is sunny.", responseText.ToString(), StringComparison.Ordinal);
	}
}
