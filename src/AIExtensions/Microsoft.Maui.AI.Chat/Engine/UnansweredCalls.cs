// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Finds the function calls of a round that no result answers, and removes calls that never received a
/// result from a turn's history when it is committed or restored, so the next request cannot fail on a
/// dangling call. The thread keeps them, so a restore shows them as the turn ended them.
/// </summary>
/// <remarks>
/// Approval requests and responses whose call has no result are removed too, because a dangling approval
/// makes the next request fail or run the approved call again. A turn that completes never has one, but a
/// stopped turn does, and a thread may lose the mark that a turn was stopped.
/// </remarks>
internal static class UnansweredCalls
{
    /// <summary>
    /// Finds the calls in <paramref name="messages"/> that no later result answers, in the order they were
    /// made. A call id reused after its result starts a new call. Informational calls ran elsewhere and are
    /// skipped.
    /// </summary>
    internal static List<FunctionCallContent> FindOpen(IEnumerable<ChatMessage> messages)
    {
        var open = new List<FunctionCallContent>();
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent { InformationalOnly: false } call:
                        // A repeat of a call that is still open is the same call.
                        open.RemoveAll(other => string.Equals(other.CallId, call.CallId, StringComparison.Ordinal));
                        open.Add(call);
                        break;
                    case FunctionResultContent result:
                        open.RemoveAll(other => string.Equals(other.CallId, result.CallId, StringComparison.Ordinal));
                        break;
                }
            }
        }

        return open;
    }

    /// <summary>
    /// Removes the calls that no later result answers from <paramref name="messages"/>, with their approval
    /// requests and responses, and the messages left empty.
    /// </summary>
    /// <remarks>
    /// The calls are matched in history order, as <see cref="FindOpen"/> matches them: a call stays open until a
    /// result for its id follows it, a repeat of an open call or its approval request is the same call, and a call
    /// id reused after its result starts a new call, as from a provider that numbers its calls per response. An
    /// approval response belongs to its id's open call, or else to the call it answered, which already has its
    /// result when the client answered an approved UI action.
    /// </remarks>
    internal static List<ChatMessage> Remove(IReadOnlyList<ChatMessage> messages)
    {
        var unanswered = FindUnanswered(messages);
        var kept = new List<ChatMessage>(messages.Count);
        foreach (var message in messages)
        {
            if (unanswered.Count == 0 || !message.Contents.Any(unanswered.Contains))
            {
                kept.Add(message);
                continue;
            }

            var contents = message.Contents.Where(content => !unanswered.Contains(content)).ToList();
            if (contents.Count == 0)
                continue;

            var clone = message.Clone();
            clone.Contents = contents;
            kept.Add(clone);
        }

        return kept;
    }

    /// <summary>Finds the calls, approval requests and approval responses of the calls that no result answers.</summary>
    private static HashSet<AIContent> FindUnanswered(IReadOnlyList<ChatMessage> messages)
    {
        var open = new Dictionary<string, List<AIContent>>(StringComparer.Ordinal);
        var last = new Dictionary<string, List<AIContent>>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        Open(call.CallId).Add(call);
                        break;
                    case ToolApprovalRequestContent { ToolCall: FunctionCallContent call } request:
                        Open(call.CallId).Add(request);
                        break;
                    case ToolApprovalResponseContent { ToolCall: FunctionCallContent call } response:
                        (open.GetValueOrDefault(call.CallId) ?? last.GetValueOrDefault(call.CallId) ?? Open(call.CallId))
                            .Add(response);
                        break;
                    case FunctionResultContent result:
                        open.Remove(result.CallId);
                        break;
                }
            }
        }

        var unanswered = new HashSet<AIContent>(ReferenceEqualityComparer.Instance);
        foreach (var contents in open.Values)
            unanswered.UnionWith(contents);
        return unanswered;

        // The open call with the id, or a new one when the id has none.
        List<AIContent> Open(string callId)
        {
            if (!open.TryGetValue(callId, out var contents))
            {
                contents = [];
                open[callId] = contents;
                last[callId] = contents;
            }

            return contents;
        }
    }
}
