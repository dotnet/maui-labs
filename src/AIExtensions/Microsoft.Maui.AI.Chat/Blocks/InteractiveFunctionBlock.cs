// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Base class for function blocks that pause the conversation for input, wrapping the inner
/// <see cref="FunctionInvocationContentBlock"/> that holds the call and result.
/// </summary>
/// <remarks>Base of <see cref="ToolApprovalBlock"/>; pairs with <see cref="IInteractiveBlock"/>.</remarks>
public abstract class InteractiveFunctionBlock(FunctionInvocationContentBlock innerBlock) : ContentBlock
{
    public FunctionInvocationContentBlock InnerBlock { get; } = innerBlock;

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
    /// The block raises <see cref="ContentBlock.OnChanged(Action)"/> when the value changes, so a view that
    /// refreshes on it, such as a chat control's block template, can bind its buttons to this property. A
    /// derived block that waits for input overrides it and raises the change notification the same way.
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
}
