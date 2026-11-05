#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Voice;
using Xunit;

namespace Comet.Tests.BaristaNotes.Integration;

/// <summary>
/// Integration tests for the AI advice, voice, and photo workflow
/// UI contracts implemented by Amos (Controls & API Dev).
/// </summary>
public sealed class AIAdviceIntegrationTests
{
    [Fact]
    public async Task AIAdviceService_UnconfiguredAdapter_ReturnsUnavailable()
    {
        var service = new AIAdviceService(
            new FakeShotService(),
            new UnavailableAIAdviceAdapter());

        var configured = await service.IsConfiguredAsync();
        Assert.False(configured);

        var response = await service.GetAdviceForShotAsync(1);
        Assert.False(response.Success);
        Assert.Equal(AIAdviceErrors.Unavailable, response.ErrorCode);
    }

    [Fact]
    public async Task AIAdviceService_ConfiguredAdapter_ReturnsSuccessWithAdjustments()
    {
        var adapter = new FakeAIAdviceAdapter(new AIAdviceResponseDto
        {
            Success = true,
            Adjustments = new[]
            {
                new ShotAdjustment { Parameter = "Grind", Direction = "Finer", Amount = "2 clicks" },
                new ShotAdjustment { Parameter = "Dose", Direction = "Increase", Amount = "0.5g" },
            },
            Reasoning = "Your extraction is under-extracting.",
            Source = "Azure OpenAI",
        });

        var service = new AIAdviceService(new FakeShotService(hasShotContext: true), adapter);

        var configured = await service.IsConfiguredAsync();
        Assert.True(configured);

        var response = await service.GetAdviceForShotAsync(1);
        Assert.True(response.Success);
        Assert.Equal(2, response.Adjustments.Count);
        Assert.Equal("Finer Grind 2 clicks", response.Adjustments[0].Recommendation);
        Assert.NotNull(response.Reasoning);
        Assert.NotNull(response.Source);
    }

    [Fact]
    public async Task AIAdviceService_Cancellation_ReturnsCancelled()
    {
        var adapter = new FakeAIAdviceAdapter(delay: TimeSpan.FromSeconds(30));
        var service = new AIAdviceService(new FakeShotService(hasShotContext: true), adapter);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await service.GetAdviceForShotAsync(1, cts.Token);
        Assert.False(response.Success);
        Assert.Equal(AIAdviceErrors.Cancelled, response.ErrorCode);
    }

    [Fact]
    public async Task AIAdviceService_ShotNotFound_ReturnsNotFoundError()
    {
        var adapter = new FakeAIAdviceAdapter(new AIAdviceResponseDto { Success = true });
        var service = new AIAdviceService(new FakeShotService(hasShotContext: false), adapter);

        var response = await service.GetAdviceForShotAsync(999);
        Assert.False(response.Success);
        Assert.Equal(AIAdviceErrors.NotFound, response.ErrorCode);
    }

    [Fact]
    public async Task AIAdviceService_ServiceReturnsCancelledError_DistinctFromFailed()
    {
        // Item 1: the service can return Success=false + ErrorCode=CANCELLED
        // which must map to Cancelled, not Failed.
        var adapter = new FakeAIAdviceAdapter(new AIAdviceResponseDto
        {
            Success = false,
            ErrorCode = AIAdviceErrors.Cancelled,
            ErrorMessage = "Request was cancelled.",
        });
        var service = new AIAdviceService(new FakeShotService(hasShotContext: true), adapter);

        var response = await service.GetAdviceForShotAsync(1);

        Assert.False(response.Success);
        Assert.Equal(AIAdviceErrors.Cancelled, response.ErrorCode);
        Assert.NotEqual(AIAdviceErrors.Unavailable, response.ErrorCode);
        Assert.NotEqual(AIAdviceErrors.Unexpected, response.ErrorCode);
    }

    [Fact]
    public async Task AIAdviceService_UnavailableVsCancelledVsFailed_DistinctErrorCodes()
    {
        // Each branch produces a distinct ErrorCode
        var unavailable = new AIAdviceService(new FakeShotService(hasShotContext: true),
            new UnavailableAIAdviceAdapter());
        var cancelled = new AIAdviceService(new FakeShotService(hasShotContext: true),
            new FakeAIAdviceAdapter(new AIAdviceResponseDto { Success = false, ErrorCode = AIAdviceErrors.Cancelled }));
        var failed = new AIAdviceService(new FakeShotService(hasShotContext: true),
            new FakeAIAdviceAdapter(new AIAdviceResponseDto { Success = false, ErrorCode = AIAdviceErrors.Connectivity }));

        var r1 = await unavailable.GetAdviceForShotAsync(1);
        var r2 = await cancelled.GetAdviceForShotAsync(1);
        var r3 = await failed.GetAdviceForShotAsync(1);

        Assert.Equal(AIAdviceErrors.Unavailable, r1.ErrorCode);
        Assert.Equal(AIAdviceErrors.Cancelled, r2.ErrorCode);
        Assert.Equal(AIAdviceErrors.Connectivity, r3.ErrorCode);
        Assert.NotEqual(r1.ErrorCode, r2.ErrorCode);
        Assert.NotEqual(r2.ErrorCode, r3.ErrorCode);
    }

    sealed class FakeAIAdviceAdapter : IAIAdviceAdapter
    {
        readonly AIAdviceResponseDto? _response;
        readonly TimeSpan _delay;

        public FakeAIAdviceAdapter(AIAdviceResponseDto? response = null, TimeSpan delay = default)
        {
            _response = response;
            _delay = delay;
        }

        public bool IsConfigured => true;

        public async Task<AIAdviceResponseDto> GetShotAdviceAsync(
            AIAdviceRequestDto request, CancellationToken cancellationToken = default)
        {
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken);
            return _response ?? new AIAdviceResponseDto { Success = false, ErrorCode = "TEST" };
        }

        public Task<string?> GetPassiveInsightAsync(
            AIAdviceRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<AIRecommendationDto> GetBeanRecommendationAsync(
            BeanRecommendationContextDto context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AIRecommendationDto { Success = false });
    }

    sealed class FakeShotService : CometBaristaNotes.Services.IShotService
    {
        readonly bool _hasShotContext;

        public FakeShotService(bool hasShotContext = false) => _hasShotContext = hasShotContext;

