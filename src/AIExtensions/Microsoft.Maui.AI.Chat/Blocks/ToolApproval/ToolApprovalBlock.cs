// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A human-in-the-loop approval request: the model wants to call a tool that requires consent.
/// Call <see cref="Approve"/> or <see cref="Reject(string?)"/> to resume the conversation.
/// </summary>
/// <remarks>
/// Emitted by <see cref="ToolApprovalHandler"/> when M.E.AI surfaces a <see cref="ToolApprovalRequestContent"/>
/// (produced by wrapping a tool in an <see cref="ApprovalRequiredAIFunction"/>). Its
/// <see cref="InteractiveFunctionBlock.InnerBlock"/> is the block that the registered handlers create for the
/// call, as they would without the approval: the built-in call block, a typed block for the tool, a block of the
/// app's own type, or the call of a UI action. That block receives the call's result once the call ran or was
/// rejected. As an <see cref="IInteractiveBlock"/>, <see cref="AgentContext"/> awaits the user's decision and
/// sends the corresponding response back to the chat client. A decision raises
/// <see cref="ContentBlock.PropertyChanged"/> for <see cref="Status"/> and <see cref="IsAwaitingInput"/>.
/// </remarks>
public class ToolApprovalBlock : InteractiveFunctionBlock, IInteractiveBlock
{
    private static readonly PropertyChangedEventArgs s_statusChanged = new(nameof(Status));

    private readonly TaskCompletionSource<AIContent> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ApprovalStatus _status;

    internal ToolApprovalBlock(
        ContentBlock innerBlock,
        FunctionInvocationContentBlock callBlock,
        ToolApprovalRequestContent request)
        : base(innerBlock, callBlock)
    {
        ApprovalRequest = request;
        _status = ApprovalStatus.Pending;
        CaptureAwaitingInput();
    }

    public ApprovalStatus Status => _status;

    public ToolApprovalRequestContent ApprovalRequest { get; }

    /// <summary>
    /// Gets whether the approval is <see cref="ApprovalStatus.Pending"/> and its turn can still send a
    /// decision.
    /// </summary>
    public override bool IsAwaitingInput => Status == ApprovalStatus.Pending && !_tcs.Task.IsCompleted;

    /// <summary>
    /// Gets the registered UI action the approval is for, which the client runs once the call is approved.
    /// </summary>
    internal UIActionBlock? UIAction { get; init; }

    public void Approve()
    {
        Resolve(ApprovalStatus.Approved, approved: true, reason: null);
    }

    public void Reject(string? reason = null)
    {
        Resolve(ApprovalStatus.Rejected, approved: false, reason);
    }

    public Task<AIContent> GetResultAsync(CancellationToken cancellationToken = default)
        => _tcs.Task.WaitAsync(cancellationToken);

    internal void RestoreResponse(ToolApprovalResponseContent response)
    {
        if (response.RequestId != ApprovalRequest.RequestId)
            return;

        var status = response.Approved
            ? ApprovalStatus.Approved
            : ApprovalStatus.Rejected;
        var changed = status != _status;
        _status = status;
        _tcs.TrySetResult(response);
        if (changed)
            RaisePropertyChanged(s_statusChanged);
        ReportAwaitingInputChanged();
    }

    /// <summary>Stops waiting for a decision because the turn ended; later decisions are ignored.</summary>
    /// <returns>Whether the approval stopped waiting for a decision.</returns>
    protected override bool Abandon()
    {
        var wasAwaitingInput = IsAwaitingInput;
        _tcs.TrySetCanceled();
        UIAction?.InvokeAbandon();
        return wasAwaitingInput;
    }

    private void Resolve(ApprovalStatus status, bool approved, string? reason)
    {
        if (Status != ApprovalStatus.Pending || _tcs.Task.IsCompleted)
            return;

        _status = status;
        var response = ApprovalRequest.CreateResponse(approved, reason);
        _tcs.TrySetResult(response);

        RaisePropertyChanged(s_statusChanged);
        ReportAwaitingInputChanged();
    }
}
