// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A human-in-the-loop approval request: the model wants to call a tool that requires consent.
/// Call <see cref="Approve"/> or <see cref="Reject(string?)"/> to resume the conversation.
/// </summary>
/// <remarks>
/// Emitted by <see cref="ToolApprovalHandler"/> when M.E.AI surfaces a <see cref="ToolApprovalRequestContent"/>
/// (produced by wrapping a tool in an <see cref="ApprovalRequiredAIFunction"/>). As an
/// <see cref="IInteractiveBlock"/>, <see cref="AgentContext"/> awaits the user's decision and sends the
/// corresponding response back to the chat client.
/// </remarks>
public class ToolApprovalBlock : InteractiveFunctionBlock, IInteractiveBlock
{
    private readonly TaskCompletionSource<AIContent> _tcs = new();

    internal ToolApprovalBlock(
        FunctionInvocationContentBlock innerBlock,
        ToolApprovalRequestContent request)
        : base(innerBlock)
    {
        ApprovalRequest = request;
        Status = ApprovalStatus.Pending;
    }

    public ApprovalStatus Status { get; private set; }

    public ToolApprovalRequestContent ApprovalRequest { get; }

    public void Approve()
    {
        Status = ApprovalStatus.Approved;
        var response = ApprovalRequest.CreateResponse(approved: true);
        NotifyChanged();
        _tcs.TrySetResult(response);
    }

    public void Reject(string? reason = null)
    {
        Status = ApprovalStatus.Rejected;
        var response = ApprovalRequest.CreateResponse(approved: false);
        NotifyChanged();
        _tcs.TrySetResult(response);
    }

    public Task<AIContent> GetResultAsync(CancellationToken cancellationToken = default)
        => _tcs.Task.WaitAsync(cancellationToken);
}
