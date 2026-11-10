// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.AI.Chat;

internal static partial class UIAgentLog
{
    [LoggerMessage(100, LogLevel.Information, "SendMessageAsync: Streaming assistant response")]
    internal static partial void StreamingAssistantResponse(ILogger logger);

    [LoggerMessage(101, LogLevel.Debug, "SendMessageAsync: Received update #{Index} Role={Role}, ContentTypes=[{ContentTypes}]")]
    internal static partial void ReceivedUpdate(ILogger logger, int index, string? role, string contentTypes);

    [LoggerMessage(102, LogLevel.Information, "SendMessageAsync: Stream complete. Total updates={UpdateCount}")]
    internal static partial void StreamComplete(ILogger logger, int updateCount);

    [LoggerMessage(103, LogLevel.Debug, "SendMessageAsync: Added {MessageCount} messages to history")]
    internal static partial void AddedToHistory(ILogger logger, int messageCount);

    [LoggerMessage(104, LogLevel.Error, "SendMessageAsync: An error occurred while processing the response")]
    internal static partial void ProcessingError(ILogger logger, Exception exception);

    [LoggerMessage(105, LogLevel.Error, "UI action '{FunctionName}' (CallId={CallId}) failed; returning an error result to the model")]
    internal static partial void UIActionFailed(ILogger logger, string functionName, string callId, Exception exception);

    [LoggerMessage(106, LogLevel.Warning, "Tool call '{FunctionName}' (CallId={CallId}) was not executed; returning an error result to the model")]
    internal static partial void UnansweredToolCall(ILogger logger, string functionName, string callId);
}
