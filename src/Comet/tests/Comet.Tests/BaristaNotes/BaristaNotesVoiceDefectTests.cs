#nullable enable
// Deterministic contracts for six confirmed voice/speech defects.
// Namespace avoids Comet.Tests.BaristaNotes to dodge ambiguous interface clones.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Recipes;
using CometBaristaNotes.Services.Voice;
using Xunit;

namespace Comet.Tests.VoiceDefects;

// ─── Helpers (non-file-local so test classes can reference them) ──

internal sealed class StubPlatform : IPlatformSpeechRecognizer
{
    public bool Available { get; set; } = true;
    public SpeechPermissionStatus PermissionStatus { get; set; } = SpeechPermissionStatus.Granted;
    public PlatformSpeechRecognitionResult? NextResult { get; set; }
    public TimeSpan ListeningDelay { get; set; } = TimeSpan.Zero;
    public bool Disposed { get; private set; }
    public bool CancelCalled { get; private set; }
    public bool ReceivedCancelableToken { get; private set; }
    public event EventHandler<string>? PartialResultReceived;
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(Available);
    public Task<SpeechPermissionStatus> GetPermissionStatusAsync(CancellationToken ct = default) => Task.FromResult(PermissionStatus);
    public Task<SpeechPermissionStatus> RequestPermissionAsync(CancellationToken ct = default) => Task.FromResult(PermissionStatus);
    public async Task<PlatformSpeechRecognitionResult> StartListeningAsync(CancellationToken ct = default)
    {
        ReceivedCancelableToken = ct.CanBeCanceled;
        if (ListeningDelay > TimeSpan.Zero) await Task.Delay(ListeningDelay, ct);
        return NextResult ?? new(false, null, 0, "No result configured");
    }
    public Task StopListeningAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CancelListeningAsync() { CancelCalled = true; return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

internal sealed class StubSpeech : CometBaristaNotes.Services.ISpeechRecognitionService
{
    readonly StubPlatform _p;
    public StubSpeech(StubPlatform p) => _p = p;
    public SpeechRecognitionState State => SpeechRecognitionState.Idle;
    public event EventHandler<SpeechRecognitionState>? StateChanged;
    public event EventHandler<string>? PartialResultReceived;
    public Task<bool> IsAvailableAsync() => Task.FromResult(_p.Available);
    public Task<bool> RequestPermissionsAsync() => Task.FromResult(_p.PermissionStatus == SpeechPermissionStatus.Granted);
    public async Task<SpeechRecognitionResultDto> StartListeningAsync(CancellationToken ct = default)
    {
        var r = await _p.StartListeningAsync(ct);
        return new(r.Success, r.Transcript, r.Confidence, r.ErrorMessage);
    }
    public Task StopListeningAsync() => Task.CompletedTask;
}

internal sealed class StubCommands : IBaristaVoiceCommandService
{
    public Func<string, CancellationToken, Task<VoiceToolResultDto>>? Process { get; init; }
    public ParsedVoiceCommand Interpret(string t) => new() { Intent = CommandIntent.LogShot };
    public Task<VoiceToolResultDto> ProcessAsync(string t, CancellationToken ct = default)
        => Process?.Invoke(t, ct) ?? Task.FromResult(new VoiceToolResultDto(true, "Saved."));
}

internal sealed class SpyCb : IBaristaVoiceCallbacks
{
    public ConcurrentQueue<VoiceOverlayState> States { get; } = new();
    public List<VoiceNavigationRequest> Navigations { get; } = new();
    public bool ActivateCalled { get; private set; }
    public void OnVoiceStateChanged(VoiceOverlayState s) => States.Enqueue(s);
    public void ApplyNewDrinkFields(VoiceFieldUpdates u) { }
    public Task<VoiceToolResultDto> CommitNewDrinkAsync(VoiceFieldUpdates u, CancellationToken ct)
        => Task.FromResult(new VoiceToolResultDto(true, "Saved."));
    public Task<VoiceNavigationResult> NavigateAsync(
        VoiceNavigationRequest request,
        CancellationToken cancellationToken)
    {
        Navigations.Add(request);
        return Task.FromResult(new VoiceNavigationResult(VoiceNavigationOutcome.Navigated));
    }
    public void ActivateNewDrinkVoice() => ActivateCalled = true;
}

internal sealed class StubShotService : CometBaristaNotes.Services.IShotService
{
    readonly List<ShotRecordDto> _shots;

    public StubShotService(IEnumerable<ShotRecordDto>? shots = null) =>
        _shots = shots?.ToList() ?? new();

    public Task<PagedResult<ShotRecordDto>> GetShotHistoryAsync(int pageIndex, int pageSize) =>
        Task.FromResult(new PagedResult<ShotRecordDto>
        {
            Items = _shots.Skip(pageIndex * pageSize).Take(pageSize).ToList(),
            TotalCount = _shots.Count,
            PageIndex = pageIndex,
            PageSize = pageSize,
        });

    public Task<ShotRecordDto?> GetMostRecentShotAsync() =>
        Task.FromResult<ShotRecordDto?>(_shots.OrderByDescending(x => x.Timestamp).FirstOrDefault());
    public Task<ShotRecordDto> CreateShotAsync(CreateShotDto dto) => throw new NotSupportedException();
    public Task<ShotRecordDto> UpdateShotAsync(int id, UpdateShotDto dto) => throw new NotSupportedException();
    public Task DeleteShotAsync(int id) => throw new NotSupportedException();
    public Task<PagedResult<ShotRecordDto>> GetShotHistoryByUserAsync(int userProfileId, int pageIndex, int pageSize) => throw new NotSupportedException();
    public Task<PagedResult<ShotRecordDto>> GetShotHistoryByBeanAsync(int beanId, int pageIndex, int pageSize) => throw new NotSupportedException();
    public Task<PagedResult<ShotRecordDto>> GetShotHistoryByEquipmentAsync(int equipmentId, int pageIndex, int pageSize) => throw new NotSupportedException();
    public Task<PagedResult<ShotRecordDto>> GetFilteredShotHistoryAsync(ShotFilterCriteriaDto? criteria, int pageIndex, int pageSize) => throw new NotSupportedException();
    public Task<ShotRecordDto?> GetShotByIdAsync(int id) => throw new NotSupportedException();
    public Task<ShotRecordDto?> GetBestRatedShotByBeanAsync(int beanId) => throw new NotSupportedException();
    public Task<ShotRecordDto?> GetBestRatedShotByBagAsync(int bagId) => throw new NotSupportedException();
    public Task<List<BeanFilterOptionDto>> GetBeansWithShotsAsync() => throw new NotSupportedException();
    public Task<List<UserProfileDto>> GetPeopleWithShotsAsync() => throw new NotSupportedException();
    public Task<AIAdviceRequestDto?> GetShotContextForAIAsync(int shotId) => throw new NotSupportedException();
    public Task<int?> GetMostRecentBeanIdAsync() => throw new NotSupportedException();
    public Task<bool> BeanHasHistoryAsync(int beanId) => throw new NotSupportedException();
    public Task<BeanRecommendationContextDto?> GetBeanRecommendationContextAsync(int beanId) => throw new NotSupportedException();

    public List<ShotRecord> GetAllShots() => throw new NotSupportedException();
    public ShotRecord? GetShot(int id) => throw new NotSupportedException();
    public ShotRecord CreateShot(ShotRecord shot) => throw new NotSupportedException();
    public ShotRecord UpdateShot(ShotRecord shot) => throw new NotSupportedException();
    public void DeleteShot(int id) => throw new NotSupportedException();
    public List<ShotRecord> GetShotsByBean(int beanId) => throw new NotSupportedException();
    public List<ShotRecord> GetShotsForBag(int bagId) => throw new NotSupportedException();
    public List<ShotRecord> GetFilteredShots(ShotFilterCriteriaDto criteria) => throw new NotSupportedException();
    public List<(int Id, string Name)> GetBeansWithShots() => throw new NotSupportedException();
    public List<(int Id, string Name)> GetPeopleWithShots() => throw new NotSupportedException();
}

internal sealed class StubBeanService : CometBaristaNotes.Services.IBeanService
{
    public Task<List<BeanDto>> GetAllActiveBeansAsync() => throw new NotSupportedException();
    public Task<BeanDto?> GetBeanByIdAsync(int id) => throw new NotSupportedException();
    public Task<BeanDto?> GetBeanWithRatingsAsync(int id) => throw new NotSupportedException();
    public Task<OperationResult<BeanDto>> CreateBeanAsync(CreateBeanDto dto) => throw new NotSupportedException();
    public Task<BeanDto> UpdateBeanAsync(int id, UpdateBeanDto dto) => throw new NotSupportedException();
    public Task ArchiveBeanAsync(int id) => throw new NotSupportedException();
    public Task DeleteBeanAsync(int id) => throw new NotSupportedException();
    public Task<IReadOnlyList<BeanDto>> GetRecentBeansAsync(int limit = 6, int withinDays = 90) => throw new NotSupportedException();
    public Task<BeanDto?> FuzzyFindByNameRoasterAsync(string name, string? roaster) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetDistinctRoastersAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetDistinctOriginsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RecipeSourcingResult> RefreshRecipesAsync(int beanId, CancellationToken ct = default) => throw new NotSupportedException();

    public List<Bean> GetAllBeans() => throw new NotSupportedException();
    public Bean? GetBean(int id) => throw new NotSupportedException();
    public Bean CreateBean(Bean bean) => throw new NotSupportedException();
    public Bean UpdateBean(Bean bean) => throw new NotSupportedException();
    public void ArchiveBean(int id) => throw new NotSupportedException();
}

internal sealed class StubBagService : CometBaristaNotes.Services.IBagService
{
    public Task<OperationResult<Bag>> CreateBagAsync(Bag bag) => throw new NotSupportedException();
    public Task<OperationResult<BagSummaryDto>> CreateNewBagForBeanAsync(int beanId, DateTime roastDate, string? notes = null) => throw new NotSupportedException();
    public Task<Bag?> GetBagByIdAsync(int id) => throw new NotSupportedException();
    public Task<List<Bag>> GetBagsForBeanAsync(int beanId, bool includeCompleted = true) => throw new NotSupportedException();
    public Task<List<BagSummaryDto>> GetActiveBagsForShotLoggingAsync() => throw new NotSupportedException();
    public Task<List<BagSummaryDto>> GetBagSummariesForBeanAsync(int beanId, bool includeCompleted = true) => throw new NotSupportedException();
    public Task<Bag?> GetMostRecentActiveBagForBeanAsync(int beanId) => throw new NotSupportedException();
    public Task<OperationResult<Bag>> UpdateBagAsync(Bag bag) => throw new NotSupportedException();
    public Task MarkBagCompleteAsync(int id) => throw new NotSupportedException();
    public Task ReactivateBagAsync(int id) => throw new NotSupportedException();
    public Task DeleteBagAsync(int id) => throw new NotSupportedException();

    public List<Bag> GetAllBags() => throw new NotSupportedException();
    public List<Bag> GetBagsForBean(int beanId) => throw new NotSupportedException();
    public Bag? GetBag(int id) => throw new NotSupportedException();
    public Bag CreateBag(Bag bag) => throw new NotSupportedException();
    public Bag UpdateBag(Bag bag) => throw new NotSupportedException();
    public void MarkComplete(int id) => throw new NotSupportedException();
    public void ArchiveBag(int id) => throw new NotSupportedException();
    public void ReactivateBag(int id) => throw new NotSupportedException();
}

internal sealed class StubEquipmentService : CometBaristaNotes.Services.IEquipmentService
{
    public Task<List<EquipmentDto>> GetAllActiveEquipmentAsync() => throw new NotSupportedException();
    public Task<List<EquipmentDto>> GetEquipmentByTypeAsync(EquipmentType type) => throw new NotSupportedException();
    public Task<EquipmentDto?> GetEquipmentByIdAsync(int id) => throw new NotSupportedException();
    public Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentDto dto) => throw new NotSupportedException();
    public Task<EquipmentDto> UpdateEquipmentAsync(int id, UpdateEquipmentDto dto) => throw new NotSupportedException();
    public Task ArchiveEquipmentAsync(int id) => throw new NotSupportedException();
    public Task DeleteEquipmentAsync(int id) => throw new NotSupportedException();

    public List<Equipment> GetAllEquipment() => throw new NotSupportedException();
    public List<Equipment> GetByType(EquipmentType type) => throw new NotSupportedException();
    public Equipment? GetEquipment(int id) => throw new NotSupportedException();
    public Equipment CreateEquipment(Equipment equipment) => throw new NotSupportedException();
    public Equipment UpdateEquipment(Equipment equipment) => throw new NotSupportedException();
    public void ArchiveEquipment(int id) => throw new NotSupportedException();
}

internal sealed class StubUserProfileService : CometBaristaNotes.Services.IUserProfileService
{
    public Task<List<UserProfileDto>> GetAllProfilesAsync() => throw new NotSupportedException();
    public Task<UserProfileDto?> GetProfileByIdAsync(int id) => throw new NotSupportedException();
    public Task<UserProfileDto> CreateProfileAsync(CreateUserProfileDto dto) => throw new NotSupportedException();
    public Task<UserProfileDto> UpdateProfileAsync(int id, UpdateUserProfileDto dto) => throw new NotSupportedException();
    public Task DeleteProfileAsync(int id) => throw new NotSupportedException();
    public Task<ProfileImageUpdateResult> UpdateProfileImageAsync(int profileId, Stream imageStream) => throw new NotSupportedException();
    public Task<bool> RemoveProfileImageAsync(int profileId) => throw new NotSupportedException();
    public Task<string?> GetProfileImagePathAsync(int profileId) => throw new NotSupportedException();

