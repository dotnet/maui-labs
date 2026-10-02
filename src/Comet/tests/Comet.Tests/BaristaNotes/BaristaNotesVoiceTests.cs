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

namespace Comet.Tests.BaristaNotes.Voice;

public class VoiceCommandParserTests
{
    readonly VoiceCommandParser _parser = new();

    [Fact]
    public void Parse_LogShotWithSpokenNumbers_ReturnsCompleteFieldUpdate()
    {
        var command = _parser.Parse(
            "Log a shot eighteen grams in thirty six out twenty eight seconds pretty good");

        Assert.Equal(CommandIntent.LogShot, command.Intent);
        Assert.True(command.CommitNewDrink);
        Assert.Equal(18m, command.FieldUpdates.DoseGrams);
        Assert.Equal(36m, command.FieldUpdates.YieldGrams);
        Assert.Equal(28m, command.FieldUpdates.TimeSeconds);
        Assert.Equal(3, command.FieldUpdates.Rating);
        Assert.Null(command.ErrorMessage);
    }

    [Fact]
    public void Parse_LogShotMissingFields_ExplainsExactlyWhatIsMissing()
    {
        var command = _parser.Parse("Log shot dose 18");

        Assert.False(command.CommitNewDrink);
        Assert.Equal("Please provide output and time.", command.ErrorMessage);
    }

    [Fact]
    public void Parse_FieldUpdate_DoesNotCommitDrink()
    {
        var command = _parser.Parse("Set grind setting to 270 microns");

        Assert.Equal(CommandIntent.LogShot, command.Intent);
        Assert.False(command.CommitNewDrink);
        Assert.Equal(270, command.FieldUpdates.GrindMicrons);
    }

    [Theory]
    [InlineData("Set grind to 2147483648 microns")]
    [InlineData("Set grind to 270.5 microns")]
    [InlineData("Set grind to 999999999999999999999999999999999999 microns")]
    public void Parse_GrindOutsideInt32OrFractional_RejectsWithoutThrowing(string transcript)
    {
        var command = _parser.Parse(transcript);

        Assert.Equal(CommandIntent.LogShot, command.Intent);
        Assert.Null(command.FieldUpdates.GrindMicrons);
        Assert.Contains("Int32", command.ErrorMessage);
    }

    [Theory]
    [InlineData("Go to beans", "beans")]
    [InlineData("Open equipment", "equipment")]
    [InlineData("Take me to settings", "settings")]
    [InlineData("Show me my shot history", "activity")]
    [InlineData("Show my shots from last week", "activity")]
    [InlineData("Navigate to the new drink page", "new-drink")]
    public void Parse_NavigationPhrases_MapToGoldDestinations(string transcript, string destination)
    {
        var command = _parser.Parse(transcript);

        Assert.Equal(CommandIntent.Navigate, command.Intent);
        Assert.Equal(destination, command.Navigation?.Destination);
    }

    [Fact]
    public void Parse_LastWeekNavigation_PreservesFilterPeriod()
    {
        var command = _parser.Parse("Show my shots from last week");

        Assert.Equal("last week", command.Navigation?.Period);
    }

    [Fact]
    public void Parse_QueryPhrase_IsNotMisclassifiedAsNavigation()
    {
        var command = _parser.Parse("How many shots today?");

        Assert.Equal(CommandIntent.Query, command.Intent);
        Assert.Equal("today", command.QueryPeriod);
    }

    [Fact]
    public void Parse_CountQuery_PreservesAllGoldFilters()
    {
        var command = _parser.Parse(
            "How many shots with Ethiopia made by David made for Angie rated at least 3 stars this week?");

        Assert.Equal(CommandIntent.Query, command.Intent);
        Assert.Equal("Ethiopia", command.QueryBeanName);
        Assert.Equal("David", command.QueryMadeBy);
        Assert.Equal("Angie", command.QueryMadeFor);
        Assert.Equal(3, command.QueryMinimumRating);
        Assert.Equal("this week", command.QueryPeriod);
        Assert.Null(command.ErrorMessage);
    }