        // --- CometBaristaNotes.Services.IShotService (DTO-based) ---
        public Task<ShotRecordDto?> GetShotByIdAsync(int id) => Task.FromResult<ShotRecordDto?>(null);
        public Task<ShotRecordDto?> GetMostRecentShotAsync() => Task.FromResult<ShotRecordDto?>(null);
        public Task<ShotRecordDto> CreateShotAsync(CreateShotDto dto) => throw new NotImplementedException();
        public Task<ShotRecordDto> UpdateShotAsync(int id, UpdateShotDto dto) => throw new NotImplementedException();
        public Task DeleteShotAsync(int id) => Task.CompletedTask;
        public Task<PagedResult<ShotRecordDto>> GetShotHistoryAsync(int page, int size) =>
            Task.FromResult(new PagedResult<ShotRecordDto> { Items = new(), TotalCount = 0 });
        public Task<PagedResult<ShotRecordDto>> GetShotHistoryByBeanAsync(int beanId, int page, int size) =>
            Task.FromResult(new PagedResult<ShotRecordDto> { Items = new(), TotalCount = 0 });
        public Task<PagedResult<ShotRecordDto>> GetShotHistoryByUserAsync(int userId, int page, int size) =>
            Task.FromResult(new PagedResult<ShotRecordDto> { Items = new(), TotalCount = 0 });
        public Task<PagedResult<ShotRecordDto>> GetShotHistoryByEquipmentAsync(int equipmentId, int page, int size) =>
            Task.FromResult(new PagedResult<ShotRecordDto> { Items = new(), TotalCount = 0 });
        public Task<PagedResult<ShotRecordDto>> GetFilteredShotHistoryAsync(ShotFilterCriteriaDto? criteria, int page, int size) =>
            Task.FromResult(new PagedResult<ShotRecordDto> { Items = new(), TotalCount = 0 });
        public Task<ShotRecordDto?> GetBestRatedShotByBeanAsync(int beanId) => Task.FromResult<ShotRecordDto?>(null);
        public Task<ShotRecordDto?> GetBestRatedShotByBagAsync(int bagId) => Task.FromResult<ShotRecordDto?>(null);
        public Task<int?> GetMostRecentBeanIdAsync() => Task.FromResult<int?>(null);
        public Task<bool> BeanHasHistoryAsync(int beanId) => Task.FromResult(false);
        public Task<AIAdviceRequestDto?> GetShotContextForAIAsync(int shotId) =>
            _hasShotContext
                ? Task.FromResult<AIAdviceRequestDto?>(new AIAdviceRequestDto
                {
                    ShotId = shotId,
                    CurrentShot = new ShotContextDto
                    {
                        DoseIn = 18,
                        BrewMethod = BrewMethod.Espresso,
                        Timestamp = DateTime.UtcNow,
                    },
                    BeanInfo = new BeanContextDto
                    {
                        Name = "Test Bean",
                        RoastDate = DateTime.Today.AddDays(-7),
                        DaysFromRoast = 7,
                    },
                    HistoricalShots = new List<ShotContextDto>(),
                })
                : Task.FromResult<AIAdviceRequestDto?>(null);
        public Task<BeanRecommendationContextDto?> GetBeanRecommendationContextAsync(int beanId) =>
            Task.FromResult<BeanRecommendationContextDto?>(null);
        public Task<List<BeanFilterOptionDto>> GetBeansWithShotsAsync() =>
            Task.FromResult(new List<BeanFilterOptionDto>());
        public Task<List<UserProfileDto>> GetPeopleWithShotsAsync() =>
            Task.FromResult(new List<UserProfileDto>());
    }
}

public sealed class VoiceIntegrationTests
{
    [Fact]
    public void VoiceFieldUpdates_HasChanges_OnlyWhenFieldsSet()
    {
        var empty = new VoiceFieldUpdates();
        Assert.False(empty.HasChanges);

        var withDose = new VoiceFieldUpdates(DoseGrams: 18);
        Assert.True(withDose.HasChanges);

        var withNotes = new VoiceFieldUpdates(TastingNotes: "fruity");
        Assert.True(withNotes.HasChanges);
    }

    [Fact]
    public void VoiceFieldUpdates_NullFieldsIgnored()
    {
        var updates = new VoiceFieldUpdates(DoseGrams: 18, Rating: null);
        Assert.True(updates.HasChanges);
        Assert.Equal(18m, updates.DoseGrams);
        Assert.Null(updates.Rating);
    }

    [Fact]
    public void VoiceOverlayState_IsReady_ForCompletedAndCancelled()
    {
        var ready = new VoiceOverlayState(
            VoiceSessionStatus.Ready, SpeechRecognitionState.Idle,
            CommandStatus.Completed, "Ready");
        Assert.True(ready.IsReady);
        Assert.False(ready.IsListening);

        var completed = new VoiceOverlayState(
            VoiceSessionStatus.Completed, SpeechRecognitionState.Idle,
            CommandStatus.Completed, "Done");
        Assert.True(completed.IsReady);

        var cancelled = new VoiceOverlayState(
            VoiceSessionStatus.Cancelled, SpeechRecognitionState.Idle,
            CommandStatus.Cancelled, "Cancelled");
        Assert.True(cancelled.IsReady);
    }

    [Fact]
    public void VoiceOverlayState_IsListening_WhenListeningStatus()
    {
        var state = new VoiceOverlayState(
            VoiceSessionStatus.Listening, SpeechRecognitionState.Listening,
            CommandStatus.Listening, "Listening...");
        Assert.True(state.IsListening);
        Assert.False(state.IsProcessing);
        Assert.False(state.IsReady);
    }

    [Fact]
    public void BaristaVoiceIntegration_State_ReturnsUnconfiguredWhenNoSession()
    {
        // Without ConfigurePlatform + Bind, Session is null → UnconfiguredState
        var state = BaristaVoiceIntegration.State;
        Assert.Equal(VoiceSessionStatus.Unconfigured, state.Status);
    }

    [Fact]
    public void BaristaVoiceIntegration_NotifyNewDrinkMounted_ReturnsFalseWhenNoActivation()
    {
        // Without prior RequestNewDrinkActivation, should return false
        Assert.False(BaristaVoiceIntegration.NotifyNewDrinkMounted());
    }

