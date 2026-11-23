using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public enum AIAdviceRequestStatus
{
    Idle,
    Loading,
    Unavailable,
    Cancelled,
    Failed,
    Success,
}

public sealed record AIAdviceRequestState(
    AIAdviceRequestStatus Status,
    string Message,
    AIAdviceResponseDto? Advice = null);

public sealed class AIAdviceRequestSession : IDisposable
{
    readonly object _gate = new();
    readonly IAIAdviceService _service;
    readonly Func<int?> _getShotId;
    readonly Action<AIAdviceRequestState> _stateChanged;
    readonly Action<Action> _dispatch;
    readonly Action<Exception>? _abandonedFaultObserver;
    readonly TimeSpan _timeout;

    CancellationTokenSource? _activeRequest;
    AIAdviceRequestState _state = new(AIAdviceRequestStatus.Idle, string.Empty);
    long _generation;
    bool _disposed;

    public AIAdviceRequestSession(
        IAIAdviceService service,
        Func<int?> getShotId,
        Action<AIAdviceRequestState> stateChanged,
        Action<Action>? dispatch = null,
        TimeSpan? timeout = null)
        : this(
            service,
            getShotId,
            stateChanged,
            dispatch,
            timeout,
            abandonedFaultObserver: null)
    {
    }

    internal AIAdviceRequestSession(
        IAIAdviceService service,
        Func<int?> getShotId,
        Action<AIAdviceRequestState> stateChanged,
        Action<Action>? dispatch,
        TimeSpan? timeout,
        Action<Exception>? abandonedFaultObserver = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _getShotId = getShotId ?? throw new ArgumentNullException(nameof(getShotId));
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
        _dispatch = dispatch ?? (action => action());
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _abandonedFaultObserver = abandonedFaultObserver;
    }

    public AIAdviceRequestState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public Task StartAsync()
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        long generation;

        lock (_gate)
        {
            if (_disposed)
                return Task.CompletedTask;

            previous = _activeRequest;
            current = new CancellationTokenSource();
            _activeRequest = current;
            generation = ++_generation;
        }

        previous?.Cancel();
        Publish(
            generation,
            new AIAdviceRequestState(
                AIAdviceRequestStatus.Loading,
                "Analyzing your drink…"));
        return RunAsync(generation, current);
    }

    public void Cancel()
    {
        CancellationTokenSource? request;
        long generation;

        lock (_gate)
        {
            if (_disposed || _state.Status != AIAdviceRequestStatus.Loading)
                return;

            request = _activeRequest;
            _activeRequest = null;
            generation = ++_generation;
        }

        request?.Cancel();
        Publish(
            generation,
            new AIAdviceRequestState(
                AIAdviceRequestStatus.Cancelled,
                "Request cancelled."));
    }

    async Task RunAsync(long generation, CancellationTokenSource request)
    {
        using (request)
        using (var timeout = new CancellationTokenSource(_timeout))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
            request.Token,
            timeout.Token))
        {
            try
            {
                var shotId = _getShotId();
                if (!shotId.HasValue)
                {
                    Publish(
                        generation,
                        new AIAdviceRequestState(
                            AIAdviceRequestStatus.Failed,
                            "Save the drink first to get AI advice."));
                    return;
                }

                var configuredTask = _service.IsConfiguredAsync();
                var configured = await AwaitBoundedAsync(
                    configuredTask,
                    linked.Token);
                if (!configured)
                {
                    Publish(
                        generation,
                        new AIAdviceRequestState(
                            AIAdviceRequestStatus.Unavailable,
                            "AI advice is not configured on this device."));
                    return;
                }

                var adviceTask = _service.GetAdviceForShotAsync(
                    shotId.Value,
                    linked.Token);
                var response = await AwaitBoundedAsync(
                    adviceTask,
                    linked.Token);

                Publish(generation, FromResponse(response));
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested)
            {
                // Cancel() already published the user-visible state and advanced the generation.
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                Publish(
                    generation,
                    new AIAdviceRequestState(
                        AIAdviceRequestStatus.Failed,
                        "AI advice timed out. Please try again."));
            }
            catch (HttpRequestException)
            {
                Publish(
                    generation,
                    new AIAdviceRequestState(
                        AIAdviceRequestStatus.Unavailable,
                        "AI advice is temporarily unavailable."));
            }
            catch (InvalidOperationException)
            {
                Publish(
                    generation,
                    new AIAdviceRequestState(
                        AIAdviceRequestStatus.Unavailable,
                        "AI advice is not configured on this device."));
            }
            catch (Exception)
            {
                Publish(
                    generation,
                    new AIAdviceRequestState(
                        AIAdviceRequestStatus.Failed,
                        "AI advice failed. Please try again."));
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activeRequest, request))
                        _activeRequest = null;
                }
            }

            async Task<T> AwaitBoundedAsync<T>(
                Task<T> sourceTask,
                CancellationToken cancellationToken)
            {
                try
                {
                    return await sourceTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ObserveAbandonedFault(sourceTask, _abandonedFaultObserver);
                    throw;
                }
            }
        }
    }

    static void ObserveAbandonedFault(
        Task sourceTask,
        Action<Exception>? abandonedFaultObserver)
    {
        if (sourceTask.IsCompletedSuccessfully || sourceTask.IsCanceled)
            return;

        var staticObserver = abandonedFaultObserver?.Target is null
            ? abandonedFaultObserver
            : null;
        _ = sourceTask.ContinueWith(
            static (task, state) =>
            {
                var exception = task.Exception;
                if (exception is null)
                    return;

                var fault = exception.InnerException ?? exception;
                Trace.TraceWarning(
                    "An abandoned AI advice request faulted after cancellation ({0}).",
                    fault.GetType().Name);
                try
                {
                    (state as Action<Exception>)?.Invoke(fault);
                }
                catch (Exception observerError)
                {
                    Trace.TraceWarning(
                        "The abandoned AI advice fault observer failed ({0}).",
                        observerError.GetType().Name);
                }
            },
            staticObserver,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    static AIAdviceRequestState FromResponse(AIAdviceResponseDto response)
    {
        if (response.Success)
        {
            return new AIAdviceRequestState(
                AIAdviceRequestStatus.Success,
                $"Based on {response.HistoricalShotsCount} previous shot(s)",
                response);
        }

        return response.ErrorCode switch
        {
            AIAdviceErrors.Cancelled => new AIAdviceRequestState(
                AIAdviceRequestStatus.Cancelled,
                response.ErrorMessage ?? "Request cancelled."),
            AIAdviceErrors.Unavailable or AIAdviceErrors.Connectivity => new AIAdviceRequestState(
                AIAdviceRequestStatus.Unavailable,
                response.ErrorMessage ?? "AI advice is temporarily unavailable."),
            _ => new AIAdviceRequestState(
                AIAdviceRequestStatus.Failed,
                response.ErrorMessage ?? "Failed to get advice."),
        };
    }

    void Publish(long generation, AIAdviceRequestState state)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation)
                return;
            _state = state;
        }

        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_disposed || generation != _generation || !ReferenceEquals(_state, state))
                    return;
            }
            _stateChanged(state);
        });
    }

    public void Dispose()
    {
        CancellationTokenSource? request;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _generation++;
            request = _activeRequest;
            _activeRequest = null;
        }
        request?.Cancel();
    }
}
