// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A registered client-side action requested by the model.
/// </summary>
/// <remarks>
/// If the action throws, the model receives a generic error <see cref="FunctionResultContent"/>
/// (with <see cref="FunctionResultContent.Exception"/> set) instead of the turn failing, and the
/// exception is logged.
/// </remarks>
public class UIActionBlock : InteractiveFunctionBlock, IInteractiveBlock
{
    private readonly AIFunction _function;
    private readonly IServiceProvider? _services;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource<AIContent> _resultSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _invocationStarted;

    internal UIActionBlock(
        AIFunction function,
        UIActionInvocationMode mode,
        FunctionInvocationContentBlock innerBlock,
        IServiceProvider? services,
        ILogger? logger = null)
        : base(innerBlock)
    {
        _function = function;
        Mode = mode;
        _services = services;
        _logger = logger ?? NullLogger.Instance;
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
        if (!_invocationStarted && !IsComplete && !_resultSource.Task.IsCompleted)
        {
            _invocationStarted = true;
            _ = InvokeCoreAsync(cancellationToken);
        }

        return _resultSource.Task;
    }

    public Task<AIContent> GetResultAsync(CancellationToken cancellationToken = default)
        => _resultSource.Task.WaitAsync(cancellationToken);

    /// <summary>Stops waiting for a result because the turn ended; a later invocation does nothing.</summary>
    internal void Abandon() => _resultSource.TrySetCanceled();

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
            UIAgentLog.UIActionFailed(_logger, _function.Name, Call!.CallId, exception);
            SetResult(new FunctionResultContent(Call.CallId, "Error: Function failed.")
            {
                Exception = exception,
            });
        }
    }
}