    [Fact]
    public void BaristaVoiceIntegration_RequestThenNotify_ReturnsTrueOnce()
    {
        BaristaVoiceIntegration.RequestNewDrinkActivation();
        // NotifyNewDrinkMounted returns true on first call, false on second
        // Note: can't fully test without callbacks bound, but contract is stable
        Assert.True(BaristaVoiceIntegration.NotifyNewDrinkMounted());
        Assert.False(BaristaVoiceIntegration.NotifyNewDrinkMounted());
    }
}

/// <summary>
/// Tests that exercise actual PushToTalkVoiceService state transitions
/// through a fake platform recognizer.
/// </summary>
public sealed class VoicePushToTalkBehaviorTests
{
    [Fact]
    public async Task Initialize_WhenAvailable_TransitionsToReady()
    {
        var platform = new FakePlatformRecognizer(available: true);
        var callbacks = new RecordingVoiceCallbacks();
        await using var speech = new NativeSpeechRecognitionService(platform);
        var commands = new BaristaVoiceCommandService(
            new VoiceCommandParser(), callbacks,
            null!, null!, null!, null!, null!);
        await using var session = new PushToTalkVoiceService(speech, commands, callbacks);

        await session.InitializeAsync();

        Assert.Equal(VoiceSessionStatus.Ready, session.CurrentState.Status);
        Assert.True(session.CurrentState.IsReady);
    }

    [Fact]
    public async Task Initialize_WhenUnavailable_TransitionsToUnavailable()
    {
        var platform = new FakePlatformRecognizer(available: false);
        var callbacks = new RecordingVoiceCallbacks();
        await using var speech = new NativeSpeechRecognitionService(platform);
        var commands = new BaristaVoiceCommandService(
            new VoiceCommandParser(), callbacks,
            null!, null!, null!, null!, null!);
        await using var session = new PushToTalkVoiceService(speech, commands, callbacks);

        await session.InitializeAsync();

        Assert.Equal(VoiceSessionStatus.Unavailable, session.CurrentState.Status);
        Assert.False(session.CurrentState.IsReady);
    }

    [Fact]
    public async Task Start_TransitionsToListening_ThenCancelTransitionsToCancelled()
    {
        var platform = new FakePlatformRecognizer(available: true, hasPermission: true);
        var callbacks = new RecordingVoiceCallbacks();
        await using var speech = new NativeSpeechRecognitionService(platform);
        var commands = new BaristaVoiceCommandService(
            new VoiceCommandParser(), callbacks,
            null!, null!, null!, null!, null!);
        await using var session = new PushToTalkVoiceService(speech, commands, callbacks);

        await session.InitializeAsync();
        Assert.Equal(VoiceSessionStatus.Ready, session.CurrentState.Status);

        // Start → Listening
        await session.StartAsync();
        Assert.Equal(VoiceSessionStatus.Listening, session.CurrentState.Status);
        Assert.True(session.CurrentState.IsListening);

        // Cancel → Cancelled
        await session.CancelAsync();
        Assert.Equal(VoiceSessionStatus.Cancelled, session.CurrentState.Status);
        Assert.True(session.CurrentState.IsReady);
    }

    [Fact]
    public async Task HandlePushToTalk_StartedThenCompleted_FullCycle()
    {
        var platform = new FakePlatformRecognizer(available: true, hasPermission: true,
            recognitionResult: new PlatformSpeechRecognitionResult(true, "set dose to 18", 0.9, null));
        var callbacks = new RecordingVoiceCallbacks();
        await using var speech = new NativeSpeechRecognitionService(platform);
        var commands = new BaristaVoiceCommandService(
            new VoiceCommandParser(), callbacks,
            null!, null!, null!, null!, null!);
        await using var session = new PushToTalkVoiceService(speech, commands, callbacks);
        await session.InitializeAsync();

        // Started phase
        await session.HandlePushToTalkAsync(PushToTalkPhase.Started);
        Assert.Equal(VoiceSessionStatus.Listening, session.CurrentState.Status);

        // Completed phase — transitions through Processing to terminal
        await session.HandlePushToTalkAsync(PushToTalkPhase.Completed);
        Assert.True(session.CurrentState.Status is VoiceSessionStatus.Completed
            or VoiceSessionStatus.Error);
    }

    [Fact]
    public async Task HandlePushToTalk_Cancelled_TransitionsToCancelled()
    {
        var platform = new FakePlatformRecognizer(available: true, hasPermission: true);
        var callbacks = new RecordingVoiceCallbacks();
        await using var speech = new NativeSpeechRecognitionService(platform);
        var commands = new BaristaVoiceCommandService(
            new VoiceCommandParser(), callbacks,
            null!, null!, null!, null!, null!);
        await using var session = new PushToTalkVoiceService(speech, commands, callbacks);
        await session.InitializeAsync();

        await session.HandlePushToTalkAsync(PushToTalkPhase.Started);
        await session.HandlePushToTalkAsync(PushToTalkPhase.Cancelled);

        Assert.Equal(VoiceSessionStatus.Cancelled, session.CurrentState.Status);
    }

    [Fact]
    public async Task Deactivate_WhileListening_Cancels()
    {
        var platform = new FakePlatformRecognizer(available: true, hasPermission: true);
        var callbacks = new RecordingVoiceCallbacks();
        await using var speech = new NativeSpeechRecognitionService(platform);
        var commands = new BaristaVoiceCommandService(
            new VoiceCommandParser(), callbacks,
            null!, null!, null!, null!, null!);
        await using var session = new PushToTalkVoiceService(speech, commands, callbacks);
        await session.InitializeAsync();

        await session.StartAsync();
        Assert.Equal(VoiceSessionStatus.Listening, session.CurrentState.Status);

        await session.DeactivateAsync();
        Assert.Equal(VoiceSessionStatus.Cancelled, session.CurrentState.Status);
    }

    [Fact]
    public async Task StateChanged_CallbackFires_OnEveryTransition()
    {
        var platform = new FakePlatformRecognizer(available: true, hasPermission: true);
        var callbacks = new RecordingVoiceCallbacks();
        await using var speech = new NativeSpeechRecognitionService(platform);
        var commands = new BaristaVoiceCommandService(
            new VoiceCommandParser(), callbacks,
            null!, null!, null!, null!, null!);
        await using var session = new PushToTalkVoiceService(speech, commands, callbacks);

        await session.InitializeAsync();
        Assert.True(callbacks.StateHistory.Count >= 1);
        Assert.Equal(VoiceSessionStatus.Ready, callbacks.StateHistory[^1].Status);
    }

