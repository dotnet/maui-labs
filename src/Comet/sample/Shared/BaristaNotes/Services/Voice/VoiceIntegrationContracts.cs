#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services.Voice;

public enum VoiceSessionStatus
{
    Unconfigured = 0,
    Ready = 1,
    Unavailable = 2,
    PermissionRequired = 3,
    PermissionDenied = 4,
    Listening = 5,
    Processing = 6,
    Completed = 7,
    Error = 8,
    Cancelled = 9,
}

public enum SpeechPermissionStatus
{
    Unknown = 0,
    Granted = 1,
    Denied = 2,
    Restricted = 3,
}

public enum PushToTalkPhase
{
    Started = 0,
    Completed = 1,
    Cancelled = 2,
}

public sealed record PlatformSpeechRecognitionResult(
    bool Success,
    string? Transcript,
    double Confidence,
    string? ErrorMessage,
    bool Cancelled = false);

public interface IPlatformSpeechRecognizer : IAsyncDisposable
{
    event EventHandler<string>? PartialResultReceived;

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<SpeechPermissionStatus> GetPermissionStatusAsync(
        CancellationToken cancellationToken = default);

    Task<SpeechPermissionStatus> RequestPermissionAsync(
        CancellationToken cancellationToken = default);

    Task<PlatformSpeechRecognitionResult> StartListeningAsync(
        CancellationToken cancellationToken = default);

    Task StopListeningAsync(CancellationToken cancellationToken = default);

    Task CancelListeningAsync();
}

public sealed record VoiceFieldUpdates(
    decimal? DoseGrams = null,
    decimal? YieldGrams = null,
    decimal? TimeSeconds = null,
    int? GrindMicrons = null,
    int? Rating = null,
    string? TastingNotes = null)
{
    public bool HasChanges =>
        DoseGrams.HasValue ||
        YieldGrams.HasValue ||
        TimeSeconds.HasValue ||
        GrindMicrons.HasValue ||
        Rating.HasValue ||
        TastingNotes is not null;
}

public sealed record VoiceNavigationRequest(
    string Destination,
    int? EntityId = null,
    string? EntityName = null,
    string? Period = null,
    string? Filter = null,
    int? ParentEntityId = null,
    string? ParentEntityName = null);

public sealed record VoiceToolResultDto(
    bool Success,
    string Message,
    object? CreatedEntity = null,
    int? EntityId = null);

public enum VoiceNavigationOutcome
{
    Navigated,
    KeptEditing,
    Cancelled,
    DestinationUnavailable,
}

public sealed record VoiceNavigationResult(
    VoiceNavigationOutcome Outcome,
    string? Message = null);

public sealed record VoiceOverlayState(
    VoiceSessionStatus Status,
    SpeechRecognitionState RecognitionState,
    CommandStatus CommandStatus,
    string StateText,
    string Transcript = "",
    string? Response = null,
    string? ErrorMessage = null)
{
    public bool IsListening => Status == VoiceSessionStatus.Listening;
    public bool IsProcessing => Status == VoiceSessionStatus.Processing;
    public bool IsReady =>
        Status is VoiceSessionStatus.Ready or
        VoiceSessionStatus.Completed or
        VoiceSessionStatus.Cancelled;
}

public interface IBaristaVoiceCallbacks
{
    void OnVoiceStateChanged(VoiceOverlayState state);

    void ApplyNewDrinkFields(VoiceFieldUpdates updates);

    Task<VoiceToolResultDto> CommitNewDrinkAsync(
        VoiceFieldUpdates updates,
        CancellationToken cancellationToken);

    Task<VoiceNavigationResult> NavigateAsync(
        VoiceNavigationRequest request,
        CancellationToken cancellationToken);

    void ActivateNewDrinkVoice();
}

public enum VoiceQueryKind
{
    ShotCount,
    LastShot,
    Shots,
    Beans,
    Bags,
    Equipment,
    Profiles,
}

public sealed record ParsedVoiceCommand
{
    public CommandIntent Intent { get; init; }
    public string NormalizedTranscript { get; init; } = "";
    public VoiceFieldUpdates FieldUpdates { get; init; } = new();
    public bool CommitNewDrink { get; init; }
    public string? Name { get; init; }
    public string? Roaster { get; init; }
    public string? Origin { get; init; }
    public string? BeanName { get; init; }
    public DateTime? RoastDate { get; init; }
    public EquipmentType EquipmentType { get; init; } = EquipmentType.Other;
    public VoiceNavigationRequest? Navigation { get; init; }
    public string? QueryPeriod { get; init; }
    public string? QueryBeanName { get; init; }
    public string? QueryMadeBy { get; init; }
    public string? QueryMadeFor { get; init; }
    public int? QueryMinimumRating { get; init; }
    public VoiceQueryKind QueryKind { get; init; } = VoiceQueryKind.ShotCount;
    public string? QueryName { get; init; }
    public string? QueryRoaster { get; init; }
    public string? QueryOrigin { get; init; }
    public EquipmentType? QueryEquipmentType { get; init; }
    public bool QueryActiveOnly { get; init; } = true;
    public bool QueryCountOnly { get; init; }
    public int QueryLimit { get; init; } = 5;
    public string? ErrorMessage { get; init; }
}

public interface IBaristaVoiceCommandService
{
    ParsedVoiceCommand Interpret(string transcript);

    Task<VoiceToolResultDto> ProcessAsync(
        string transcript,
        CancellationToken cancellationToken = default);
}