    public List<UserProfile> GetAllProfiles() => throw new NotSupportedException();
    public UserProfile? GetProfile(int id) => throw new NotSupportedException();
    public UserProfile CreateProfile(UserProfile profile) => throw new NotSupportedException();
    public UserProfile UpdateProfile(UserProfile profile) => throw new NotSupportedException();
    public void DeleteProfile(int id) => throw new NotSupportedException();
}

internal sealed class TrackingStore : CometBaristaNotes.Data.InMemoryDataStore
{
    public int DisposeCount { get; private set; }

    public override void Dispose() => DisposeCount++;
}

[CollectionDefinition("Barista voice integration", DisableParallelization = true)]
public sealed class VoiceIntegrationCollection
{
    public const string Name = "Barista voice integration";
}

public class BaristaServicesLifetimeTests
{
    [Fact]
    public void Dispose_ClosesOwnedStoreOnceAndClearsMatchingLocator()
    {
        var store = new TrackingStore();
        var services = new CometSamples.BaristaNotes.BaristaServices(store);

        services.Dispose();
        services.Dispose();

        Assert.Equal(1, store.DisposeCount);
        Assert.False(BaristaServiceLocator.IsInitialized);
    }

    [Fact]
    public void Dispose_OlderServicesCannotClearNewerAppLocator()
    {
        var oldStore = new TrackingStore();
        var oldServices = new CometSamples.BaristaNotes.BaristaServices(oldStore);
        var currentStore = new TrackingStore();
        var currentServices = new CometSamples.BaristaNotes.BaristaServices(currentStore);

        oldServices.Dispose();

        Assert.Equal(1, oldStore.DisposeCount);
        Assert.True(BaristaServiceLocator.IsInitialized);

        currentServices.Dispose();
        Assert.Equal(1, currentStore.DisposeCount);
        Assert.False(BaristaServiceLocator.IsInitialized);
    }
}

internal static class VoiceTestWait
{
    public static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}

// ─── Defect 1: Voice integration lifecycle ────────────────────────

[Collection(VoiceIntegrationCollection.Name)]
public class VoiceIntegrationLifecycleTests : IAsyncLifetime
{
    static PushToTalkVoiceService Make(StubPlatform p, SpyCb cb)
        => new(new StubSpeech(p), new StubCommands(), cb);

