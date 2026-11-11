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
/// Approval requests and responses are removed too when <c>includeApprovals</c> is set (a stopped
/// turn), because a dangling approval makes the next request fail or re-run the approved call.
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

    internal static HashSet<string> CollectAnsweredCallIds(PendingTurn turn)
    {
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in turn.Messages)
            AddAnsweredCallIds(callIds, message.Contents);
        foreach (var update in turn.ThreadUpdates)
            AddAnsweredCallIds(callIds, update.Contents);
        return callIds;
    }

    /// <summary>Adds the call ids of the results in <paramref name="contents"/> to <paramref name="callIds"/>.</summary>
    internal static void AddAnsweredCallIds(HashSet<string> callIds, IList<AIContent> contents)
    {
        foreach (var content in contents)
        {
            if (content is FunctionResultContent result)
                callIds.Add(result.CallId);
        }
    }

    internal static List<ChatMessage> Remove(
        IReadOnlyList<ChatMessage> messages,
        HashSet<string> answeredCallIds,
        bool includeApprovals)
    {
        var kept = new List<ChatMessage>(messages.Count);
        foreach (var message in messages)
        {
            if (AllKept(message.Contents, answeredCallIds, includeApprovals))
            {
                kept.Add(message);
                continue;
            }

            var contents = Filter(message.Contents, answeredCallIds, includeApprovals);
            if (contents.Count == 0)
                continue;

            var clone = message.Clone();
            clone.Contents = contents;
            kept.Add(clone);
        }

        return kept;
    }

    private static bool AllKept(
        IList<AIContent> contents,
        HashSet<string> answeredCallIds,
        bool includeApprovals)
    {
        foreach (var content in contents)
        {
            if (!IsKept(content, answeredCallIds, includeApprovals))
                return false;
        }

        return true;
    }

    private static List<AIContent> Filter(
        IList<AIContent> contents,
        HashSet<string> answeredCallIds,
        bool includeApprovals)
        => contents
            .Where(content => IsKept(content, answeredCallIds, includeApprovals))
            .ToList();

    private static bool IsKept(
        AIContent content,
        HashSet<string> answeredCallIds,
        bool includeApprovals)
        => content switch
        {
            FunctionCallContent call => answeredCallIds.Contains(call.CallId),
            ToolApprovalRequestContent { ToolCall: FunctionCallContent call } when includeApprovals
                => answeredCallIds.Contains(call.CallId),
            ToolApprovalResponseContent { ToolCall: FunctionCallContent call } when includeApprovals
                => answeredCallIds.Contains(call.CallId),
            _ => true,
        };
}
