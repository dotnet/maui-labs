// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

/// <summary>Checks the tool calls of a request the way a provider does.</summary>
internal static class ToolHistory
{
    /// <summary>Asserts that every call is sent once and answered once, after the call.</summary>
    internal static void AssertValid(IReadOnlyList<ChatMessage> messages)
    {
        var calls = new HashSet<string>(StringComparer.Ordinal);
        var answered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        Assert.True(calls.Add(call.CallId), $"Call '{call.CallId}' is sent more than once.");
                        break;
                    case FunctionResultContent result:
                        Assert.True(
                            calls.Contains(result.CallId),
                            $"Result '{result.CallId}' is sent without its call before it.");
                        Assert.True(answered.Add(result.CallId), $"Call '{result.CallId}' is answered more than once.");
                        break;
                    case ToolApprovalRequestContent { ToolCall: FunctionCallContent { InformationalOnly: false } } request:
                        Assert.Fail($"The pending approval request '{request.RequestId}' reached the model.");
                        break;
                    case ToolApprovalResponseContent { ToolCall: FunctionCallContent { InformationalOnly: false } } response:
                        Assert.Fail($"The pending approval response '{response.RequestId}' reached the model.");
                        break;
                }
            }
        }

        Assert.Equal(calls.Order(), answered.Order());
    }

    /// <summary>Gets the result sent for a call.</summary>
    internal static string? FindResult(IReadOnlyList<ChatMessage> messages, string callId)
        => messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(result => result.CallId == callId)
            .Result?.ToString();
}