    public Task InitializeAsync() => BaristaVoiceIntegration.ShutdownAsync();
    public Task DisposeAsync() => BaristaVoiceIntegration.ShutdownAsync();

    [Fact]
    public async Task ProductionBind_Initialize_UsesConfiguredPlatform()
    {
        var platform = new StubPlatform { Available = true };
        var callbacks = new SpyCb();
        await BaristaVoiceIntegration.ConfigurePlatformAsync(platform);

        var session = BaristaVoiceIntegration.Bind(
            callbacks,
            new StubShotService(),
            new StubBeanService(),
            new StubBagService(),
            new StubEquipmentService(),
            new StubUserProfileService());
        await session.InitializeAsync();

        Assert.Same(session, BaristaVoiceIntegration.Session);
        Assert.Equal(VoiceSessionStatus.Ready, BaristaVoiceIntegration.State.Status);
        Assert.Contains(callbacks.States, state => state.Status == VoiceSessionStatus.Ready);
    }

    [Fact]
    public void SynchronousConfigure_WhenPlatformAlreadyConfigured_RequiresAsyncReplacement()
    {
        var first = new StubPlatform { Available = true };
        var replacement = new StubPlatform { Available = true };
        BaristaVoiceIntegration.ConfigurePlatform(first);

        var error = Assert.Throws<InvalidOperationException>(
            () => BaristaVoiceIntegration.ConfigurePlatform(replacement));

        Assert.Contains("ConfigurePlatformAsync", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveRecognition_PageOrBackgroundDeactivation_Cancels(bool background)
    {
        var platform = new StubPlatform
        {
            Available = true,
            ListeningDelay = TimeSpan.FromSeconds(30),
        };
        await BaristaVoiceIntegration.ConfigurePlatformAsync(platform);
        var session = BaristaVoiceIntegration.Bind(
            new SpyCb(),
            new StubShotService(),
            new StubBeanService(),
            new StubBagService(),
            new StubEquipmentService(),
            new StubUserProfileService());
        await session.InitializeAsync();
        await session.StartAsync();

        if (background)
            await BaristaVoiceIntegration.OnAppBackgroundedAsync();
        else
            await BaristaVoiceIntegration.OnPageDeactivatedAsync();

        Assert.Equal(VoiceSessionStatus.Cancelled, session.CurrentState.Status);
        Assert.True(platform.CancelCalled);
    }

    [Fact]
    public async Task Init_Ready_WhenAvailable()
    {
        var p = new StubPlatform { Available = true };
        var cb = new SpyCb();
        var s = Make(p, cb);
        await s.InitializeAsync();
        Assert.Equal(VoiceSessionStatus.Ready, s.CurrentState.Status);
    }

    [Fact]
    public async Task Init_Unavailable_WhenNotAvailable()
    {
        var p = new StubPlatform { Available = false };
        var s = Make(p, new SpyCb());
        await s.InitializeAsync();
        Assert.Equal(VoiceSessionStatus.Unavailable, s.CurrentState.Status);
    }

    [Fact]
    public async Task PushRelease_TerminalState()
    {
        var p = new StubPlatform { Available = true, NextResult = new(true, "dose 18 yield 36 time 28", 0.95, null) };
        var cb = new SpyCb();
        var s = Make(p, cb);
        await s.InitializeAsync();
        await s.HandlePushToTalkAsync(PushToTalkPhase.Started);
        Assert.Contains(cb.States, x => x.Status == VoiceSessionStatus.Listening);
        await s.HandlePushToTalkAsync(PushToTalkPhase.Completed);
        Assert.True(cb.States.Last().Status is VoiceSessionStatus.Completed or VoiceSessionStatus.Error);
    }

    [Fact]
    public async Task Cancel_ProducesCancelledState()
    {
        var p = new StubPlatform { Available = true, NextResult = new(false, null, 0, "Cancelled", Cancelled: true) };
        var cb = new SpyCb();
        var s = Make(p, cb);
        await s.InitializeAsync();
        await s.HandlePushToTalkAsync(PushToTalkPhase.Started);
        await s.HandlePushToTalkAsync(PushToTalkPhase.Cancelled);
        Assert.Contains(cb.States, x => x.Status == VoiceSessionStatus.Cancelled);
    }

    [Fact]
    public async Task MultipleTransitions()
    {
        var p = new StubPlatform { Available = true, NextResult = new(true, "dose 18 yield 36 time 28", 0.9, null) };
        var cb = new SpyCb();
        var s = Make(p, cb);
        await s.InitializeAsync();
        cb.States.Clear();
        await s.HandlePushToTalkAsync(PushToTalkPhase.Started);
        await s.HandlePushToTalkAsync(PushToTalkPhase.Completed);
        Assert.True(cb.States.Count >= 3, $"Expected ≥3, got {cb.States.Count}");
    }

    [Fact]
    public void OverlayState_TranscriptAndResponse()
    {
        var s = new VoiceOverlayState(VoiceSessionStatus.Completed,
            SpeechRecognitionState.Idle, CommandStatus.Completed, "Done",
            "dose 18", Response: "Saved.");
        Assert.Equal("dose 18", s.Transcript);
        Assert.Equal("Saved.", s.Response);
    }

    [Fact]
    public void IsReady_Semantics()
    {
        Assert.True(new VoiceOverlayState(VoiceSessionStatus.Ready, SpeechRecognitionState.Idle, CommandStatus.Completed, "R").IsReady);
        Assert.True(new VoiceOverlayState(VoiceSessionStatus.Completed, SpeechRecognitionState.Idle, CommandStatus.Completed, "D").IsReady);
        Assert.True(new VoiceOverlayState(VoiceSessionStatus.Cancelled, SpeechRecognitionState.Idle, CommandStatus.Cancelled, "C").IsReady);
        Assert.False(new VoiceOverlayState(VoiceSessionStatus.Listening, SpeechRecognitionState.Listening, CommandStatus.Listening, "L").IsReady);
    }
}

// ─── Defect 2: Activation / mount handshake ──────────────────────

[Collection(VoiceIntegrationCollection.Name)]
public class NewDrinkActivationTests
{
    public NewDrinkActivationTests() =>
        BaristaVoiceIntegration.ShutdownAsync().GetAwaiter().GetResult();

    [Fact]
    public void NotifyWithoutRequest_False() =>
        Assert.False(BaristaVoiceIntegration.NotifyNewDrinkMounted());

    [Fact]
    public void RequestThenNotify_True()
    {
        BaristaVoiceIntegration.RequestNewDrinkActivation();
        Assert.True(BaristaVoiceIntegration.NotifyNewDrinkMounted());
    }

    [Fact]
    public void ClearsAfterFirst()
    {
        BaristaVoiceIntegration.RequestNewDrinkActivation();
        BaristaVoiceIntegration.NotifyNewDrinkMounted();
        Assert.False(BaristaVoiceIntegration.NotifyNewDrinkMounted());
    }

    [Fact]
    public void MultipleRequests_OnlyFireOnce()
    {
        BaristaVoiceIntegration.RequestNewDrinkActivation();
        BaristaVoiceIntegration.RequestNewDrinkActivation();
        Assert.True(BaristaVoiceIntegration.NotifyNewDrinkMounted());
        Assert.False(BaristaVoiceIntegration.NotifyNewDrinkMounted());
    }

    [Theory]
    [InlineData("Activity")]
    [InlineData("Settings")]
    public async Task ActivityOrSettingsRequest_IsDeferredUntilNewDrinkMount(string source)
    {
        var platform = new StubPlatform();
        var callbacks = new SpyCb();
        await BaristaVoiceIntegration.ConfigurePlatformAsync(platform);
        BaristaVoiceIntegration.Bind(
            callbacks,
            new StubShotService(),
            new StubBeanService(),
            new StubBagService(),
            new StubEquipmentService(),
            new StubUserProfileService());

        BaristaVoiceIntegration.RequestNewDrinkActivation();
        Assert.False(callbacks.ActivateCalled);

        Assert.True(BaristaVoiceIntegration.NotifyNewDrinkMounted());
        Assert.True(callbacks.ActivateCalled);
        Assert.False(string.IsNullOrEmpty(source));
    }
}

// ─── Defect 3: 60-second listening bound + cancellation hook ─────

public class ListeningTimeoutTests : IAsyncLifetime
{
    readonly StubPlatform _p = new() { Available = true };
    NativeSpeechRecognitionService _speech = null!;
    public Task InitializeAsync() { _speech = new(_p, TimeSpan.FromMilliseconds(200)); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _speech.DisposeAsync();

    [Fact]
    public async Task Timeout_ReturnsError()
    {
        _p.ListeningDelay = TimeSpan.FromSeconds(5);
        _p.NextResult = new(true, "late", 0.8, null);
        var r = await _speech.StartListeningAsync();
        Assert.False(r.Success);
        Assert.Contains("timed out", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SpeechRecognitionState.Error, _speech.State);
    }

    [Fact]
    public async Task UserCancel_ReturnsBeforeTimeout()
    {
        _p.ListeningDelay = TimeSpan.FromSeconds(30);
        _p.NextResult = new(true, "x", 0.5, null);
        using var cts = new CancellationTokenSource(50);
        var r = await _speech.StartListeningAsync(cts.Token);
        Assert.False(r.Success);
        Assert.DoesNotContain("timed out", r.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SpeechRecognitionState.Idle, _speech.State);
        Assert.True(_p.ReceivedCancelableToken);
        Assert.True(_p.CancelCalled);
    }

    [Fact]
    public async Task DefaultTimeout_IsSixtySeconds()
    {
        var speech = new NativeSpeechRecognitionService(_p);
        var field = typeof(NativeSpeechRecognitionService).GetField(
            "_maximumListeningDuration",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal(TimeSpan.FromSeconds(60), field.GetValue(speech));
        await speech.DisposeAsync();
    }

    [Fact]
    public async Task Timeout_ListeningThenError()
    {
        _p.ListeningDelay = TimeSpan.FromSeconds(5);
        _p.NextResult = new(true, "late", 0.8, null);
        var states = new List<SpeechRecognitionState>();
        _speech.StateChanged += (_, st) => states.Add(st);
        await _speech.StartListeningAsync();
        Assert.Contains(SpeechRecognitionState.Listening, states);
        Assert.Contains(SpeechRecognitionState.Error, states);
        Assert.True(_p.ReceivedCancelableToken);
        Assert.True(_p.CancelCalled);
    }
}

// ─── Defect 4: Disposal cascades ─────────────────────────────────

[Collection(VoiceIntegrationCollection.Name)]
public class DisposalCascadeTests
{
    [Fact]
    public async Task NativeSpeech_DisposesPlatform()
    {
        var p = new StubPlatform { Available = true };
        await new NativeSpeechRecognitionService(p).DisposeAsync();
        Assert.True(p.Disposed);
    }

    [Fact]
    public async Task DoubleDispose_NoThrow()
    {
        var p = new StubPlatform();
        var s = new NativeSpeechRecognitionService(p);
        await s.DisposeAsync();
        await s.DisposeAsync();
    }

    [Fact]
    public async Task PushToTalk_CascadesThroughSpeech()
    {
        var p = new StubPlatform { Available = true };
        var session = new PushToTalkVoiceService(new NativeSpeechRecognitionService(p), new StubCommands(), new SpyCb());
        await session.DisposeAsync();
        Assert.True(p.Disposed);
    }

    [Fact]
    public async Task PostDispose_Throws()
    {
        var p = new StubPlatform { Available = true };
        var session = new PushToTalkVoiceService(new NativeSpeechRecognitionService(p), new StubCommands(), new SpyCb());
        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.StartAsync());
    }

    [Fact]
    public async Task Shutdown_DisposesPlatform()
    {
        await BaristaVoiceIntegration.ShutdownAsync();
        var p = new StubPlatform { Available = true };
        await BaristaVoiceIntegration.ConfigurePlatformAsync(p);
        await BaristaVoiceIntegration.ShutdownAsync();
        Assert.True(p.Disposed);
    }

    [Fact]
    public async Task Unbind_DisposesAppSessionButKeepsHostPlatformConfigured()
    {
        await BaristaVoiceIntegration.ShutdownAsync();
        var platform = new StubPlatform { Available = true };
        var callbacks = new SpyCb();
        await BaristaVoiceIntegration.ConfigurePlatformAsync(platform);
        BaristaVoiceIntegration.Bind(
            callbacks,
            new StubShotService(),
            new StubBeanService(),
            new StubBagService(),
            new StubEquipmentService(),
            new StubUserProfileService());

        await BaristaVoiceIntegration.UnbindAsync(callbacks);

        Assert.Null(BaristaVoiceIntegration.Session);
        Assert.False(platform.Disposed);

        await BaristaVoiceIntegration.ShutdownAsync();
        Assert.True(platform.Disposed);
    }
}

// ─── Defect 5: Out-of-Int32 grind → error state ─────────────────

public class GrindOverflowTests
{
    readonly VoiceCommandParser _parser = new();

    [Fact] public void AboveMax_Error() { var c = _parser.Parse("Set grind to 3000000000 microns"); Assert.NotNull(c.ErrorMessage); Assert.Null(c.FieldUpdates.GrindMicrons); }
    [Fact] public void BelowMin_Error() { var c = _parser.Parse("Set grind to -3000000000 microns"); Assert.NotNull(c.ErrorMessage); Assert.Null(c.FieldUpdates.GrindMicrons); }
    [Fact] public void Fraction_Error() { var c = _parser.Parse("Set grind to 270.5 microns"); Assert.NotNull(c.ErrorMessage); Assert.Contains("whole number", c.ErrorMessage!); }
    [Fact] public void Valid_NoError() { var c = _parser.Parse("Set grind to 270 microns"); Assert.Null(c.ErrorMessage); Assert.Equal(270, c.FieldUpdates.GrindMicrons); }
    [Fact] public void Zero_Valid() { var c = _parser.Parse("Set grind setting to 0 microns"); Assert.Null(c.ErrorMessage); Assert.Equal(0, c.FieldUpdates.GrindMicrons); }
    [Fact] public void AtMax_Valid() { var c = _parser.Parse($"Set grind to {int.MaxValue} microns"); Assert.Null(c.ErrorMessage); Assert.Equal(int.MaxValue, c.FieldUpdates.GrindMicrons); }

    [Theory]
    [InlineData("99999999999999")]
    [InlineData("2147483648")]
    public void JustOver_Error(string v) { var c = _parser.Parse($"Set grind setting to {v} microns"); Assert.NotNull(c.ErrorMessage); Assert.Null(c.FieldUpdates.GrindMicrons); }

    [Fact]
    public async Task OverflowRecognition_CannotThrowOrRemainProcessing()
    {
        var platform = new StubPlatform
        {
            NextResult = new(true, "set grind to 3000000000 microns", 0.95, null),
        };
        var callbacks = new SpyCb();
        var commands = new BaristaVoiceCommandService(
            _parser,
            callbacks,
            new StubShotService(),
            new StubBeanService(),
            new StubBagService(),
            new StubEquipmentService(),
            new StubUserProfileService());
        await using var session = new PushToTalkVoiceService(
            new StubSpeech(platform),
            commands,
            callbacks);

        await session.InitializeAsync();
        await session.StartAsync();
        await VoiceTestWait.UntilAsync(() =>
            session.CurrentState.Status is VoiceSessionStatus.Error or VoiceSessionStatus.Completed);

        Assert.Equal(VoiceSessionStatus.Error, session.CurrentState.Status);
        Assert.NotEqual(VoiceSessionStatus.Processing, session.CurrentState.Status);
    }

    [Fact]
    public async Task RecognitionObserverFailure_EndsInError()
    {
        var platform = new StubPlatform
        {
            NextResult = new(true, "log shot dose 18 output 36 time 28", 0.95, null),
        };
        var callbacks = new SpyCb();
        var commands = new StubCommands
        {
            Process = (_, _) => throw new InvalidOperationException("observer failure"),
        };
        await using var session = new PushToTalkVoiceService(
            new StubSpeech(platform),
            commands,
            callbacks);

        await session.InitializeAsync();
        await session.StartAsync();
        await VoiceTestWait.UntilAsync(() => session.CurrentState.Status == VoiceSessionStatus.Error);

        Assert.Equal("observer failure", session.CurrentState.ErrorMessage);
        Assert.NotEqual(VoiceSessionStatus.Processing, session.CurrentState.Status);
    }
}

// ─── Defect 6: UTC count boundaries + filter preservation ────────

public class ShotCountFilterTests
{
    static readonly DateTime FixedUtcNow =
        new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
    readonly VoiceCommandParser _p = new();

    [Theory]
    [InlineData("How many shots today?", "today")]
    [InlineData("How many shots this week?", "this week")]
    [InlineData("How many shots this month?", "this month")]
    [InlineData("How many shots all time?", "all time")]
    public void Period_Preserved(string t, string e) { var c = _p.Parse(t); Assert.Equal(CommandIntent.Query, c.Intent); Assert.Equal(e, c.QueryPeriod); }

    [Fact] public void MadeBy() { var c = _p.Parse("How many shots made by David?"); Assert.Equal("David", c.QueryMadeBy); }
    [Fact] public void MadeFor() { var c = _p.Parse("How many shots made for Sarah?"); Assert.Equal("Sarah", c.QueryMadeFor); }
    [Fact] public void Bean() { var c = _p.Parse("How many shots with Ethiopia Yirgacheffe?"); Assert.Equal("Ethiopia Yirgacheffe", c.QueryBeanName); }
    [Fact] public void MinRating() { var c = _p.Parse("How many shots with minimum rating 3?"); Assert.Equal(3, c.QueryMinimumRating); }
    [Fact] public void InvalidRating() { var c = _p.Parse("How many shots rated 5?"); Assert.NotNull(c.ErrorMessage); }

    [Fact]
    public void Combined()
    {
        var c = _p.Parse("How many shots made by David with Ethiopia rated at least 3 this week?");
        Assert.Equal("David", c.QueryMadeBy);
        Assert.Equal("Ethiopia", c.QueryBeanName);
        Assert.Equal(3, c.QueryMinimumRating);
        Assert.Equal("this week", c.QueryPeriod);
    }

    [Fact]
    public void UnsupportedFilter_Error()
    {
        // "weighted by color" is not a recognized filter pattern
        var c = _p.Parse("How many shots weighted by color today?");
        Assert.Equal(CommandIntent.Query, c.Intent);
        // The parser should report an error for unrecognized filter text
        Assert.NotNull(c.ErrorMessage);
    }

    [Theory]
    [InlineData("today", 1)]
    [InlineData("this week", 5)]
    [InlineData("this month", 4)]
    [InlineData("all time", 8)]
    public async Task UtcPeriods_UseHalfOpenUtcBoundaries(string period, int expected)
    {
        var shots = new[]
        {
            Shot(new DateTime(2026, 8, 29, 23, 59, 59, DateTimeKind.Utc)),
            Shot(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc)),
            Shot(new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc)),
            Shot(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            Shot(new DateTime(2026, 9, 4, 23, 59, 59, DateTimeKind.Utc)),
            Shot(new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc)),
            Shot(new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc)),
            Shot(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
        };

        var result = await Service(shots).ProcessAsync($"How many shots {period}?");

        Assert.True(result.Success);
        Assert.Contains($"{expected} shot", result.Message);
    }

    [Theory]
    [InlineData("How many shots made by David today?", 2)]
    [InlineData("How many shots made for Sarah today?", 2)]
    [InlineData("How many shots with Ethiopia today?", 2)]
    [InlineData("How many shots with minimum rating 3 today?", 2)]
    [InlineData("How many shots made by David with Ethiopia rated at least 3 today?", 1)]
    public async Task CountQuery_AppliesSupportedFilters(string transcript, int expected)
    {
        var shots = new[]
        {
            Shot(FixedUtcNow, "Ethiopia Yirgacheffe", "David", "Sarah", 4),
            Shot(FixedUtcNow, "Ethiopia Guji", "David", "Alex", 2),
            Shot(FixedUtcNow, "Brazil", "Dana", "Sarah", 3),
            Shot(FixedUtcNow.AddMonths(-1), "Ethiopia", "David", "Sarah", 4),
        };

        var result = await Service(shots).ProcessAsync(transcript);

        Assert.True(result.Success);
        Assert.Contains($"{expected} shot", result.Message);
    }

    static BaristaVoiceCommandService Service(IEnumerable<ShotRecordDto> shots) =>
        new(
            new VoiceCommandParser(),
            new SpyCb(),
            new StubShotService(shots),
            new StubBeanService(),
            new StubBagService(),
            new StubEquipmentService(),
            new StubUserProfileService(),
            () => FixedUtcNow);

    static ShotRecordDto Shot(
        DateTime timestamp,
        string bean = "Ethiopia",
        string maker = "David",
        string recipient = "Sarah",
        int rating = 4) => new()
        {
            Timestamp = timestamp,
            Bean = new BeanDto { Name = bean },
            MadeBy = new UserProfileDto { Name = maker },
            MadeFor = new UserProfileDto { Name = recipient },
            Rating = rating,
        };
}
