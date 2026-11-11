namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Records a short audio note from the microphone.</summary>
/// <remarks>
/// <see cref="AppBuilderExtensions.UseAIChat"/> registers a default WAV recorder. Throw
/// <see cref="PermissionException"/> from <see cref="StartAsync"/> when microphone access is denied so the
/// chat view can tell the user why recording did not start.
/// </remarks>
public interface IChatAudioRecorder
{
    /// <summary>Gets whether this device can record audio.</summary>
    bool IsSupported { get; }

    /// <summary>Gets whether a recording is in progress.</summary>
    bool IsRecording { get; }

    /// <summary>Starts recording.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when recording has started.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops recording and returns the captured audio.</summary>
    /// <param name="maximumBytes">The largest recording, in bytes, the caller accepts.</param>
    /// <param name="cancellationToken">Cancels the stop.</param>
    /// <returns>
    /// The recording with <see cref="DataContent.Name"/> and an <c>audio/*</c> media type, or
    /// <see langword="null"/> when nothing was captured.
    /// </returns>
    Task<DataContent?> StopAsync(long maximumBytes, CancellationToken cancellationToken = default);

    /// <summary>Stops recording and discards the captured audio.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the microphone is released.</returns>
    Task CancelAsync(CancellationToken cancellationToken = default);
}

/// <summary>Turns a recorded audio note into text for dictation.</summary>
/// <remarks>
/// When <see cref="AIChatView.AudioTranscriber"/> is set, the audio button dictates: the recording is
/// transcribed into the message box instead of being attached.
/// </remarks>
public interface IChatAudioTranscriber
{
    /// <summary>Transcribes a recording.</summary>
    /// <param name="recording">The recorded audio.</param>
    /// <param name="cancellationToken">Cancels the transcription.</param>
    /// <returns>The recognized text, or <see langword="null"/> when no speech was recognized.</returns>
    ValueTask<string?> TranscribeAsync(DataContent recording, CancellationToken cancellationToken = default);
}
