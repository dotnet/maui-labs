#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using Xunit;

namespace Comet.Tests.BaristaNotes.Services;

public sealed class BaristaNotesAIAdviceStateTests
{
    static TaskCompletionSource<Exception>? s_observedFault;
    static int s_observedFaultCount;

    [Fact]
    public async Task StartAsync_UnconfiguredProvider_TerminatesAsUnavailable()
    {
        var service = new ControllableAdviceService { IsConfigured = false };
        using var session = CreateSession(service);

        await session.StartAsync();

        Assert.Equal(AIAdviceRequestStatus.Unavailable, session.State.Status);
        Assert.Contains("not configured", session.State.Message);
        Assert.Equal(0, service.AdviceCalls);
    }

    [Fact]
    public async Task Cancel_CurrentRequest_PublishesCancelledImmediately()
    {
        var started = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ControllableAdviceService
        {
            Advice = async (_, token) =>
            {
                started.TrySetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return SuccessAdvice();
            }
        };
        using var session = CreateSession(service);

        var request = session.StartAsync();
        var requestToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Cancel();

        Assert.True(requestToken.IsCancellationRequested);
        Assert.Equal(AIAdviceRequestStatus.Cancelled, session.State.Status);
        await request;
    }

    [Fact]
    public async Task Cancel_LateCompletionCannotOverwriteCancelledState()
    {
        var completion = new TaskCompletionSource<AIAdviceResponseDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ControllableAdviceService
        {
            Advice = (_, _) =>
            {
                started.TrySetResult();
                return completion.Task;
            }
        };
        using var session = CreateSession(service);

        var request = session.StartAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Cancel();
        completion.TrySetResult(SuccessAdvice());
        await request;

        Assert.Equal(AIAdviceRequestStatus.Cancelled, session.State.Status);
        Assert.Null(session.State.Advice);
    }

    [Fact]
    public async Task Timeout_NonCooperativeService_IsHardBoundedAndPublishesOnce()
    {
        var never = new TaskCompletionSource<AIAdviceResponseDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new List<AIAdviceRequestState>();
        var service = new ControllableAdviceService
        {
            Advice = (_, _) => never.Task,
        };
        using var session = CreateSession(
            service,
            states.Add,
            TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(AIAdviceRequestStatus.Failed, session.State.Status);
        Assert.Contains("timed out", session.State.Message);
        Assert.Equal(
            [AIAdviceRequestStatus.Loading, AIAdviceRequestStatus.Failed],
            states.ConvertAll(state => state.Status));

        never.TrySetResult(SuccessAdvice("late"));
        await Task.Yield();

        Assert.Equal(AIAdviceRequestStatus.Failed, session.State.Status);
        Assert.Equal(2, states.Count);
    }

    [Fact]
    public async Task Timeout_LateResultThenReopen_PreservesFreshGeneration()
    {
        var firstCompletion = new TaskCompletionSource<AIAdviceResponseDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new ControllableAdviceService
        {
            Advice = (_, _) => Interlocked.Increment(ref calls) == 1
                ? firstCompletion.Task
                : Task.FromResult(SuccessAdvice("retry")),
        };
        using var session = CreateSession(
            service,
            timeout: TimeSpan.FromMilliseconds(50));

        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AIAdviceRequestStatus.Failed, session.State.Status);

        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AIAdviceRequestStatus.Success, session.State.Status);
        Assert.Equal("retry", session.State.Advice?.Source);

        firstCompletion.TrySetResult(SuccessAdvice("late"));
        await Task.Yield();