    [Fact]
    public void Parse_CountQuery_WithUnsupportedFilter_RejectsInsteadOfReturningAll()
    {
        var command = _parser.Parse("How many shots from my Niche grinder?");

        Assert.Equal(CommandIntent.Query, command.Intent);
        Assert.Contains("filter", command.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("What was my last shot?", VoiceQueryKind.LastShot)]
    [InlineData("Find shots with Ethiopia", VoiceQueryKind.Shots)]
    [InlineData("List my beans", VoiceQueryKind.Beans)]
    [InlineData("List active bags", VoiceQueryKind.Bags)]
    [InlineData("List grinders", VoiceQueryKind.Equipment)]
    [InlineData("Find profiles named David", VoiceQueryKind.Profiles)]
    public void Parse_SourceQueryPhrases_SelectExpectedQuery(
        string transcript,
        VoiceQueryKind expected)
    {
        var command = _parser.Parse(transcript);

        Assert.Equal(CommandIntent.Query, command.Intent);
        Assert.Equal(expected, command.QueryKind);
    }

    [Theory]
    [InlineData("How many beans do I have?", VoiceQueryKind.Beans)]
    [InlineData("Count all bags", VoiceQueryKind.Bags)]
    [InlineData("How many grinders do I have?", VoiceQueryKind.Equipment)]
    [InlineData("Count user profiles", VoiceQueryKind.Profiles)]
    public void Parse_SourceCountQueries_SelectEntityAndCountMode(
        string transcript,
        VoiceQueryKind expected)
    {
        var command = _parser.Parse(transcript);

        Assert.Equal(CommandIntent.Query, command.Intent);
        Assert.Equal(expected, command.QueryKind);
        Assert.True(command.QueryCountOnly);
    }

    [Fact]
    public void Parse_FindShotQuery_PreservesFiltersAndLimit()
    {
        var command = _parser.Parse(
            "Find shots with Ethiopia made by David made for Angie rated at least 3 stars this week limit 7");

        Assert.Equal(VoiceQueryKind.Shots, command.QueryKind);
        Assert.Equal("Ethiopia", command.QueryBeanName);
        Assert.Equal("David", command.QueryMadeBy);
        Assert.Equal("Angie", command.QueryMadeFor);
        Assert.Equal(3, command.QueryMinimumRating);
        Assert.Equal("this week", command.QueryPeriod);
        Assert.Equal(7, command.QueryLimit);
        Assert.Null(command.ErrorMessage);
    }

    [Fact]
    public void Parse_EntityQueries_PreserveSourceFilters()
    {
        var beans = _parser.Parse("List beans named Yirgacheffe by Counter Culture from Ethiopia limit 3");
        var bags = _parser.Parse("List bags of Yirgacheffe from Counter Culture including finished");
        var equipment = _parser.Parse("Find grinders named Turin");
        var profiles = _parser.Parse("Find profiles named David");

        Assert.Equal("Yirgacheffe", beans.QueryName);
        Assert.Equal("Counter Culture", beans.QueryRoaster);
        Assert.Equal("Ethiopia", beans.QueryOrigin);
        Assert.Equal(3, beans.QueryLimit);
        Assert.Equal("Yirgacheffe", bags.QueryName);
        Assert.Equal("Counter Culture", bags.QueryRoaster);
        Assert.False(bags.QueryActiveOnly);
        Assert.Equal(EquipmentType.Grinder, equipment.QueryEquipmentType);
        Assert.Equal("Turin", equipment.QueryName);
        Assert.Equal("David", profiles.QueryName);
    }

    [Fact]
    public void Parse_AddBean_PreservesNameAndRoaster()
    {
        var command = _parser.Parse(
            "Add a new bean called Ethiopia Yirgacheffe from Counter Culture");

        Assert.Equal(CommandIntent.AddBean, command.Intent);
        Assert.Equal("Ethiopia Yirgacheffe", command.Name);
        Assert.Equal("Counter Culture", command.Roaster);
    }

    [Fact]
    public void Parse_AddBagWithoutName_UsesNoSpuriousBeanName()
    {
        var command = _parser.Parse("Add bag roasted three days ago");

        Assert.Equal(CommandIntent.AddBag, command.Intent);
        Assert.Null(command.BeanName);
        Assert.Equal(DateTime.Today.AddDays(-3), command.RoastDate);
    }

    [Fact]
    public void Parse_TastingNotes_RemovesLastShotReference()
    {
        var command = _parser.Parse(
            "Add tasting notes dark chocolate and citrus to my last shot");

        Assert.Equal(CommandIntent.AddTastingNotes, command.Intent);
        Assert.Equal("dark chocolate and citrus", command.FieldUpdates.TastingNotes);
    }

    [Fact]
    public void Parse_CoffeeRecognitionErrors_NormalizesBeforeParsing()
    {
        var command = _parser.Parse(
            "Record a short doze eighteen grams yelled 30 6 grams twenty eight seconds");

        Assert.Equal(CommandIntent.LogShot, command.Intent);
        Assert.Equal(18m, command.FieldUpdates.DoseGrams);
        Assert.Equal(36m, command.FieldUpdates.YieldGrams);
        Assert.Equal(28m, command.FieldUpdates.TimeSeconds);
    }

    [Fact]
    public void Parse_Cancel_ReturnsCancelIntent()
    {
        Assert.Equal(CommandIntent.Cancel, _parser.Parse("Never mind").Intent);
    }
}

public class PushToTalkVoiceServiceTests
{
    [Fact]
    public async Task Initialize_WhenRecognizerUnavailable_PublishesUnavailable()
    {
        var fixture = new VoiceFixture { Speech = { Available = false } };

        await fixture.Subject.InitializeAsync();

        Assert.Equal(VoiceSessionStatus.Unavailable, fixture.Subject.CurrentState.Status);
        Assert.Contains("not available", fixture.Callbacks.LastState!.ErrorMessage);
    }