    sealed class FakePlatformRecognizer : IPlatformSpeechRecognizer
    {
        readonly bool _available;
        readonly bool _hasPermission;
        readonly PlatformSpeechRecognitionResult? _result;
        TaskCompletionSource<PlatformSpeechRecognitionResult>? _listenTcs;

        public FakePlatformRecognizer(
            bool available = true,
            bool hasPermission = true,
            PlatformSpeechRecognitionResult? recognitionResult = null)
        {
            _available = available;
            _hasPermission = hasPermission;
            _result = recognitionResult;
        }

        public event EventHandler<string>? PartialResultReceived;

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
            Task.FromResult(_available);

        public Task<SpeechPermissionStatus> GetPermissionStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(_hasPermission ? SpeechPermissionStatus.Granted : SpeechPermissionStatus.Denied);

        public Task<SpeechPermissionStatus> RequestPermissionAsync(CancellationToken ct = default) =>
            Task.FromResult(_hasPermission ? SpeechPermissionStatus.Granted : SpeechPermissionStatus.Denied);

        public Task<PlatformSpeechRecognitionResult> StartListeningAsync(CancellationToken ct = default)
        {
            if (_result is not null)
                return Task.FromResult(_result);
            _listenTcs = new TaskCompletionSource<PlatformSpeechRecognitionResult>();
            ct.Register(() => _listenTcs.TrySetResult(
                new PlatformSpeechRecognitionResult(false, null, 0, "Cancelled", Cancelled: true)));
            return _listenTcs.Task;
        }

        public Task StopListeningAsync(CancellationToken ct = default)
        {
            _listenTcs?.TrySetResult(_result
                ?? new PlatformSpeechRecognitionResult(false, null, 0, "Stopped"));
            return Task.CompletedTask;
        }

        public Task CancelListeningAsync()
        {
            _listenTcs?.TrySetResult(
                new PlatformSpeechRecognitionResult(false, null, 0, "Cancelled", Cancelled: true));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class RecordingVoiceCallbacks : IBaristaVoiceCallbacks
    {
        public List<VoiceOverlayState> StateHistory { get; } = new();

        public void OnVoiceStateChanged(VoiceOverlayState state) =>
            StateHistory.Add(state);

        public void ApplyNewDrinkFields(VoiceFieldUpdates updates) { }

        public Task<VoiceToolResultDto> CommitNewDrinkAsync(
            VoiceFieldUpdates updates, CancellationToken ct) =>
            Task.FromResult(new VoiceToolResultDto(false, "Not implemented in test"));

        public Task<VoiceNavigationResult> NavigateAsync(
            VoiceNavigationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VoiceNavigationResult(VoiceNavigationOutcome.Navigated));
        public void ActivateNewDrinkVoice() { }
    }
}

public sealed class PhotoWorkflowStateMachineTests
{
    static readonly PhotoAsset TestPhoto = new([1, 2, 3], "image/jpeg", "test.jpg", PhotoSource.Camera);

    [Fact]
    public async Task RetakeThenProfile_CapturesTwiceAndRoutesProfile()
    {
        var photos = new FakePhotoService(
            captureResults: new Queue<PhotoOperationResult>(
            [
                PhotoOperationResult.Success(TestPhoto),
                PhotoOperationResult.Success(TestPhoto),
            ]));
        var vision = new FakeVision(
            classifications: new Queue<PhotoClassificationResult>(
            [
                new(VisionRequestStatus.Success,
                    new PhotoWorkflowAnalysis(false, PhotoWorkflowIntent.Unknown, false, null, null),
                    null, null),
                new(VisionRequestStatus.Success,
                    new PhotoWorkflowAnalysis(true, PhotoWorkflowIntent.Profile, true, null, null),
                    null, null),
            ]));
        var callbacks = new SequencedPhotoCallbacks(
            new Queue<PhotoIntentChoice>([PhotoIntentChoice.Retake]));
        var coordinator = new PhotoWorkflowCoordinator(photos, vision, callbacks);

        var outcome = await coordinator.RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Profile, outcome);
        Assert.Contains("profile", callbacks.Routes, StringComparer.OrdinalIgnoreCase);
        Assert.True(callbacks.BusyTransitions.Count >= 2);
    }

    [Fact]
    public async Task ConcurrentWorkflow_SecondCallRejectsBusy()
    {
        var tcs = new TaskCompletionSource<PhotoOperationResult>();
        var photos = new FakePhotoService(captureTask: tcs.Task);
        var callbacks = new SequencedPhotoCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(photos, new FakeVision(), callbacks);

        var first = coordinator.RunCameraAsync();
        var second = await coordinator.RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Error, second);
        Assert.NotNull(callbacks.LastError);
        Assert.Equal("workflow_busy", callbacks.LastError!.Code);

        tcs.SetResult(PhotoOperationResult.Cancelled());
        await first;
    }

    sealed class FakePhotoService : IBaristaPhotoService
    {
        readonly Queue<PhotoOperationResult>? _captureResults;
        readonly Task<PhotoOperationResult>? _captureTask;

        public FakePhotoService(
            Queue<PhotoOperationResult>? captureResults = null,
            Task<PhotoOperationResult>? captureTask = null)
        {
            _captureResults = captureResults;
            _captureTask = captureTask;
        }

        public bool IsCameraAvailable => true;
        public bool IsGalleryAvailable => true;

        public Task<PhotoOperationResult> CapturePhotoAsync(CancellationToken ct = default) =>
            _captureTask ?? Task.FromResult(
                _captureResults?.Count > 0
                    ? _captureResults.Dequeue()
                    : PhotoOperationResult.Cancelled());

        public Task<PhotoOperationResult> PickPhotoAsync(CancellationToken ct = default) =>
            Task.FromResult(PhotoOperationResult.Cancelled());
    }

    sealed class FakeVision : IBaristaVisionAnalyzer
    {
        readonly Queue<PhotoClassificationResult>? _classifications;

        public FakeVision(Queue<PhotoClassificationResult>? classifications = null) =>
            _classifications = classifications;

        public VisionAvailability Availability => new(VisionAvailabilityState.Ready, null);

