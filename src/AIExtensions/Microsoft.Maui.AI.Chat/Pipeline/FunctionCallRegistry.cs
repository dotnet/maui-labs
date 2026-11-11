// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Tracks the blocks that show the function calls of one turn by call id, across all of the turn's
/// request rounds.
/// </summary>
/// <remarks>
/// A call reappears in a later round when <see cref="FunctionInvokingChatClient"/> replays an
/// approved or rejected call, or when a result is sent back as the next request. The pipeline
/// routes such content to the call's existing block instead of creating a duplicate. A call can be
/// shown by any block whose handler claimed it, such as a worksheet with a row per call.
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

    internal bool TryGet(string? callId, [NotNullWhen(true)] out Registration? registration)
    {
        if (!string.IsNullOrEmpty(callId) && _calls.TryGetValue(callId, out var found))
        {
            registration = found;
            return true;
        }

        registration = null;
        return false;
    }

    /// <summary>Records that a result for the call was handled.</summary>
    internal void ReportAnswered(string? callId)
    {
        if (TryGet(callId, out var registration))
            registration.Answered = true;
    }

    internal sealed class Registration(
        FunctionCallContent call,
        ContentBlock block,
        IActiveEntry? resultEntry)
    {
        internal FunctionCallContent Call { get; } = call;

        internal ContentBlock Block { get; } = block;

        internal IActiveEntry? ResultEntry { get; } = resultEntry;

        /// <summary>Gets or sets whether a result for the call was handled since it was registered.</summary>
        internal bool Answered { get; set; }

        /// <summary>
        /// Gets whether the call has its result: a call block's own result, or for a block of the app's own
        /// type, whose result the registry cannot read, whether a result for the call was handled.
        /// </summary>
        internal bool HasResult => Block switch
        {
            FunctionInvocationContentBlock functionBlock => functionBlock.HasResult,
            InteractiveFunctionBlock interactiveBlock => interactiveBlock.HasResult,
            _ => Answered,
        };
    }
}
