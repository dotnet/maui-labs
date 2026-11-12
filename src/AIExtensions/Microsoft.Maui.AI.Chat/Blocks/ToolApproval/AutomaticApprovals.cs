// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Finds the approval requests that need no decision. When one call of a response needs approval,
/// <see cref="FunctionInvokingChatClient"/> turns every call of the response into an approval request, and sets
/// <see cref="ToolApprovalRequestContent.RequiresConfirmation"/> to <see langword="false"/> for the calls whose
/// tools need none. The engine shows such a request as the call it carries and approves it itself, so the call
/// runs with the batch once the calls that need a decision are decided.
/// </summary>
/// <remarks>
/// A registered UI action keeps its request: <see cref="UIActionHandler"/> decides from the action's own
/// registration whether it needs approval, and the client answers the action with its result, not an approval.
/// </remarks>
internal static class AutomaticApprovals
{
    /// <summary>Gets the call of an approval request that needs no decision.</summary>
    /// <remarks>
    /// The call may be <see cref="FunctionCallContent.InformationalOnly"/>: once
    /// <see cref="FunctionInvokingChatClient"/> runs an approved call, it marks the request's call that way, so a
    /// restore of the turn sees the request with a call that already ran.
    /// </remarks>
    internal static bool TryGetCall(
        AIContent content,
        IReadOnlyDictionary<string, UIAgentOptions.UIActionRegistration> uiActions,
        [NotNullWhen(true)] out FunctionCallContent? call)
    {
        // RequiresConfirmation is experimental (MEAI001); it is how FunctionInvokingChatClient marks these requests.
#pragma warning disable MEAI001
        if (content is ToolApprovalRequestContent
            {
                RequiresConfirmation: false,
                ToolCall: FunctionCallContent functionCall,
            }
            && !uiActions.ContainsKey(functionCall.Name))
#pragma warning restore MEAI001
        {
            call = functionCall;
            return true;
        }

        call = null;
        return false;
    }
}