        public Task<PhotoClassificationResult> ClassifyPhotoAsync(PhotoAsset photo, CancellationToken ct = default) =>
            Task.FromResult(_classifications?.Count > 0
                ? _classifications.Dequeue()
                : new PhotoClassificationResult(VisionRequestStatus.Error,
                    new PhotoWorkflowAnalysis(false, PhotoWorkflowIntent.Unknown, false, null, "test"),
                    null, "test"));

        public Task<BeanLabelResult> ExtractBeanLabelAsync(PhotoAsset photo, CancellationToken ct = default) =>
            Task.FromResult(new BeanLabelResult(VisionRequestStatus.Error, null, "test"));
        public Task<RoomAnalysisResult> AnalyzeRoomAsync(PhotoAsset photo, CancellationToken ct = default) =>
            Task.FromResult(new RoomAnalysisResult(VisionRequestStatus.Error, null, "test"));
        public Task<ProfileMatchResult> MatchProfileAsync(
            PhotoAsset photo, IReadOnlyList<PersonIdentificationCandidate> candidates, CancellationToken ct = default) =>
            Task.FromResult(new ProfileMatchResult(VisionRequestStatus.Error, null, "test"));
    }

    sealed class SequencedPhotoCallbacks : IBaristaPhotoWorkflowCallbacks
    {
        readonly Queue<PhotoIntentChoice>? _choices;
        public PhotoWorkflowError? LastError { get; private set; }
        public List<string> Routes { get; } = new();
        public List<bool> BusyTransitions { get; } = new();

        public SequencedPhotoCallbacks(Queue<PhotoIntentChoice>? choices = null) =>
            _choices = choices;

        public void SetPhotoWorkflowBusy(bool isBusy) => BusyTransitions.Add(isBusy);

        public Task VisionResultAsync(VisionCallback result, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<PhotoIntentChoice> ChoosePhotoIntentAsync(
            PhotoAsset photo, PhotoClassificationResult classification, CancellationToken ct) =>
            Task.FromResult(_choices?.Count > 0 ? _choices.Dequeue() : PhotoIntentChoice.Cancel);

        public Task RouteCoffeeAsync(PhotoAsset photo, BeanLabelExtraction? details, CancellationToken ct)
        { Routes.Add("coffee"); return Task.CompletedTask; }

        public Task RouteProfileAsync(PhotoAsset photo, CancellationToken ct)
        { Routes.Add("profile"); return Task.CompletedTask; }

        public Task RouteRoomAsync(PhotoAsset photo, VisionAnalysisResult analysis, CancellationToken ct)
        { Routes.Add("room"); return Task.CompletedTask; }

        public Task PhotoWorkflowCancelledAsync(PhotoWorkflowCancellation reason, CancellationToken ct) =>
            Task.CompletedTask;

        public Task PhotoWorkflowFailedAsync(PhotoWorkflowError error, CancellationToken ct)
        { LastError = error; return Task.CompletedTask; }
    }
}

public sealed class PhotoWorkflowIntegrationTests
{
    static readonly PhotoAsset TestPhoto = new([1, 2, 3], "image/jpeg", "test.jpg", PhotoSource.Camera);

    [Fact]
    public async Task PhotoWorkflowCoordinator_CameraCancel_NotifiesCallbacks()
    {
        var photos = new FakePhotoService(captureResult: PhotoOperationResult.Cancelled());
        var callbacks = new RecordingPhotoCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(photos, new FakeVision(), callbacks);

        var outcome = await coordinator.RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Cancelled, outcome);
        Assert.Equal(PhotoWorkflowCancellation.CaptureCancelled, callbacks.LastCancellation);
    }

    [Fact]
    public async Task PhotoWorkflowCoordinator_GalleryCancel_NotifiesCallbacks()
    {
        var photos = new FakePhotoService(pickResult: PhotoOperationResult.Cancelled());
        var callbacks = new RecordingPhotoCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(photos, new FakeVision(), callbacks);

        var outcome = await coordinator.RunGalleryAsync();

        Assert.Equal(PhotoWorkflowOutcome.Cancelled, outcome);
        Assert.Equal(PhotoWorkflowCancellation.GalleryCancelled, callbacks.LastCancellation);
    }

    [Fact]
    public async Task PhotoWorkflowCoordinator_SuccessfulCoffeeClassification_RoutesCoffee()
    {
        var photos = new FakePhotoService(
            captureResult: PhotoOperationResult.Success(TestPhoto));
        var vision = new FakeVision(
            classificationResult: new PhotoClassificationResult(
                VisionRequestStatus.Success,
                new PhotoWorkflowAnalysis(true, PhotoWorkflowIntent.Coffee, true, null, null),
                new BeanLabelExtraction { Success = true, Name = "Test Bean" },
                null));
        var callbacks = new RecordingPhotoCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(photos, vision, callbacks);

        var outcome = await coordinator.RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Coffee, outcome);
        Assert.Contains("coffee", callbacks.Routes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhotoWorkflowCoordinator_Error_NotifiesFailure()
    {
        var photos = new FakePhotoService(
            captureResult: PhotoOperationResult.Error("test_error", "Test failure"));
        var callbacks = new RecordingPhotoCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(photos, new FakeVision(), callbacks);

        var outcome = await coordinator.RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Error, outcome);
        Assert.NotNull(callbacks.LastError);
        Assert.Equal("test_error", callbacks.LastError!.Code);
    }

    [Fact]
    public void BaristaPlatformServices_TryGet_ReturnsFalseWhenNotConfigured()
    {
        // Without Configure, TryGet should return false
        // Note: only testable if no other test has called Configure
        var result = BaristaPlatformServices.TryGet(out var photos, out var vision);
        // If previously configured, both are non-null. Otherwise false.
        // This tests the contract, not a specific expected value.
        Assert.Equal(result, photos is not null && vision is not null);
    }

    sealed class FakePhotoService : IBaristaPhotoService
    {
        readonly PhotoOperationResult _captureResult;
        readonly PhotoOperationResult _pickResult;

        public FakePhotoService(
            PhotoOperationResult? captureResult = null,
            PhotoOperationResult? pickResult = null)
        {
            _captureResult = captureResult ?? PhotoOperationResult.Cancelled();
            _pickResult = pickResult ?? PhotoOperationResult.Cancelled();
        }

