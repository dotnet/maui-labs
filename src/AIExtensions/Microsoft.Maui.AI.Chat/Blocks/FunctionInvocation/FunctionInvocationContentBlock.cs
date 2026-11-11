// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>A tool call together with its (eventual) result, matched by <c>CallId</c>.</summary>
/// <remarks>
/// Produced by <see cref="FunctionInvocationHandler"/> from M.E.AI <see cref="FunctionCallContent"/> and
/// <see cref="FunctionResultContent"/>. Subclass it (with a custom <see cref="ContentBlockHandler{TState}"/>)
/// to project a specific tool into a strongly-typed block. Server tools are invoked by
/// <see cref="FunctionInvokingChatClient"/>; the engine only renders the call and its result. Calls
/// marked <see cref="FunctionCallContent.InformationalOnly"/> (handled elsewhere, replayed, or
/// restored) are rendered too, and a turn shows each call id once even when the call reappears in
/// a later round. Setting <see cref="Call"/> raises <see cref="ContentBlock.PropertyChanged"/> for it and
/// for <see cref="ToolName"/> and <see cref="Arguments"/> when they change; setting <see cref="Result"/>
/// raises it for the result and for <see cref="HasResult"/> when that changes. A card bound to the block
/// needs nothing else: <see cref="Approval"/> is the approval that wraps the call, if any, and
/// <see cref="IsAbandoned"/> says the call will never get a result.
/// </remarks>
public class FunctionInvocationContentBlock : ContentBlock
{
    private static readonly PropertyChangedEventArgs s_callChanged = new(nameof(Call));
    private static readonly PropertyChangedEventArgs s_toolNameChanged = new(nameof(ToolName));
    private static readonly PropertyChangedEventArgs s_argumentsChanged = new(nameof(Arguments));
    private static readonly PropertyChangedEventArgs s_resultChanged = new(nameof(Result));
    private static readonly PropertyChangedEventArgs s_hasResultChanged = new(nameof(HasResult));
    private static readonly PropertyChangedEventArgs s_isAbandonedChanged = new(nameof(IsAbandoned));
    private static readonly PropertyChangedEventArgs s_approvalChanged = new(nameof(Approval));

    private FunctionCallContent? _call;
    private FunctionResultContent? _result;
    private ToolApprovalBlock? _approval;
    private bool _abandoned;

    public FunctionCallContent? Call
    {
        get => _call;
        set
        {
            var previous = _call;
            _call = value;
            if (value is not null && string.IsNullOrEmpty(Id))
            {
                Id = string.IsNullOrEmpty(value.CallId)
                    ? Guid.NewGuid().ToString("N")
                    : value.CallId;
            }

            if (ReferenceEquals(previous, value))
                return;

            RaisePropertyChanged(s_callChanged);
            if (!string.Equals(previous?.Name, value?.Name, StringComparison.Ordinal))
                RaisePropertyChanged(s_toolNameChanged);
            if (!ReferenceEquals(previous?.Arguments, value?.Arguments))
                RaisePropertyChanged(s_argumentsChanged);
        }
    }

    public FunctionResultContent? Result
    {
        get => _result;
        set
        {
            var hadResult = HasResult;
            if (SetResultSilently(value))
                ReportResultChanged(hadResult);
        }
    }

    public string? ToolName => Call?.Name;

    public IDictionary<string, object?>? Arguments => Call?.Arguments;

    public bool HasResult => Result is not null;

    /// <summary>
    /// Gets the approval that asks the user whether the call may run, or <see langword="null"/> when the call
    /// needs none.
    /// </summary>
    /// <remarks>
    /// Set when a <see cref="ToolApprovalBlock"/> wraps this block as its
    /// <see cref="InteractiveFunctionBlock.InnerBlock"/>: a typed block that a handler created for the call,
    /// the built-in block, or the call of a UI action that needs approval. A card reads the approval's
    /// <see cref="ToolApprovalBlock.Status"/> and <see cref="InteractiveFunctionBlock.IsAwaitingInput"/>
    /// through it, without searching the conversation. Setting it raises
    /// <see cref="ContentBlock.PropertyChanged"/>.
    /// </remarks>
    public ToolApprovalBlock? Approval
    {
        get => _approval;
        internal set
        {
            if (ReferenceEquals(_approval, value))
                return;

            _approval = value;
            RaisePropertyChanged(s_approvalChanged);
        }
    }

    /// <summary>Gets whether the call will never get a result, because its turn ended without one.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="AgentContext"/> sets it when the call's turn ends and the call has no <see cref="Result"/>:
    /// the turn completed without one, was stopped or failed, or left the call's approval or UI action
    /// unanswered. A restore sets it for a call without a recorded result. Until then a call without a
    /// result may still get one; once its turn ended, a call has a result or is abandoned.
    /// </para>
    /// <para>
    /// A rejected call is not abandoned: <see cref="FunctionInvokingChatClient"/> gives it a result that only
    /// says it was rejected, and its <see cref="Approval"/> has the status
    /// <see cref="ApprovalStatus.Rejected"/>. It is abandoned only when its turn ended before that result
    /// arrived.
    /// </para>
    /// <para>
    /// The change raises <see cref="ContentBlock.PropertyChanged"/> once the turn's outcome is final, after
    /// the changes of the turn and of the context. Setting a <see cref="Result"/> makes it false again.
    /// </para>
    /// </remarks>
    public bool IsAbandoned => _abandoned && !HasResult;

    /// <summary>
    /// Marks the call as never getting a result because its turn ended, unless it has one. The caller raises
    /// the change with <see cref="ReportAbandonedChanged"/> once the turn's outcome is final.
    /// </summary>
    /// <returns>Whether <see cref="IsAbandoned"/> became true.</returns>
    internal bool Abandon()
    {
        if (_abandoned || HasResult)
            return false;

        _abandoned = true;
        return true;
    }

    /// <summary>Raises the change of <see cref="IsAbandoned"/>.</summary>
    internal void ReportAbandonedChanged() => RaisePropertyChanged(s_isAbandonedChanged);

    /// <summary>Sets <see cref="Result"/> without raising its change, which the caller reports later.</summary>
    /// <returns>Whether the result changed.</returns>
    internal bool SetResultSilently(FunctionResultContent? value)
    {
        if (ReferenceEquals(_result, value))
            return false;

        _result = value;
        return true;
    }

    /// <summary>
    /// Raises the change of <see cref="Result"/>, and of <see cref="HasResult"/> and an abandoned call's
    /// <see cref="IsAbandoned"/> when it changed.
    /// </summary>
    internal void ReportResultChanged(bool hadResult)
    {
        RaisePropertyChanged(s_resultChanged);
        if (HasResult == hadResult)
            return;

        RaisePropertyChanged(s_hasResultChanged);
        if (_abandoned)
            RaisePropertyChanged(s_isAbandonedChanged);
    }
}