    [Fact]
    public async Task Start_WhenPermissionDenied_DoesNotListen()
    {
        var fixture = new VoiceFixture
        {
            Speech = { PermissionGranted = false }
        };

        await fixture.Subject.StartAsync();

        Assert.Equal(VoiceSessionStatus.PermissionDenied, fixture.Subject.CurrentState.Status);
        Assert.False(fixture.Speech.StartCalled);
    }

    [Fact]
    public async Task PressPartialRelease_ProcessesTranscriptAndPublishesCompletion()
    {
        var fixture = new VoiceFixture();
        await fixture.Subject.InitializeAsync();

        await fixture.Subject.HandlePushToTalkAsync(PushToTalkPhase.Started);
        fixture.Speech.PublishPartial("dose 18");
        fixture.Speech.FinalResult = new(true, "Set dose to 18", 0.92, null);
        await fixture.Subject.HandlePushToTalkAsync(PushToTalkPhase.Completed);

        Assert.Equal("Set dose to 18", fixture.Commands.LastTranscript);
        Assert.Equal(VoiceSessionStatus.Completed, fixture.Subject.CurrentState.Status);
        Assert.Equal("Command applied.", fixture.Subject.CurrentState.Response);
        Assert.Contains(
            fixture.Callbacks.States,
            state => state.Status == VoiceSessionStatus.Listening &&
                state.Transcript == "dose 18");
    }

    [Fact]
    public async Task CancelWhileListening_DoesNotExecuteCommand()
    {
        var fixture = new VoiceFixture();
        await fixture.Subject.StartAsync();

        await fixture.Subject.HandlePushToTalkAsync(PushToTalkPhase.Cancelled);

        Assert.Equal(VoiceSessionStatus.Cancelled, fixture.Subject.CurrentState.Status);
        Assert.Null(fixture.Commands.LastTranscript);
    }

    [Fact]
    public async Task RecognitionError_IsExposedToOverlay()
    {
        var fixture = new VoiceFixture();
        await fixture.Subject.StartAsync();
        fixture.Speech.FinalResult = new(false, null, 0, "Recognizer busy");

        await fixture.Subject.CompleteAsync();

        Assert.Equal(VoiceSessionStatus.Error, fixture.Subject.CurrentState.Status);
        Assert.Equal("Recognizer busy", fixture.Subject.CurrentState.ErrorMessage);
    }

    [Fact]
    public async Task Release_WhenFinalRecognitionFails_ProcessesPartialTranscript()
    {
        var fixture = new VoiceFixture();
        await fixture.Subject.StartAsync();
        fixture.Speech.PublishPartial("Set dose to 18");
        fixture.Speech.FinalResult = new(false, null, 0, "No final result");

        await fixture.Subject.CompleteAsync();

        Assert.Equal("Set dose to 18", fixture.Commands.LastTranscript);
        Assert.Equal(VoiceSessionStatus.Completed, fixture.Subject.CurrentState.Status);
    }

    [Fact]
    public async Task RecognizerCompletesBeforeRelease_ProcessesImmediately()
    {
        var fixture = new VoiceFixture();
        await fixture.Subject.StartAsync();

        fixture.Speech.Finish(new(true, "Set dose to 18", 1, null));
        await fixture.Callbacks.WaitForStatusAsync(VoiceSessionStatus.Completed);

        Assert.Equal("Set dose to 18", fixture.Commands.LastTranscript);
        Assert.Equal(VoiceSessionStatus.Completed, fixture.Subject.CurrentState.Status);
    }

    [Fact]
    public async Task PageDeactivation_CancelsPartialWithoutExecutingIt()
    {
        var fixture = new VoiceFixture();
        await fixture.Subject.StartAsync();
        fixture.Speech.PublishPartial("Set dose to 18");

        await fixture.Subject.DeactivateAsync();

        Assert.Equal(VoiceSessionStatus.Cancelled, fixture.Subject.CurrentState.Status);
        Assert.Null(fixture.Commands.LastTranscript);
    }