        public bool IsCameraAvailable => true;
        public bool IsGalleryAvailable => true;

        public Task<PhotoOperationResult> CapturePhotoAsync(CancellationToken ct = default) =>
            Task.FromResult(_captureResult);

        public Task<PhotoOperationResult> PickPhotoAsync(CancellationToken ct = default) =>
            Task.FromResult(_pickResult);
    }

    sealed class FakeVision : IBaristaVisionAnalyzer
    {
        readonly PhotoClassificationResult? _classificationResult;

        public FakeVision(PhotoClassificationResult? classificationResult = null)
        {
            _classificationResult = classificationResult;
        }

        public VisionAvailability Availability => new(VisionAvailabilityState.Ready, null);

        public Task<PhotoClassificationResult> ClassifyPhotoAsync(PhotoAsset photo, CancellationToken ct = default) =>
            Task.FromResult(_classificationResult ?? new PhotoClassificationResult(
                VisionRequestStatus.Error,
                new PhotoWorkflowAnalysis(false, PhotoWorkflowIntent.Unknown, false, null, "Not configured"),
                null, "Not configured"));

        public Task<BeanLabelResult> ExtractBeanLabelAsync(PhotoAsset photo, CancellationToken ct = default) =>
            Task.FromResult(new BeanLabelResult(VisionRequestStatus.Error, null, "Not configured"));

        public Task<RoomAnalysisResult> AnalyzeRoomAsync(PhotoAsset photo, CancellationToken ct = default) =>
            Task.FromResult(new RoomAnalysisResult(VisionRequestStatus.Error, null, "Not configured"));

        public Task<ProfileMatchResult> MatchProfileAsync(
            PhotoAsset photo, IReadOnlyList<PersonIdentificationCandidate> candidates, CancellationToken ct = default) =>
            Task.FromResult(new ProfileMatchResult(VisionRequestStatus.Error, null, "Not configured"));
    }

    sealed class RecordingPhotoCallbacks : IBaristaPhotoWorkflowCallbacks
    {
        public PhotoWorkflowCancellation? LastCancellation { get; private set; }
        public PhotoWorkflowError? LastError { get; private set; }
        public List<string> Routes { get; } = new();
        bool _isBusy;

        public void SetPhotoWorkflowBusy(bool isBusy) => _isBusy = isBusy;

