// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A registered client-side action requested by the model and executed automatically by
/// <see cref="AgentContext"/> without pausing for human input.
/// </summary>
public class UIActionBlock : InteractiveFunctionBlock, IInteractiveBlock
{
    private readonly AIFunction _function;
    private readonly IServiceProvider? _services;
    private Task<AIContent>? _invocation;

    internal UIActionBlock(
        AIFunction function,
        FunctionInvocationContentBlock innerBlock,
        IServiceProvider? services)
        : base(innerBlock)
    {
        _function = function;
        _services = services;
    }

    public bool IsComplete => Result is not null;

    /// <summary>Executes the registered action once and returns its function result.</summary>
    public Task<AIContent> InvokeAsync(CancellationToken cancellationToken = default)
    {
        _invocation ??= InvokeCoreAsync(cancellationToken);
        return _invocation;
    }

    public Task<AIContent> GetResultAsync(CancellationToken cancellationToken = default)
        => InvokeAsync(cancellationToken).WaitAsync(cancellationToken);

    private async Task<AIContent> InvokeCoreAsync(CancellationToken cancellationToken)
    {
        var arguments = new AIFunctionArguments(
            Call?.Arguments ?? new Dictionary<string, object?>())
        {
            Services = _services,
        };
        var result = await _function.InvokeAsync(arguments, cancellationToken);
        var functionResult = new FunctionResultContent(Call!.CallId, result);
        InnerBlock.Result = functionResult;
        NotifyChanged();
        return functionResult;
    }
}
