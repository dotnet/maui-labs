// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A registered client-side action requested by the model.
/// </summary>
public class UIActionBlock : InteractiveFunctionBlock, IInteractiveBlock
{
    private readonly AIFunction _function;
    private readonly IServiceProvider? _services;
    private readonly TaskCompletionSource<AIContent> _resultSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _invocationStarted;

    internal UIActionBlock(
        AIFunction function,
        UIActionInvocationMode mode,
        FunctionInvocationContentBlock innerBlock,
        IServiceProvider? services)
        : base(innerBlock)
    {
        _function = function;
        Mode = mode;
        _services = services;
    }

    public bool IsComplete => Result is not null;

    /// <summary>Gets whether the action is invoked automatically or by the UI.</summary>
    public UIActionInvocationMode Mode { get; }

    /// <summary>
    /// Executes the registered action once and returns its function result. The cancellation token
    /// controls the initial invocation; later callers can cancel their wait through
    /// <see cref="GetResultAsync"/>.
    /// </summary>
    public Task<AIContent> InvokeAsync(CancellationToken cancellationToken = default)
    {
        if (!_invocationStarted && !IsComplete)
        {
            _invocationStarted = true;
            _ = InvokeCoreAsync(cancellationToken);
        }

        return _resultSource.Task;
    }

    public Task<AIContent> GetResultAsync(CancellationToken cancellationToken = default)
        => _resultSource.Task.WaitAsync(cancellationToken);

    internal void SetResult(FunctionResultContent result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InnerBlock.Result = result;
        _resultSource.TrySetResult(result);
        NotifyChanged();
    }

    private async Task InvokeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var arguments = new AIFunctionArguments(
                Call?.Arguments ?? new Dictionary<string, object?>())
            {
                Services = _services,
            };
            var result = await _function.InvokeAsync(arguments, cancellationToken);
            SetResult(new FunctionResultContent(Call!.CallId, result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _resultSource.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            _resultSource.TrySetException(exception);
        }
    }
}
