// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Base class for blocks that pause the conversation for input about a function call: an approval, or a UI
/// action. <see cref="InnerBlock"/> is the block that shows the call, and the call and its result are this
/// block's own <see cref="Call"/> and <see cref="Result"/>.
/// </summary>
/// <remarks>
/// Base of <see cref="ToolApprovalBlock"/> and <see cref="UIActionBlock"/>; pairs with <see cref="IInteractiveBlock"/>.
/// When the call or its outcome changes, this block raises <see cref="ContentBlock.PropertyChanged"/> for
/// <see cref="Call"/>, <see cref="ToolName"/>, <see cref="Arguments"/>, <see cref="Result"/>,
/// <see cref="HasResult"/> or <see cref="IsAbandoned"/>, and for all six when everything may have changed.
/// </remarks>
public abstract class InteractiveFunctionBlock : ContentBlock
{
    private static readonly PropertyChangedEventArgs s_callChanged = new(nameof(Call));
    private static readonly PropertyChangedEventArgs s_toolNameChanged = new(nameof(ToolName));
    private static readonly PropertyChangedEventArgs s_argumentsChanged = new(nameof(Arguments));
    private static readonly PropertyChangedEventArgs s_resultChanged = new(nameof(Result));
    private static readonly PropertyChangedEventArgs s_hasResultChanged = new(nameof(HasResult));
    private static readonly PropertyChangedEventArgs s_isAbandonedChanged = new(nameof(IsAbandoned));
    private static readonly PropertyChangedEventArgs s_isAwaitingInputChanged = new(nameof(IsAwaitingInput));

    private bool? _reportedAwaitingInput;

    /// <summary>Initializes a new instance of the <see cref="InteractiveFunctionBlock"/> class.</summary>
    /// <param name="innerBlock">The block that shows the call and its result.</param>
    public InteractiveFunctionBlock(FunctionInvocationContentBlock innerBlock)
        : this(innerBlock, innerBlock)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InteractiveFunctionBlock"/> class.</summary>
    /// <param name="innerBlock">The block that shows the call, of any type.</param>
    /// <param name="callBlock">
    /// The block that holds the call and its result: <paramref name="innerBlock"/> when it is a function block,
    /// otherwise one that the engine keeps for it.
    /// </param>
    private protected InteractiveFunctionBlock(ContentBlock innerBlock, FunctionInvocationContentBlock callBlock)
    {
        ArgumentNullException.ThrowIfNull(innerBlock);
        ArgumentNullException.ThrowIfNull(callBlock);
        InnerBlock = innerBlock;
        CallBlock = callBlock;
        callBlock.PropertyChanged += OnCallBlockPropertyChanged;
    }

    /// <summary>
    /// Gets the block that shows the call: the built-in call block, a typed block that a handler or
    /// <c>[ToolBlock]</c> created for the tool, or a block of the app's own type.
    /// </summary>
    /// <remarks>
    /// A <see cref="UIActionBlock"/>'s inner block is always a <see cref="FunctionInvocationContentBlock"/>,
    /// because the action shows its result in it.
    /// </remarks>
    public ContentBlock InnerBlock { get; }

    /// <summary>
    /// Gets the block that holds the call and its result: <see cref="InnerBlock"/> when it is a function block,
    /// otherwise one that the engine keeps and nothing shows.
    /// </summary>
    internal FunctionInvocationContentBlock CallBlock { get; }

    public FunctionCallContent? Call => CallBlock.Call;

    public FunctionResultContent? Result => CallBlock.Result;

    public string? ToolName => CallBlock.ToolName;

    public IDictionary<string, object?>? Arguments => CallBlock.Arguments;

    public bool HasResult => CallBlock.HasResult;

    /// <summary>
    /// Gets whether the call will never get a result, because its turn ended without one: for example an
    /// approval or UI action left unanswered when the turn stopped. See
    /// <see cref="FunctionInvocationContentBlock.IsAbandoned"/>.
    /// </summary>
    public bool IsAbandoned => CallBlock.IsAbandoned;

    /// <summary>
    /// Gets whether the block waits for the user: an approval that is still pending, or a manual UI action
    /// that has not run. False once it was answered, while it runs, and once its turn stopped or failed or
    /// the block was restored, when an answer would be ignored.
    /// </summary>
    /// <remarks>
    /// The engine's blocks raise <see cref="ContentBlock.PropertyChanged"/> for this property when the value
    /// changes, so a view can bind its buttons to it. A derived block that waits for input overrides it and
    /// raises the change the same way.
    /// </remarks>
    public virtual bool IsAwaitingInput => false;

    /// <summary>
    /// Records the current <see cref="IsAwaitingInput"/>, which <see cref="ReportAwaitingInputChanged"/>
    /// compares against. Called once the engine's block is constructed, or when it is restored silently.
    /// </summary>
    internal void CaptureAwaitingInput() => _reportedAwaitingInput = IsAwaitingInput;

    internal override void OnTurnSet(ConversationTurn? turn)
    {
        InnerBlock.Turn = turn;
        if (!ReferenceEquals(CallBlock, InnerBlock))
            CallBlock.Turn = turn;
        if (this is ToolApprovalBlock { UIAction: { } action })
            action.Turn = turn;
    }

    /// <summary>
    /// Raises <see cref="ContentBlock.PropertyChanged"/> for <see cref="IsAwaitingInput"/> when it changed
    /// since it was last captured or reported.
    /// </summary>
    /// <returns>Whether the value changed.</returns>
    internal bool ReportAwaitingInputChanged()
    {
        if (_reportedAwaitingInput is not { } reported)
            return false;

        var current = IsAwaitingInput;
        if (current == reported)
            return false;

        _reportedAwaitingInput = current;
        RaisePropertyChanged(s_isAwaitingInputChanged);
        return true;
    }

    /// <summary>Raises the changes of this block that follow from a change of the call or its outcome.</summary>
    /// <param name="propertyName">The property that changed, or empty when all of them may have changed.</param>
    private protected virtual void OnCallChanged(string? propertyName) => ReportAwaitingInputChanged();

    private void OnCallBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or "":
                RaisePropertyChanged(s_callChanged);
                RaisePropertyChanged(s_toolNameChanged);
                RaisePropertyChanged(s_argumentsChanged);
                RaisePropertyChanged(s_resultChanged);
                RaisePropertyChanged(s_hasResultChanged);
                RaisePropertyChanged(s_isAbandonedChanged);
                break;
            case nameof(Call):
            case nameof(ToolName):
            case nameof(Arguments):
            case nameof(Result):
            case nameof(HasResult):
            case nameof(IsAbandoned):
                RaisePropertyChanged(e);
                break;
            default:
                return;
        }

        OnCallChanged(e.PropertyName);
    }
}
