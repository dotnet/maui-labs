// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Tracks the blocks that show the function calls of one turn by call id, across all of the turn's
/// request rounds.
/// </summary>
/// <remarks>
/// A call reappears in a later round when <see cref="FunctionInvokingChatClient"/> replays an
/// approved or rejected call, or when a result is sent back as the next request. The pipeline
/// routes such content to the call's existing block instead of creating a duplicate.
/// </remarks>
internal sealed class FunctionCallRegistry
{
    private readonly Dictionary<string, Registration> _calls = new(StringComparer.Ordinal);

    /// <summary>
    /// Records that <paramref name="block"/> shows <paramref name="call"/>, and which handler
    /// entry, if any, claims the call's result.
    /// </summary>
    internal void Register(FunctionCallContent call, ContentBlock block, IActiveEntry? resultEntry)
    {
        if (!string.IsNullOrEmpty(call.CallId))
            _calls[call.CallId] = new Registration(call, block, resultEntry);
    }

    internal bool TryGet(string? callId, out Registration registration)
    {
        if (!string.IsNullOrEmpty(callId) && _calls.TryGetValue(callId, out var found))
        {
            registration = found;
            return true;
        }

        registration = default;
        return false;
    }

    internal readonly record struct Registration(
        FunctionCallContent Call,
        ContentBlock Block,
        IActiveEntry? ResultEntry)
    {
        internal bool HasResult => Block switch
        {
            FunctionInvocationContentBlock functionBlock => functionBlock.HasResult,
            InteractiveFunctionBlock interactiveBlock => interactiveBlock.HasResult,
            _ => false,
        };
    }
}
