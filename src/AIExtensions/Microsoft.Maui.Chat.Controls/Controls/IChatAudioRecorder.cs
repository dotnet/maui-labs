namespace Microsoft.Maui.Chat.Controls;

/// <summary>Records an audio attachment for a <see cref="ChatView"/> composer.</summary>
/// <remarks>
/// The built-in implementation records WAV audio on Android, iOS, Mac Catalyst, and Windows.
/// Replace this service to use another recorder or to make capture deterministic in tests.
/// </remarks>
public interface IChatAudioRecorder
{
    /// <summary>Gets whether recording is supported on the current device.</summary>
    bool IsSupported { get; }

    /// <summary>Gets whether a recording is active.</summary>
    bool IsRecording { get; }

    /// <summary>Requests microphone access and starts recording.</summary>
    /// <param name="cancellationToken">Cancels startup.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops recording and returns the captured audio, or <see langword="null"/> when no audio was captured.</summary>
    /// <param name="maximumBytes">The largest accepted recording size.</param>
    /// <param name="cancellationToken">Cancels reading the captured audio.</param>
    Task<ChatAttachment?> StopAsync(
        long maximumBytes,
        CancellationToken cancellationToken = default);

    /// <summary>Stops and discards an active recording.</summary>
    /// <param name="cancellationToken">Cancels the stop operation.</param>
    Task CancelAsync(CancellationToken cancellationToken = default);
}

/// <summary>Converts a captured audio attachment into text.</summary>
public interface IChatAudioTranscriber
{
    /// <summary>Transcribes <paramref name="recording"/>.</summary>
    /// <param name="recording">The captured audio.</param>
    /// <param name="cancellationToken">Cancels transcription.</param>
    /// <returns>The recognized text, or <see langword="null"/> when no speech was recognized.</returns>
    ValueTask<string?> TranscribeAsync(
        ChatAttachment recording,
        CancellationToken cancellationToken = default);
}

/// <summary>Event data for a captured audio attachment.</summary>
public sealed class ChatAudioRecordedEventArgs(ChatAttachment recording) : EventArgs
{
    /// <summary>Gets the captured audio.</summary>
    public ChatAttachment Recording { get; } =
        recording ?? throw new ArgumentNullException(nameof(recording));
}

/// <summary>Event data for completed audio transcription.</summary>
public sealed class ChatAudioTranscribedEventArgs(
    ChatAttachment recording,
    string text) : EventArgs
{
    /// <summary>Gets the captured audio.</summary>
    public ChatAttachment Recording { get; } =
        recording ?? throw new ArgumentNullException(nameof(recording));

    /// <summary>Gets the trimmed transcription.</summary>
    public string Text { get; } =
        !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : throw new ArgumentException("Transcription text cannot be blank.", nameof(text));
}
