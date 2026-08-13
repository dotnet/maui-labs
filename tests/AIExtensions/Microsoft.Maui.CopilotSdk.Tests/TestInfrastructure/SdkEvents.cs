using System.Text.Json;
using GitHub.Copilot;

namespace Microsoft.Maui.CopilotSdk.Tests;

/// <summary>
/// Factory helpers that build real GitHub Copilot SDK <see cref="SessionEvent"/> objects, setting all
/// required members so the events are shaped exactly as the runtime would produce them.
/// </summary>
internal static class SdkEvents
{
    public static AssistantMessageDeltaEvent Delta(string text, string messageId = "m1") => new()
    {
        Data = new AssistantMessageDeltaData { DeltaContent = text, MessageId = messageId },
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static AssistantReasoningDeltaEvent ReasoningDelta(string text, string reasoningId = "r1") => new()
    {
        Data = new AssistantReasoningDeltaData { DeltaContent = text, ReasoningId = reasoningId },
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static AssistantMessageEvent FinalMessage(string content, string messageId = "m1", string? model = null) => new()
    {
        Data = new AssistantMessageData { Content = content, MessageId = messageId, Model = model },
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static AssistantReasoningEvent FinalReasoning(string content, string reasoningId = "r1") => new()
    {
        Data = new AssistantReasoningData { Content = content, ReasoningId = reasoningId },
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static AssistantUsageEvent Usage(
        long? input,
        long? output,
        long? reasoning = null,
        long? cacheRead = null,
        string model = "gpt-5") => new()
    {
        Data = new AssistantUsageData
        {
            Model = model,
            InputTokens = input,
            OutputTokens = output,
            ReasoningTokens = reasoning,
            CacheReadTokens = cacheRead,
        },
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static SessionIdleEvent Idle() => new()
    {
        Data = new SessionIdleData(),
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static SessionErrorEvent Error(string message, string? errorCode = null, string errorType = "runtime_error") => new()
    {
        Data = new SessionErrorData { Message = message, ErrorCode = errorCode, ErrorType = errorType },
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static AbortEvent Abort(string reason = "user_abort") => new()
    {
        Data = new AbortData { Reason = new AbortReason(reason) },
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static ExternalToolRequestedEvent ToolRequested(
        string requestId,
        string toolCallId,
        string toolName,
        object? arguments = null,
        string sessionId = "session-1") => new()
    {
        Data = new ExternalToolRequestedData
        {
            RequestId = requestId,
            SessionId = sessionId,
            ToolCallId = toolCallId,
            ToolName = toolName,
            Arguments = arguments is null ? null : JsonSerializer.SerializeToElement(arguments),
        },
        Timestamp = DateTimeOffset.UtcNow,
    };

    public static ExternalToolCompletedEvent ToolCompleted(string requestId) => new()
    {
        Data = new ExternalToolCompletedData { RequestId = requestId },
        Timestamp = DateTimeOffset.UtcNow,
    };
}