    [Fact]
    public async Task RecognitionObserver_WhenCommandThrows_PublishesError()
    {
        var fixture = new VoiceFixture();
        fixture.Commands.Exception = new InvalidOperationException("Command observer failed");
        await fixture.Subject.StartAsync();

        fixture.Speech.Finish(new(true, "Set dose to 18", 1, null));
        await fixture.Callbacks.WaitForStatusAsync(VoiceSessionStatus.Error);

        Assert.False(fixture.Subject.CurrentState.IsProcessing);
        Assert.Contains("observer failed", fixture.Subject.CurrentState.ErrorMessage);
    }

    sealed class VoiceFixture
    {
        public FakeSpeechService Speech { get; set; } = new();
        public FakeCommandService Commands { get; } = new();
        public RecordingCallbacks Callbacks { get; } = new();

        public PushToTalkVoiceService Subject =>
            _subject ??= new PushToTalkVoiceService(Speech, Commands, Callbacks);

        PushToTalkVoiceService? _subject;
    }

    sealed class FakeSpeechService : ISpeechRecognitionService
    {
        TaskCompletionSource<SpeechRecognitionResultDto>? _completion;

        public bool Available { get; set; } = true;
        public bool PermissionGranted { get; set; } = true;
        public bool StartCalled { get; private set; }
        public SpeechRecognitionResultDto FinalResult { get; set; } =
            new(true, "Log shot 18 in 36 out 28 seconds", 1, null);
        public SpeechRecognitionState State { get; private set; }

        public event EventHandler<SpeechRecognitionState>? StateChanged;
        public event EventHandler<string>? PartialResultReceived;

        public Task<bool> IsAvailableAsync() => Task.FromResult(Available);
        public Task<bool> RequestPermissionsAsync() => Task.FromResult(PermissionGranted);

        public Task<SpeechRecognitionResultDto> StartListeningAsync(
            CancellationToken cancellationToken = default)
        {
            StartCalled = true;
            State = SpeechRecognitionState.Listening;
            StateChanged?.Invoke(this, State);
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() =>
                _completion.TrySetResult(new(false, null, 0, "Cancelled")));
            return _completion.Task;
        }

        public Task StopListeningAsync()
        {
            State = SpeechRecognitionState.Idle;
            StateChanged?.Invoke(this, State);
            _completion?.TrySetResult(FinalResult);
            return Task.CompletedTask;
        }

        public void PublishPartial(string transcript) =>
            PartialResultReceived?.Invoke(this, transcript);

        public void Finish(SpeechRecognitionResultDto result) =>
            _completion?.TrySetResult(result);
    }

    sealed class FakeCommandService : IBaristaVoiceCommandService
    {
        public string? LastTranscript { get; private set; }
        public Exception? Exception { get; set; }

        public ParsedVoiceCommand Interpret(string transcript) => new()
        {
            Intent = CommandIntent.LogShot,
            NormalizedTranscript = transcript,
        };

        public Task<VoiceToolResultDto> ProcessAsync(
            string transcript,
            CancellationToken cancellationToken = default)
        {
            LastTranscript = transcript;
            if (Exception is not null)
                return Task.FromException<VoiceToolResultDto>(Exception);
            return Task.FromResult(new VoiceToolResultDto(true, "Command applied."));
        }
    }

    sealed class RecordingCallbacks : IBaristaVoiceCallbacks
    {
        TaskCompletionSource<VoiceOverlayState>? _statusWaiter;
        VoiceSessionStatus _awaitedStatus;

        public List<VoiceOverlayState> States { get; } = new();
        public VoiceOverlayState? LastState =>
            States.Count == 0 ? null : States[States.Count - 1];

        public void OnVoiceStateChanged(VoiceOverlayState state)
        {
            States.Add(state);
            if (state.Status == _awaitedStatus)
                _statusWaiter?.TrySetResult(state);
        }

        public Task<VoiceOverlayState> WaitForStatusAsync(VoiceSessionStatus status)
        {
            if (LastState?.Status == status)
                return Task.FromResult(LastState);
            _awaitedStatus = status;
            _statusWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _statusWaiter.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        public void ApplyNewDrinkFields(VoiceFieldUpdates updates) { }
        public Task<VoiceToolResultDto> CommitNewDrinkAsync(
            VoiceFieldUpdates updates,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VoiceToolResultDto(true, "Saved."));
        public Task<VoiceNavigationResult> NavigateAsync(
            VoiceNavigationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VoiceNavigationResult(VoiceNavigationOutcome.Navigated));
        public void ActivateNewDrinkVoice() { }
    }
}

public class NativeSpeechRecognitionServiceTests
{
    [Fact]
    public async Task StartListening_MaximumDuration_CancelsPlatformAndReturnsTimeout()
    {
        var platform = new FakePlatformSpeechRecognizer();
        await using var service = new NativeSpeechRecognitionService(
            platform,
            TimeSpan.FromMilliseconds(20));

        var result = await service.StartListeningAsync();

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(platform.CancelledByToken);
    }

