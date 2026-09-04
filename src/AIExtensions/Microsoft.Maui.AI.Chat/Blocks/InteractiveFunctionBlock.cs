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
}
