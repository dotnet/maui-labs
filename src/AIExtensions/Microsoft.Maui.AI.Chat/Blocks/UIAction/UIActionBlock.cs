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
    private readonly IActiveEntry? _innerEntry;
    private readonly TaskCompletionSource<AIContent> _resultSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _invocationStarted;

    /// <param name="function">The registered action.</param>
    /// <param name="mode">Whether the action is invoked automatically or by the UI.</param>
    /// <param name="innerBlock">The block that shows the call and its result.</param>
    /// <param name="services">The services passed to the action.</param>
    /// <param name="logger">Logs a failed action.</param>
    /// <param name="innerEntry">
    /// The handler entry that created <paramref name="innerBlock"/>, which maps the result into it.
    /// </param>
    internal UIActionBlock(
        AIFunction function,
        UIActionInvocationMode mode,
        FunctionInvocationContentBlock innerBlock,
        IServiceProvider? services,
        ILogger? logger = null,
        IActiveEntry? innerEntry = null)
        : base(innerBlock)
    {
        _function = function;
        Mode = mode;
        _services = services;
        _logger = logger ?? NullLogger.Instance;
        _innerEntry = innerEntry;
    }

    public bool IsComplete => Result is not null;

    /// <summary>Gets whether the action is invoked automatically or by the UI.</summary>
    public UIActionInvocationMode Mode { get; }

    /// <summary>
    /// Executes the registered action once and returns its function result. Calls made while it runs, or
    /// after it finished, return the same result.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the action this call starts. When the action stops because of it, the returned task is
    /// canceled and the action can be invoked again; the turn keeps waiting for its result. Later callers
    /// can cancel their wait through <see cref="GetResultAsync"/>.
    /// </param>
    /// <returns>
    /// The action's result. The task is canceled when the turn ended before the action could return a
    /// result, for example after a stop or a failure, or for an action shown by a restore.
    /// </returns>
    public Task<AIContent> InvokeAsync(CancellationToken cancellationToken = default)
    {
        if (!_invocationStarted && !IsComplete && !_resultSource.Task.IsCompleted)
        {
            _invocationStarted = true;
            return InvokeCoreAsync(cancellationToken);
        }

        return _resultSource.Task;
    }

    public Task<AIContent> GetResultAsync(CancellationToken cancellationToken = default)
        => _resultSource.Task.WaitAsync(cancellationToken);

    /// <summary>Stops waiting for a result because the turn ended; a later invocation does nothing.</summary>
    internal override void Abandon() => _resultSource.TrySetCanceled();

    internal void SetResult(FunctionResultContent result)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            // A typed inner block, such as a [ToolBlock], maps the result with its own handler.
            if (_innerEntry?.Invoke(new BlockMappingContext(new ChatResponseUpdate(ChatRole.Tool, [result])))
                is { Kind: not HandleResult.ResultKind.Pass })
            {
                InnerBlock.InvokeNotifyChanged();
            }
        }
        finally
        {
            InnerBlock.Result ??= result;
            _resultSource.TrySetResult(result);
            NotifyChanged();
        }
    }

    private async Task<AIContent> InvokeCoreAsync(CancellationToken cancellationToken)
    {
        FunctionResultContent result;
        try
        {
            var arguments = new AIFunctionArguments(
                Call?.Arguments ?? new Dictionary<string, object?>())
            {
                Services = _services,
            };
            result = new FunctionResultContent(
                Call!.CallId,
                await _function.InvokeAsync(arguments, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only this invocation was canceled. The turn still waits for a result, so allow another one;
            // a turn that ended has abandoned the block, which stops a later invocation.
            _invocationStarted = false;
            throw;
        }
        catch (Exception exception)
        {
            UIAgentLog.UIActionFailed(_logger, _function.Name, Call!.CallId, exception);
            result = new FunctionResultContent(Call.CallId, "Error: Function failed.")
            {
                Exception = exception,
            };
        }

        try
        {
            SetResult(result);
        }
        catch (Exception exception)
        {
            // The result still reaches the model; only the block that shows it failed.
            UIAgentLog.UIActionResultNotShown(_logger, _function.Name, Call.CallId, exception);
        }

        // Canceled when the turn ended before the result arrived, as for every other caller.
        return await _resultSource.Task;
    }
}