    [Fact]
    public async Task DisposeAsync_CascadesToPlatformAdapter()
    {
        var platform = new FakePlatformSpeechRecognizer();
        var service = new NativeSpeechRecognitionService(platform);

        await service.DisposeAsync();

        Assert.True(platform.Disposed);
    }

    sealed class FakePlatformSpeechRecognizer : IPlatformSpeechRecognizer
    {
        public bool CancelledByToken { get; private set; }
        public bool Disposed { get; private set; }

        public event EventHandler<string>? PartialResultReceived;

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<SpeechPermissionStatus> GetPermissionStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SpeechPermissionStatus.Granted);

        public Task<SpeechPermissionStatus> RequestPermissionAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SpeechPermissionStatus.Granted);

        public Task<PlatformSpeechRecognitionResult> StartListeningAsync(
            CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<PlatformSpeechRecognitionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() =>
            {
                CancelledByToken = true;
                completion.TrySetResult(
                    new(false, null, 0, "Cancelled", Cancelled: true));
            });
            return completion.Task;
        }

        public Task StopListeningAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CancelListeningAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

public class BaristaVoiceCommandServiceQueryTests
{
    static readonly DateTime UtcNow = new(2026, 9, 4, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CountQuery_UsesUtcDayBoundaries()
    {
        var shots = new[]
        {
            Shot(UtcNow.Date, "Ethiopia", "David", "Angie", 4),
            Shot(UtcNow.Date.AddTicks(-1), "Ethiopia", "David", "Angie", 4),
            Shot(UtcNow.Date.AddDays(1).AddTicks(-1), "Ethiopia", "David", "Angie", 4),
        };
        var service = CreateService(shots);

        var result = await service.ProcessAsync("How many shots today?");

        Assert.True(result.Success);
        Assert.Equal("You've pulled 2 shots today.", result.Message);
    }

    [Fact]
    public async Task CountQuery_AppliesMakerRecipientBeanAndMinimumRating()
    {
        var shots = new[]
        {
            Shot(UtcNow.Date, "Ethiopia Yirgacheffe", "David", "Angie", 4),
            Shot(UtcNow.Date, "Ethiopia Yirgacheffe", "David", "Angie", 2),
            Shot(UtcNow.Date, "Ethiopia Yirgacheffe", "Sam", "Angie", 4),
            Shot(UtcNow.Date, "Colombia", "David", "Angie", 4),
            Shot(UtcNow.Date, "Ethiopia Yirgacheffe", "David", "Maya", 4),
        };
        var service = CreateService(shots);

        var result = await service.ProcessAsync(
            "How many shots with Ethiopia made by David made for Angie rated at least 3 stars today?");

        Assert.True(result.Success);
        Assert.Equal("David has made 1 shot today.", result.Message);
    }

    [Fact]
    public async Task CountQuery_PaginatesThroughTotalCount()
    {
        var shots = Enumerable.Range(1, 1001)
            .Select(id => Shot(UtcNow.Date, "Ethiopia", "David", "Angie", 4) with { Id = id })
            .ToList();
        var shotService = new StubShotService(shots);
        var service = new BaristaVoiceCommandService(
            new VoiceCommandParser(),
            new NoOpCallbacks(),
            shotService,
            null!,
            null!,
            null!,
            null!,
            () => UtcNow);

        var result = await service.ProcessAsync("How many shots today?");

        Assert.True(result.Success);
        Assert.Equal("You've pulled 1001 shots today.", result.Message);
        Assert.Equal(Enumerable.Range(0, 11), shotService.RequestedPages);
    }

    [Fact]
    public async Task SourceQueries_ReturnLastShotAndEntityResults()
    {
        var store = new CometBaristaNotes.Data.InMemoryDataStore();
        store.Seed();
        var notifier = new DataChangeNotifier();
        var preferences = new InMemoryPreferencesService();
        var service = new BaristaVoiceCommandService(
            new VoiceCommandParser(),
            new NoOpCallbacks(),
            new InMemoryShotService(store, notifier, preferences),
            new InMemoryBeanService(
                store,
                notifier,
                new InMemoryRatingService(store),
                null),
            new InMemoryBagService(store, notifier),
            new InMemoryEquipmentService(store, notifier),
            new InMemoryUserProfileService(
                store,
                notifier,
                new LocalImageProcessingService()));

        Assert.Contains("Ethiopian Yirgacheffe", (await service.ProcessAsync("What was my last shot?")).Message);
        Assert.Contains("Ethiopian Yirgacheffe", (await service.ProcessAsync("Find shots with Ethiopian")).Message);
        Assert.Contains("Counter Culture", (await service.ProcessAsync("List my beans")).Message);
        Assert.Contains("Active", (await service.ProcessAsync("List active bags")).Message);
        Assert.Contains("Turin DF64V", (await service.ProcessAsync("List grinders")).Message);
        Assert.Contains("David", (await service.ProcessAsync("Find profiles named David")).Message);
        Assert.Contains("2 active beans", (await service.ProcessAsync("How many beans do I have?")).Message);
        Assert.Contains("2 bags total", (await service.ProcessAsync("Count all bags")).Message);
        Assert.Contains("1 active equipment item", (await service.ProcessAsync("How many grinders do I have?")).Message);
        Assert.Contains("1 profile", (await service.ProcessAsync("Count user profiles")).Message);
    }

    static BaristaVoiceCommandService CreateService(IEnumerable<ShotRecordDto> shots) =>
        new(
            new VoiceCommandParser(),
            new NoOpCallbacks(),
            new StubShotService(shots),
            null!,
            null!,
            null!,
            null!,
            () => UtcNow);

    static ShotRecordDto Shot(
        DateTime timestamp,
        string bean,
        string maker,
        string recipient,
        int rating) => new()
        {
            Timestamp = timestamp,
            Bean = new BeanDto { Name = bean },
            MadeBy = new UserProfileDto { Name = maker },
            MadeFor = new UserProfileDto { Name = recipient },
            Rating = rating,
        };

    sealed class NoOpCallbacks : IBaristaVoiceCallbacks
    {
        public void OnVoiceStateChanged(VoiceOverlayState state) { }
        public void ApplyNewDrinkFields(VoiceFieldUpdates updates) { }
        public Task<VoiceToolResultDto> CommitNewDrinkAsync(
            VoiceFieldUpdates updates,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VoiceToolResultDto(true, "Saved."));
        public Task<VoiceNavigationResult> NavigateAsync(
            VoiceNavigationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VoiceNavigationResult(VoiceNavigationOutcome.Navigated));
        public void ActivateNewDrinkVoice() { }
    }

    sealed class StubShotService : CometBaristaNotes.Services.IShotService
    {
        readonly List<ShotRecordDto> _shots;

        public StubShotService(IEnumerable<ShotRecordDto> shots) => _shots = new(shots);
        public List<int> RequestedPages { get; } = new();

        public List<ShotRecord> GetAllShots() => throw new NotSupportedException();
        public ShotRecord? GetShot(int id) => throw new NotSupportedException();
        public ShotRecord CreateShot(ShotRecord shot) => throw new NotSupportedException();
        public ShotRecord UpdateShot(ShotRecord shot) => throw new NotSupportedException();
        public void DeleteShot(int id) => throw new NotSupportedException();
        public List<ShotRecord> GetShotsByBean(int beanId) => throw new NotSupportedException();
        public List<ShotRecord> GetShotsForBag(int bagId) => throw new NotSupportedException();
        public List<ShotRecord> GetFilteredShots(ShotFilterCriteriaDto criteria) =>
            throw new NotSupportedException();
        public List<(int Id, string Name)> GetBeansWithShots() =>
            throw new NotSupportedException();
        public List<(int Id, string Name)> GetPeopleWithShots() =>
            throw new NotSupportedException();

        public Task<PagedResult<ShotRecordDto>> GetShotHistoryAsync(int pageIndex, int pageSize) =>
            Task.FromResult(CreatePage(pageIndex, pageSize));

        PagedResult<ShotRecordDto> CreatePage(int pageIndex, int pageSize)
        {
            RequestedPages.Add(pageIndex);
            return new PagedResult<ShotRecordDto>
            {
                Items = _shots.Skip(pageIndex * pageSize).Take(pageSize).ToList(),
                TotalCount = _shots.Count,
                PageIndex = pageIndex,
                PageSize = pageSize,
            };
        }

        public Task<ShotRecordDto?> GetMostRecentShotAsync() =>
            Task.FromResult<ShotRecordDto?>(
                _shots.OrderByDescending(shot => shot.Timestamp).FirstOrDefault());
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
    }
}

public class VoiceNavigationEntityIdTests
{
    readonly VoiceCommandParser _parser = new();