        Assert.Equal(AIAdviceRequestStatus.Success, session.State.Status);
        Assert.Equal("retry", session.State.Advice?.Source);
    }

    [Fact]
    public async Task Timeout_LateFault_IsObservedWithoutDuplicateTerminalState()
    {
        var completion = new TaskCompletionSource<AIAdviceResponseDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = ResetObservedFault();
        var states = new List<AIAdviceRequestState>();
        var service = new ControllableAdviceService
        {
            Advice = (_, _) => completion.Task,
        };
        using var session = CreateSession(
            service,
            states.Add,
            TimeSpan.FromMilliseconds(50),
            ObserveAbandonedFault);

        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        completion.TrySetException(new HttpRequestException("late timeout fault"));

        var lateFault = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<HttpRequestException>(lateFault);
        Assert.Equal(AIAdviceRequestStatus.Failed, session.State.Status);
        Assert.Equal(
            [AIAdviceRequestStatus.Loading, AIAdviceRequestStatus.Failed],
            states.ConvertAll(state => state.Status));
    }

    [Fact]
    public async Task Cancel_LateFault_IsObservedAndCancelledRemainsTerminal()
    {
        var completion = new TaskCompletionSource<AIAdviceResponseDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = ResetObservedFault();
        var states = new List<AIAdviceRequestState>();
        var service = new ControllableAdviceService
        {
            Advice = (_, _) =>
            {
                started.TrySetResult();
                return completion.Task;
            },
        };
        using var session = CreateSession(
            service,
            states.Add,
            abandonedFaultObserver: ObserveAbandonedFault);

        var request = session.StartAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Cancel();
        completion.TrySetException(new InvalidOperationException("late cancel fault"));

        await request.WaitAsync(TimeSpan.FromSeconds(2));
        var lateFault = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<InvalidOperationException>(lateFault);
        Assert.Equal(AIAdviceRequestStatus.Cancelled, session.State.Status);
        Assert.Equal(
            [AIAdviceRequestStatus.Loading, AIAdviceRequestStatus.Cancelled],
            states.ConvertAll(state => state.Status));
    }

    [Fact]
    public async Task ActiveFault_PublishesFailureAndIsNotTreatedAsAbandoned()
    {
        ResetObservedFault();
        var states = new List<AIAdviceRequestState>();
        var service = new ControllableAdviceService
        {
            Advice = (_, _) => Task.FromException<AIAdviceResponseDto>(
                new Exception("active failure")),
        };
        using var session = CreateSession(
            service,
            states.Add,
            abandonedFaultObserver: ObserveAbandonedFault);

        await session.StartAsync();

        Assert.Equal(AIAdviceRequestStatus.Failed, session.State.Status);
        Assert.Equal(0, Volatile.Read(ref s_observedFaultCount));
        Assert.Equal(
            [AIAdviceRequestStatus.Loading, AIAdviceRequestStatus.Failed],
            states.ConvertAll(state => state.Status));
    }

    [Fact]
    public async Task Reopen_StartsCleanGeneration_AndIgnoresPriorCompletion()
    {
        var firstCompletion = new TaskCompletionSource<AIAdviceResponseDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new ControllableAdviceService
        {
            Advice = (_, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstStarted.TrySetResult();
                    return firstCompletion.Task;
                }
                return Task.FromResult(SuccessAdvice("fresh"));
            }
        };
        using var session = CreateSession(service);

        var first = session.StartAsync();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Cancel();
        await session.StartAsync();

        Assert.Equal(AIAdviceRequestStatus.Success, session.State.Status);
        Assert.Equal("fresh", session.State.Advice?.Source);

        firstCompletion.TrySetResult(new AIAdviceResponseDto
        {
            Success = false,
            ErrorCode = AIAdviceErrors.Unexpected,
            ErrorMessage = "stale failure"
        });
        await first;

        Assert.Equal(AIAdviceRequestStatus.Success, session.State.Status);
        Assert.Equal("fresh", session.State.Advice?.Source);
    }

    [Fact]
    public async Task Dispose_CancelsCurrentRequest_AndSuppressesLatePublication()
    {
        var completion = new TaskCompletionSource<AIAdviceResponseDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new List<AIAdviceRequestState>();
        var service = new ControllableAdviceService
        {
            Advice = (_, token) =>
            {
                started.TrySetResult(token);
                return completion.Task;
            }
        };
        var session = CreateSession(service, states.Add);

        var request = session.StartAsync();
        var requestToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Dispose();
        completion.TrySetResult(SuccessAdvice());
        await request;

        Assert.True(requestToken.IsCancellationRequested);
        Assert.DoesNotContain(states, state => state.Status == AIAdviceRequestStatus.Success);
    }

    [Fact]
    public async Task Dispose_NonCooperativeService_ReleasesWaitingRequest()
    {
        var started = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ControllableAdviceService
        {
            Advice = (_, token) =>
            {
                started.TrySetResult(token);
                return new TaskCompletionSource<AIAdviceResponseDto>(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task;
            },
        };
        var states = new List<AIAdviceRequestState>();
        var session = CreateSession(service, states.Add);

        var request = session.StartAsync();
        var requestToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Dispose();
        await request.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(requestToken.IsCancellationRequested);
        Assert.DoesNotContain(states, state => state.Status is
            AIAdviceRequestStatus.Success or
            AIAdviceRequestStatus.Failed or
            AIAdviceRequestStatus.Unavailable);
    }

    [Fact]
    public async Task Dispose_ExternallyRootedNeverTask_DoesNotRetainSessionOrUiOwner()
    {
        var never = new TaskCompletionSource<AIAdviceResponseDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var (sessionReference, ownerReference) =
            await CreateDisposedAbandonedSessionAsync(never);

        ForceCollection();

        Assert.False(sessionReference.IsAlive);
        Assert.False(ownerReference.IsAlive);
        GC.KeepAlive(never);
    }

    [Fact]
    public void PromptBuilder_PreservesSourceContext_WithoutStorageMetadata()
    {
        var context = new AIAdviceRequestDto
        {
            ShotId = 73,
            CurrentShot = new ShotContextDto
            {
                BrewMethod = BrewMethod.PourOver,
                DrinkType = "Pour Over",
                DoseIn = 20,
                ActualOutput = 320,
                ActualTime = 195,
                GrindMicrons = 700,
                Rating = 3,
                TastingNotes = "floral",
                Timestamp = DateTime.UtcNow,
            },
            BeanInfo = new BeanContextDto
            {
                Name = "Test Bean",
                Roaster = "Test Roaster",
                Origin = "Ethiopia",
                DaysFromRoast = 8,
                Notes = "washed",
            },
            Equipment = new EquipmentContextDto
            {
                MachineName = "Kettle",
                GrinderName = "Hand grinder",
            },
            MadeFor = new UserProfileDto
            {
                Id = 99,
                Name = "Alex",
                Context = "Prefers bright filter coffee.",
                AvatarPath = "/private/profile/avatar.jpg",
                CreatedAt = new DateTime(2026, 9, 1),
            },
        };

        var prompt = AIPromptBuilder.BuildPrompt(context);
        var system = AIPromptBuilder.BuildAdviceSystemPrompt(BrewMethod.PourOver);

        Assert.Contains("## Current Shot", prompt);
        Assert.Contains("- Brew method: Pour Over", prompt);
        Assert.Contains("## Made For: Alex", prompt);
        Assert.Contains("Prefers bright filter coffee.", prompt);
        Assert.DoesNotContain("AvatarPath", prompt);
        Assert.DoesNotContain("/private/profile/avatar.jpg", prompt);
        Assert.DoesNotContain("CreatedAt", prompt);
        Assert.DoesNotContain("base64", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("byte[]", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do NOT apply assumptions from other brewing methods", system);
    }

    static AIAdviceRequestSession CreateSession(
        IAIAdviceService service,
        Action<AIAdviceRequestState>? stateChanged = null,
        TimeSpan? timeout = null,
        Action<Exception>? abandonedFaultObserver = null) =>
        new(
            service,
            () => 1,
            stateChanged ?? (_ => { }),
            action => action(),
            timeout ?? TimeSpan.FromSeconds(5),
            abandonedFaultObserver);

    static TaskCompletionSource<Exception> ResetObservedFault()
    {
        Volatile.Write(ref s_observedFaultCount, 0);
        var observed = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref s_observedFault, observed);
        return observed;
    }

    static void ObserveAbandonedFault(Exception exception)
    {
        Interlocked.Increment(ref s_observedFaultCount);
        Volatile.Read(ref s_observedFault)?.TrySetResult(exception);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<(WeakReference Session, WeakReference Owner)>
        CreateDisposedAbandonedSessionAsync(
            TaskCompletionSource<AIAdviceResponseDto> never)
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new AdviceUiOwner();
        var service = new ControllableAdviceService
        {
            Advice = (_, _) =>
            {
                started.TrySetResult();
                return never.Task;
            },
        };
        var session = new AIAdviceRequestSession(
            service,
            () => 1,
            owner.OnStateChanged,
            action => action(),
            TimeSpan.FromMinutes(5),
            owner.OnAbandonedFault);
        var request = session.StartAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var sessionReference = new WeakReference(session);
        var ownerReference = new WeakReference(owner);

        session.Dispose();
        await request.WaitAsync(TimeSpan.FromSeconds(2));
        return (sessionReference, ownerReference);
    }

    static void ForceCollection()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    static AIAdviceResponseDto SuccessAdvice(string source = "test") => new()
    {
        Success = true,
        Adjustments =
        [
            new ShotAdjustment
            {
                Parameter = "grind",
                Direction = "finer",
                Amount = "1 click",
            }
        ],
        Reasoning = "The brew ran quickly.",
        Source = source,
    };

    sealed class AdviceUiOwner
    {
        public AIAdviceRequestState? State { get; private set; }
        public void OnStateChanged(AIAdviceRequestState state) => State = state;
        public void OnAbandonedFault(Exception exception) => State = new(
            AIAdviceRequestStatus.Failed,
            exception.Message);
    }

    sealed class ControllableAdviceService : IAIAdviceService
    {
        public bool IsConfigured { get; init; } = true;
        public int AdviceCalls { get; private set; }
        public Func<int, CancellationToken, Task<AIAdviceResponseDto>> Advice { get; init; } =
            (_, _) => Task.FromResult(SuccessAdvice());

        public Task<bool> IsConfiguredAsync() => Task.FromResult(IsConfigured);

        public Task<AIAdviceResponseDto> GetAdviceForShotAsync(
            int shotId,
            CancellationToken cancellationToken = default)
        {
            AdviceCalls++;
            return Advice(shotId, cancellationToken);
        }

        public Task<string?> GetPassiveInsightAsync(
            int shotId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<AIRecommendationDto> GetRecommendationsForBeanAsync(
            int beanId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AIRecommendationDto { Success = false });
    }
}
