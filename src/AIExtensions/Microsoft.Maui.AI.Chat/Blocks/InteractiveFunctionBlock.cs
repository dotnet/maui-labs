// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Base class for function blocks that pause the conversation for input, wrapping the inner
/// <see cref="FunctionInvocationContentBlock"/> that holds the call and result.
/// </summary>
/// <remarks>
/// Base of <see cref="ToolApprovalBlock"/>; pairs with <see cref="IInteractiveBlock"/>. When the inner block
/// raises <see cref="ContentBlock.PropertyChanged"/> for <see cref="Call"/>, <see cref="ToolName"/>,
/// <see cref="Arguments"/>, <see cref="Result"/> or <see cref="HasResult"/>, this block raises it for its
/// property of the same name, and for all five when the inner block reports that everything changed.
/// </remarks>
public abstract class InteractiveFunctionBlock : ContentBlock
{
    private static readonly PropertyChangedEventArgs s_callChanged = new(nameof(Call));
    private static readonly PropertyChangedEventArgs s_toolNameChanged = new(nameof(ToolName));
    private static readonly PropertyChangedEventArgs s_argumentsChanged = new(nameof(Arguments));
    private static readonly PropertyChangedEventArgs s_resultChanged = new(nameof(Result));
    private static readonly PropertyChangedEventArgs s_hasResultChanged = new(nameof(HasResult));
    private static readonly PropertyChangedEventArgs s_isAwaitingInputChanged = new(nameof(IsAwaitingInput));

    private bool? _reportedAwaitingInput;

    /// <summary>Initializes a new instance of the <see cref="InteractiveFunctionBlock"/> class.</summary>
    /// <param name="innerBlock">The block that shows the call and its result.</param>
    public InteractiveFunctionBlock(FunctionInvocationContentBlock innerBlock)
    {
        ArgumentNullException.ThrowIfNull(innerBlock);
        InnerBlock = innerBlock;
        innerBlock.PropertyChanged += OnInnerBlockPropertyChanged;
    }

    public FunctionInvocationContentBlock InnerBlock { get; }

    public FunctionCallContent? Call => InnerBlock.Call;

    public FunctionResultContent? Result => InnerBlock.Result;

    public string? ToolName => InnerBlock.ToolName;

    public IDictionary<string, object?>? Arguments => InnerBlock.Arguments;

    public bool HasResult => InnerBlock.HasResult;

    /// <summary>
    /// Gets whether the block waits for the user: an approval that is still pending, or a manual UI action
    /// that has not run. False once it was answered, while it runs, and once its turn stopped or failed or
    /// the block was restored, when an answer would be ignored.
    /// </summary>
    /// <remarks>
    /// The engine's blocks raise <see cref="ContentBlock.PropertyChanged"/> for this property, and the
    /// <see cref="ContentBlock.OnChanged(Action)"/> callbacks, when the value changes, so a view can bind its
    /// buttons to it. A derived block that waits for input overrides it and reports the change the same way.
    /// </remarks>
    public virtual bool IsAwaitingInput => false;

    /// <summary>
    /// Stops accepting a decision or an invocation once nothing can send it: the turn stopped or failed, or
    /// the block was restored.
    /// </summary>
    /// <returns>
    /// Whether the block stopped waiting for input, so the caller raises the change notification once
    /// the turn's outcome is final.
    /// </returns>
    internal virtual bool Abandon() => false;

    /// <summary>
    /// Records the current <see cref="IsAwaitingInput"/>, which <see cref="ReportAwaitingInputChanged"/>
    /// compares against. Called once the engine's block is constructed, or when it is restored silently.
    /// </summary>
    internal void CaptureAwaitingInput() => _reportedAwaitingInput = IsAwaitingInput;

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

    /// <summary>Raises the changes of this block that follow from a change of the inner block.</summary>
    /// <param name="propertyName">The inner block's property, or empty when all of them may have changed.</param>
    private protected virtual void OnInnerBlockChanged(string? propertyName) => ReportAwaitingInputChanged();

    private void OnInnerBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or "":
                RaisePropertyChanged(s_callChanged);
                RaisePropertyChanged(s_toolNameChanged);
                RaisePropertyChanged(s_argumentsChanged);
                RaisePropertyChanged(s_resultChanged);
                RaisePropertyChanged(s_hasResultChanged);
                break;
            case nameof(Call):
            case nameof(ToolName):
            case nameof(Arguments):
            case nameof(Result):
            case nameof(HasResult):
                RaisePropertyChanged(e);
                break;
            default:
                return;
        }

        OnInnerBlockChanged(e.PropertyName);
    }
}