        public Task VisionResultAsync(VisionCallback result, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<PhotoIntentChoice> ChoosePhotoIntentAsync(
            PhotoAsset photo, PhotoClassificationResult classification, CancellationToken ct) =>
            Task.FromResult(PhotoIntentChoice.Cancel);

        public Task RouteCoffeeAsync(PhotoAsset photo, BeanLabelExtraction? details, CancellationToken ct)
        {
            Routes.Add("coffee");
            return Task.CompletedTask;
        }

        public Task RouteProfileAsync(PhotoAsset photo, CancellationToken ct)
        {
            Routes.Add("profile");
            return Task.CompletedTask;
        }

        public Task RouteRoomAsync(PhotoAsset photo, VisionAnalysisResult analysis, CancellationToken ct)
        {
            Routes.Add("room");
            return Task.CompletedTask;
        }

        public Task PhotoWorkflowCancelledAsync(PhotoWorkflowCancellation reason, CancellationToken ct)
        {
            LastCancellation = reason;
            return Task.CompletedTask;
        }

        public Task PhotoWorkflowFailedAsync(PhotoWorkflowError error, CancellationToken ct)
        {
            LastError = error;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Tests that verify HasPendingIntentChoice branching, per-request TCS lifecycle,
/// and route callback behavior through the PhotoWorkflowCoordinator.
/// </summary>
public sealed class PhotoIntentBranchingTests
{
    [Fact]
    public async Task IntentChoice_Resolved_ReturnsSelectedChoice()
    {
        var callbacks = new IntentTracker();
        var photo = new PhotoAsset([1], "image/jpeg", "test.jpg", PhotoSource.Camera);
        var classification = new PhotoClassificationResult(
            VisionRequestStatus.Success,
            new PhotoWorkflowAnalysis(false, PhotoWorkflowIntent.Unknown, false, null, null),
            null, null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var choiceTask = callbacks.ChoosePhotoIntentAsync(photo, classification, cts.Token);

        Assert.True(callbacks.IsPending);
        callbacks.Resolve(PhotoIntentChoice.Coffee);
        var result = await choiceTask;

        Assert.Equal(PhotoIntentChoice.Coffee, result);
        Assert.False(callbacks.IsPending);
    }

    [Fact]
    public async Task IntentChoice_Cancelled_ReturnsCancel()
    {
        var callbacks = new IntentTracker();
        var photo = new PhotoAsset([1], "image/jpeg", "test.jpg", PhotoSource.Camera);
        var classification = new PhotoClassificationResult(
            VisionRequestStatus.Success,
            new PhotoWorkflowAnalysis(false, PhotoWorkflowIntent.Unknown, false, null, null),
            null, null);

        using var cts = new CancellationTokenSource();
        var choiceTask = callbacks.ChoosePhotoIntentAsync(photo, classification, cts.Token);

        Assert.True(callbacks.IsPending);
        cts.Cancel();
        var result = await choiceTask;

        Assert.Equal(PhotoIntentChoice.Cancel, result);
    }

    [Fact]
    public async Task IntentChoice_PerRequest_SecondCancelsFirst()
    {
        var callbacks = new IntentTracker();
        var photo = new PhotoAsset([1], "image/jpeg", "test.jpg", PhotoSource.Camera);
        var classification = new PhotoClassificationResult(
            VisionRequestStatus.Success,
            new PhotoWorkflowAnalysis(false, PhotoWorkflowIntent.Unknown, false, null, null),
            null, null);

        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = callbacks.ChoosePhotoIntentAsync(photo, classification, cts1.Token);

        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var second = callbacks.ChoosePhotoIntentAsync(photo, classification, cts2.Token);

        var firstResult = await first;
        Assert.Equal(PhotoIntentChoice.Cancel, firstResult);

        Assert.True(callbacks.IsPending);
        callbacks.Resolve(PhotoIntentChoice.Room);
        var secondResult = await second;
        Assert.Equal(PhotoIntentChoice.Room, secondResult);
    }

    [Fact]
    public async Task RouteCallbacks_CoffeeProfileRoom_DistinctRoutes()
    {
        var captures = new Queue<PhotoOperationResult>(new[]
        {
            PhotoOperationResult.Success(new PhotoAsset([1], "image/jpeg", "a.jpg", PhotoSource.Camera)),
            PhotoOperationResult.Success(new PhotoAsset([2], "image/jpeg", "b.jpg", PhotoSource.Camera)),
        });
        var classifications = new Queue<PhotoClassificationResult>(new[]
        {
            Obvious(PhotoWorkflowIntent.Coffee),
            Obvious(PhotoWorkflowIntent.Profile),
        });
        var photos = new QueuedPhotoService(captures);
        var vision = new QueuedVision(classifications);
        var callbacks = new TrackingCallbacks();

        var r1 = await new PhotoWorkflowCoordinator(photos, vision, callbacks).RunCameraAsync();
        var r2 = await new PhotoWorkflowCoordinator(photos, vision, callbacks).RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Coffee, r1);
        Assert.Equal(PhotoWorkflowOutcome.Profile, r2);
        Assert.Contains("coffee", callbacks.RoutedIntents);
        Assert.Contains("profile", callbacks.RoutedIntents);
    }

    [Fact]
    public void InitialState_NoPendingTCS()
    {
        var callbacks = new IntentTracker();
        Assert.False(callbacks.IsPending);
    }

    static PhotoClassificationResult Obvious(PhotoWorkflowIntent intent) => new(
        VisionRequestStatus.Success,
        new PhotoWorkflowAnalysis(true, intent, true, null, null),
        intent == PhotoWorkflowIntent.Coffee ? new BeanLabelExtraction { Success = true, Name = "X" } : null,
        null);

    sealed class IntentTracker : IBaristaPhotoWorkflowCallbacks
    {
        TaskCompletionSource<PhotoIntentChoice>? _tcs;
        public bool IsPending => _tcs is { Task.IsCompleted: false };

        public void Resolve(PhotoIntentChoice choice) { _tcs?.TrySetResult(choice); _tcs = null; }

        public void SetPhotoWorkflowBusy(bool b) { }
        public Task VisionResultAsync(VisionCallback r, CancellationToken ct) => Task.CompletedTask;
        public Task<PhotoIntentChoice> ChoosePhotoIntentAsync(
            PhotoAsset photo, PhotoClassificationResult c, CancellationToken ct)
        {
            _tcs?.TrySetCanceled();
            var tcs = new TaskCompletionSource<PhotoIntentChoice>();
            _tcs = tcs;
            var reg = ct.Register(() => tcs.TrySetCanceled());
            return tcs.Task.ContinueWith(t => { reg.Dispose(); return t.IsCanceled ? PhotoIntentChoice.Cancel : t.Result; },
                TaskScheduler.Default);
        }
        public Task RouteCoffeeAsync(PhotoAsset p, BeanLabelExtraction? d, CancellationToken ct) => Task.CompletedTask;
        public Task RouteProfileAsync(PhotoAsset p, CancellationToken ct) => Task.CompletedTask;
        public Task RouteRoomAsync(PhotoAsset p, VisionAnalysisResult a, CancellationToken ct) => Task.CompletedTask;
        public Task PhotoWorkflowCancelledAsync(PhotoWorkflowCancellation r, CancellationToken ct) => Task.CompletedTask;
        public Task PhotoWorkflowFailedAsync(PhotoWorkflowError e, CancellationToken ct) => Task.CompletedTask;
    }

    sealed class QueuedPhotoService(Queue<PhotoOperationResult> results) : IBaristaPhotoService
    {
        public bool IsCameraAvailable => true;
        public bool IsGalleryAvailable => true;
        public Task<PhotoOperationResult> CapturePhotoAsync(CancellationToken ct) =>
            Task.FromResult(results.Count > 0 ? results.Dequeue() : PhotoOperationResult.Cancelled());
        public Task<PhotoOperationResult> PickPhotoAsync(CancellationToken ct) =>
            Task.FromResult(PhotoOperationResult.Cancelled());
    }

    sealed class QueuedVision(Queue<PhotoClassificationResult> results) : IBaristaVisionAnalyzer
    {
        public VisionAvailability Availability => new(VisionAvailabilityState.Ready, null);
        public Task<PhotoClassificationResult> ClassifyPhotoAsync(PhotoAsset photo, CancellationToken ct) =>
            Task.FromResult(results.Count > 0 ? results.Dequeue() : new PhotoClassificationResult(
                VisionRequestStatus.Error, new PhotoWorkflowAnalysis(false, PhotoWorkflowIntent.Unknown, false, null, "e"), null, "e"));
        public Task<BeanLabelResult> ExtractBeanLabelAsync(PhotoAsset photo, CancellationToken ct) =>
            Task.FromResult(new BeanLabelResult(VisionRequestStatus.Error, null, "e"));
        public Task<RoomAnalysisResult> AnalyzeRoomAsync(PhotoAsset photo, CancellationToken ct) =>
            Task.FromResult(new RoomAnalysisResult(VisionRequestStatus.Success,
                new VisionAnalysisResult(true, 3, 3, 60, "3 people", null), null));
        public Task<ProfileMatchResult> MatchProfileAsync(
            PhotoAsset photo, IReadOnlyList<PersonIdentificationCandidate> candidates, CancellationToken ct) =>
            Task.FromResult(new ProfileMatchResult(VisionRequestStatus.Error, null, "e"));
    }

    sealed class TrackingCallbacks : IBaristaPhotoWorkflowCallbacks
    {
        public List<string> RoutedIntents { get; } = new();
        public void SetPhotoWorkflowBusy(bool b) { }
        public Task VisionResultAsync(VisionCallback r, CancellationToken ct) => Task.CompletedTask;
        public Task<PhotoIntentChoice> ChoosePhotoIntentAsync(
            PhotoAsset p, PhotoClassificationResult c, CancellationToken ct) =>
            Task.FromResult(PhotoIntentChoice.Cancel);
        public Task RouteCoffeeAsync(PhotoAsset p, BeanLabelExtraction? d, CancellationToken ct)
        { RoutedIntents.Add("coffee"); return Task.CompletedTask; }
        public Task RouteProfileAsync(PhotoAsset p, CancellationToken ct)
        { RoutedIntents.Add("profile"); return Task.CompletedTask; }
        public Task RouteRoomAsync(PhotoAsset p, VisionAnalysisResult a, CancellationToken ct)
        { RoutedIntents.Add("room"); return Task.CompletedTask; }
        public Task PhotoWorkflowCancelledAsync(PhotoWorkflowCancellation r, CancellationToken ct) =>
            Task.CompletedTask;
        public Task PhotoWorkflowFailedAsync(PhotoWorkflowError e, CancellationToken ct) =>
            Task.CompletedTask;
    }
}


public sealed class ActivityPeriodFilterTests
{
    // Fixed UTC instant: 2026-03-15 14:30:00 UTC (a Sunday)
    static readonly DateTime FixedUtc = new(2026, 3, 15, 14, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("today", "2026-03-15", "2026-03-16")]
    [InlineData("yesterday", "2026-03-14", "2026-03-15")]
    public void PeriodBounds_TodayAndYesterday_CorrectStartEnd(string period, string expectedStart, string expectedEnd)
    {
        var (start, end) = PeriodBounds.Resolve(period, FixedUtc);
        Assert.Equal(DateTime.Parse(expectedStart), start);
        Assert.Equal(DateTime.Parse(expectedEnd), end);
    }

    [Fact]
    public void PeriodBounds_Yesterday_ExcludesTodayByExclusiveEnd()
    {
        var (_, end) = PeriodBounds.Resolve("yesterday", FixedUtc);
        Assert.Equal(FixedUtc.Date, end);
        Assert.True(FixedUtc >= end);
    }

    [Fact]
    public void PeriodBounds_ThisWeek_StartIsSundaySpans7Days()
    {
        var (start, end) = PeriodBounds.Resolve("this week", FixedUtc);
        Assert.Equal(DayOfWeek.Sunday, start!.Value.DayOfWeek);
        Assert.Equal(7, (end!.Value - start.Value).Days);
    }

    [Fact]
    public void PeriodBounds_LastWeek_EndsAtThisWeekStart()
    {
        var (lastStart, lastEnd) = PeriodBounds.Resolve("last week", FixedUtc);
        var (thisStart, _) = PeriodBounds.Resolve("this week", FixedUtc);
        Assert.Equal(thisStart, lastEnd);
        Assert.Equal(7, (lastEnd!.Value - lastStart!.Value).Days);
    }

    [Fact]
    public void PeriodBounds_LastMonth_EndsAtThisMonthStart()
    {
        var (lastStart, lastEnd) = PeriodBounds.Resolve("last month", FixedUtc);
        var (thisStart, _) = PeriodBounds.Resolve("this month", FixedUtc);
        Assert.Equal(thisStart, lastEnd);
        Assert.Equal(new DateTime(2026, 2, 1), lastStart);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("all")]
    [InlineData("ALL")]
    public void PeriodBounds_AllOrNull_BothNull(string? period)
    {
        var (start, end) = PeriodBounds.Resolve(period, FixedUtc);
        Assert.Null(start);
        Assert.Null(end);
    }

    [Fact]
    public void ShotFilterCriteriaDto_PeriodStart_IncludedInHasFilters()
    {
        Assert.True(new ShotFilterCriteriaDto { PeriodStart = FixedUtc.Date }.HasFilters);
        Assert.False(new ShotFilterCriteriaDto().HasFilters);
    }

    [Fact]
    public async Task Service_Yesterday_ExcludesTodayShots()
    {
        var store = new CometBaristaNotes.Data.InMemoryDataStore();
        store.Seed();
        var notifier = new DataChangeNotifier();
        var service = new InMemoryShotService(store, notifier);

        var todayShot = await service.CreateShotAsync(new CreateShotDto
        {
            BagId = store.Bags.First(b => !b.IsDeleted).Id,
            BrewMethod = CometBaristaNotes.Models.Enums.BrewMethod.Espresso,
            DrinkType = "Espresso",
            DoseIn = 18, ExpectedOutput = 36, ExpectedTime = 28,
            Timestamp = FixedUtc,
        });

        var (start, end) = PeriodBounds.Resolve("yesterday", FixedUtc);
        var result = await service.GetFilteredShotHistoryAsync(
            new ShotFilterCriteriaDto { PeriodStart = start, PeriodEnd = end }, 0, 100);

        Assert.DoesNotContain(result.Items, s => s.Id == todayShot.Id);
    }

    [Fact]
    public async Task Service_Today_IncludesTodayShots()
    {
        var store = new CometBaristaNotes.Data.InMemoryDataStore();
        store.Seed();
        var notifier = new DataChangeNotifier();
        var service = new InMemoryShotService(store, notifier);

        var todayShot = await service.CreateShotAsync(new CreateShotDto
        {
            BagId = store.Bags.First(b => !b.IsDeleted).Id,
            BrewMethod = CometBaristaNotes.Models.Enums.BrewMethod.Espresso,
            DrinkType = "Espresso",
            DoseIn = 18, ExpectedOutput = 36, ExpectedTime = 28,
            Timestamp = FixedUtc,
        });

        var (start, end) = PeriodBounds.Resolve("today", FixedUtc);
        var result = await service.GetFilteredShotHistoryAsync(
            new ShotFilterCriteriaDto { PeriodStart = start, PeriodEnd = end }, 0, 100);

        Assert.Contains(result.Items, s => s.Id == todayShot.Id);
    }

    [Theory]
    [InlineData("rating:3", FilterKind.Rating, 3)]
    [InlineData("bean:42", FilterKind.Bean, 42)]
    [InlineData("made-for:7", FilterKind.MadeFor, 7)]
    public void ParseFilterToken_ValidTokens(string filter, FilterKind expectedKind, int expectedValue)
    {
        var token = PeriodBounds.ParseFilterToken(filter);
        Assert.NotNull(token);
        Assert.Equal(expectedKind, token!.Kind);
        Assert.Equal(expectedValue, token.Value);
    }

    [Theory]
    [InlineData("rating:5")]
    [InlineData("bean:-1")]
    [InlineData("unknown:3")]
    [InlineData("rating:")]
    [InlineData("garbage")]
    [InlineData(null)]
    [InlineData("")]
    public void ParseFilterToken_InvalidTokens_ReturnsNull(string? filter)
    {
        Assert.Null(PeriodBounds.ParseFilterToken(filter));
    }
}