    [Theory]
    [InlineData("Show me bean 42", "beans", 42)]
    [InlineData("Open bean 7", "beans", 7)]
    [InlineData("Show me bean #3", "beans", 3)]
    [InlineData("Open profile 5", "profiles", 5)]
    [InlineData("Show me profile 12", "profiles", 12)]
    [InlineData("Open equipment 9", "equipment", 9)]
    [InlineData("Show me grinder 1", "equipment", 1)]
    [InlineData("Show me machine 2", "equipment", 2)]
    public void Parse_EntityDetailWithNumericId_PopulatesEntityId(
        string transcript, string expectedDest, int expectedId)
    {
        var command = _parser.Parse(transcript);
        Assert.Equal(CommandIntent.Navigate, command.Intent);
        Assert.NotNull(command.Navigation);
        Assert.Equal(expectedDest, command.Navigation!.Destination);
        Assert.Equal(expectedId, command.Navigation.EntityId);
    }

    [Theory]
    [InlineData("Show me the Ethiopia bean", "beans", "Ethiopia")]
    [InlineData("Open the Stumptown bean", "beans", "Stumptown")]
    public void Parse_EntityDetailWithName_PopulatesEntityName(
        string transcript, string expectedDest, string expectedName)
    {
        var command = _parser.Parse(transcript);
        Assert.Equal(CommandIntent.Navigate, command.Intent);
        Assert.NotNull(command.Navigation);
        Assert.Equal(expectedDest, command.Navigation!.Destination);
        Assert.Null(command.Navigation.EntityId);
        Assert.Equal(expectedName, command.Navigation.EntityName, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Go to beans", "beans")]
    [InlineData("Open equipment", "equipment")]
    [InlineData("Take me to settings", "settings")]
    public void Parse_ManagementNavigation_NoEntityId(string transcript, string expectedDest)
    {
        var command = _parser.Parse(transcript);
        Assert.Equal(CommandIntent.Navigate, command.Intent);
        Assert.NotNull(command.Navigation);
        Assert.Equal(expectedDest, command.Navigation!.Destination);
        Assert.Null(command.Navigation.EntityId);
        Assert.Null(command.Navigation.EntityName);
    }

    [Fact]
    public async Task Service_ResolvesEntityName_ForBeans()
    {
        var store = new CometBaristaNotes.Data.InMemoryDataStore();
        store.Seed();
        var notifier = new DataChangeNotifier();
        var beanService = new InMemoryBeanService(
            store, notifier,
            new InMemoryRatingService(store),
            null);
        var created = await beanService.CreateBeanAsync(new CreateBeanDto { Name = "Ethiopia Yirgacheffe" });
        Assert.True(created.Success);

        var callbacks = new NavigationRecorder();
        var service = new BaristaVoiceCommandService(
            _parser, callbacks, null!, beanService, null!, null!, null!);

        var result = await service.ProcessAsync("Show me the Ethiopia bean");

        Assert.True(result.Success);
        Assert.NotNull(callbacks.LastRequest);
        Assert.Equal("beans", callbacks.LastRequest!.Destination);
        Assert.True(callbacks.LastRequest.EntityId.HasValue,
            "EntityId should be resolved from the name via FuzzyFindByNameRoasterAsync");
        Assert.Equal(created.Data!.Id, callbacks.LastRequest.EntityId!.Value);
    }

    [Fact]
    public async Task Service_NumericId_PassedThrough()
    {
        var callbacks = new NavigationRecorder();
        var service = new BaristaVoiceCommandService(
            _parser, callbacks, null!, null!, null!, null!, null!);

        var result = await service.ProcessAsync("Open bean 42");

        Assert.True(result.Success);
        Assert.NotNull(callbacks.LastRequest);
        Assert.Equal("beans", callbacks.LastRequest!.Destination);
        Assert.Equal(42, callbacks.LastRequest.EntityId);
    }

    [Fact]
    public async Task Service_BagId_ResolvesOwningBeanForDetailRoute()
    {
        var store = new CometBaristaNotes.Data.InMemoryDataStore();
        store.Seed();
        var notifier = new DataChangeNotifier();
        var beans = new InMemoryBeanService(
            store,
            notifier,
            new InMemoryRatingService(store),
            null);
        var bags = new InMemoryBagService(store, notifier);
        var bag = store.Bags[0];
        var bean = await beans.GetBeanByIdAsync(bag.BeanId);
        var callbacks = new NavigationRecorder();
        var service = new BaristaVoiceCommandService(
            _parser, callbacks, null!, beans, bags, null!, null!);

        var result = await service.ProcessAsync($"Open bag {bag.Id}");

        Assert.True(result.Success);
        Assert.NotNull(callbacks.LastRequest);
        Assert.Equal("bags", callbacks.LastRequest!.Destination);
        Assert.Equal(bag.Id, callbacks.LastRequest.EntityId);
        Assert.Equal(bag.BeanId, callbacks.LastRequest.ParentEntityId);
        Assert.Equal(bean!.Name, callbacks.LastRequest.ParentEntityName);
    }

    [Fact]
    public async Task Service_UnknownBag_DoesNotNavigate()
    {
        var store = new CometBaristaNotes.Data.InMemoryDataStore();
        store.Seed();
        var notifier = new DataChangeNotifier();
        var callbacks = new NavigationRecorder();
        var service = new BaristaVoiceCommandService(
            _parser,
            callbacks,
            null!,
            new InMemoryBeanService(
                store,
                notifier,
                new InMemoryRatingService(store),
                null),
            new InMemoryBagService(store, notifier),
            null!,
            null!);

        var result = await service.ProcessAsync("Open bag 999999");

        Assert.False(result.Success);
        Assert.Contains("couldn't find a bag", result.Message);
        Assert.Null(callbacks.LastRequest);
    }

    [Fact]
    public async Task Service_DetailNavigation_WaitsForDiscardBeforeReportingSuccess()
    {
        var store = new CometBaristaNotes.Data.InMemoryDataStore();
        store.Seed();
        var notifier = new DataChangeNotifier();
        var bag = store.Bags[0];
        var callbacks = new GuardedNavigationCallbacks();
        var service = new BaristaVoiceCommandService(
            _parser,
            callbacks,
            null!,
            new InMemoryBeanService(
                store,
                notifier,
                new InMemoryRatingService(store),
                null),
            new InMemoryBagService(store, notifier),
            null!,
            null!);

        var processing = service.ProcessAsync($"Open bag {bag.Id}");

        Assert.False(processing.IsCompleted);
        Assert.Null(callbacks.AppliedRequest);
        callbacks.DiscardChanges();

        var result = await processing;
        Assert.True(result.Success);
        Assert.Equal(bag.Id, callbacks.AppliedRequest?.EntityId);
        Assert.Equal(bag.BeanId, callbacks.AppliedRequest?.ParentEntityId);
    }

    [Fact]
    public async Task Service_NavigationKeepEditing_ReturnsNonSuccessWithoutNavigating()
    {
        var callbacks = new GuardedNavigationCallbacks();
        var service = new BaristaVoiceCommandService(
            _parser,
            callbacks,
            null!,
            null!,
            null!,
            null!,
            null!);

        var processing = service.ProcessAsync("Open bean 42");

        Assert.False(processing.IsCompleted);
        Assert.Null(callbacks.AppliedRequest);
        callbacks.KeepEditing();

        var result = await processing;
        Assert.False(result.Success);
        Assert.Contains("unsaved changes", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(callbacks.AppliedRequest);
    }

    sealed class NavigationRecorder : IBaristaVoiceCallbacks
    {
        public VoiceNavigationRequest? LastRequest { get; private set; }

        public void OnVoiceStateChanged(VoiceOverlayState state) { }
        public void ApplyNewDrinkFields(VoiceFieldUpdates updates) { }
        public Task<VoiceToolResultDto> CommitNewDrinkAsync(VoiceFieldUpdates updates, CancellationToken ct) =>
            Task.FromResult(new VoiceToolResultDto(false, "Not implemented"));
        public Task<VoiceNavigationResult> NavigateAsync(
            VoiceNavigationRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new VoiceNavigationResult(VoiceNavigationOutcome.Navigated));
        }
        public void ActivateNewDrinkVoice() { }
    }

    sealed class GuardedNavigationCallbacks : IBaristaVoiceCallbacks
    {
        readonly EditorLeaveGuard _guard = new();
        EditorLeaveRequest _request;

        public VoiceNavigationRequest? AppliedRequest { get; private set; }

        public void DiscardChanges() =>
            _guard.Confirm(_request.Generation, () => { });
        public void KeepEditing() =>
            _guard.KeepEditing(_request.Generation);
        public void OnVoiceStateChanged(VoiceOverlayState state) { }
        public void ApplyNewDrinkFields(VoiceFieldUpdates updates) { }
        public Task<VoiceToolResultDto> CommitNewDrinkAsync(
            VoiceFieldUpdates updates,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VoiceToolResultDto(true, "Saved."));

        public async Task<VoiceNavigationResult> NavigateAsync(
            VoiceNavigationRequest request,
            CancellationToken cancellationToken)
        {
            _request = _guard.Begin(
                requiresConfirmation: true,
                () => AppliedRequest = request,
                cancellationToken);
            var outcome = await _request.Completion;
            return new VoiceNavigationResult(outcome switch
            {
                EditorLeaveOutcome.Left => VoiceNavigationOutcome.Navigated,
                EditorLeaveOutcome.KeptEditing => VoiceNavigationOutcome.KeptEditing,
                _ => VoiceNavigationOutcome.Cancelled,
            });
        }

        public void ActivateNewDrinkVoice() { }
    }
}
