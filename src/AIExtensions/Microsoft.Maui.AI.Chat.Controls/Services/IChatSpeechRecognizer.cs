using System.Globalization;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Classifies why a speech recognition pass ended without a result.</summary>
public enum ChatSpeechRecognitionErrorKind
{
    /// <summary>No error.</summary>
    None,

    /// <summary>The recognizer heard no speech.</summary>
    NoSpeech,

    /// <summary>The pass was aborted, for example because it was stopped.</summary>
    Aborted,

    /// <summary>Microphone or speech recognition permission was denied.</summary>
    PermissionDenied,

    /// <summary>The requested language is not supported.</summary>
    LanguageNotSupported,

    /// <summary>A temporary failure, such as a network interruption, that a retry can recover from.</summary>
    Transient,

    /// <summary>A failure that a retry will not fix.</summary>
    Fatal,
}

/// <summary>Carries an interim or final speech recognition result.</summary>
public sealed class ChatSpeechRecognitionEventArgs : EventArgs
{
    /// <summary>Creates a successful result.</summary>
    /// <param name="text">The recognized text.</param>
    /// <param name="isFinal">Whether the pass has ended.</param>
    public ChatSpeechRecognitionEventArgs(string? text, bool isFinal)
        : this(text, isFinal, ChatSpeechRecognitionErrorKind.None, exception: null)
    {
    }

    /// <summary>Creates a result that may report an error.</summary>
    /// <param name="text">The recognized text.</param>
    /// <param name="isFinal">Whether the pass has ended.</param>
    /// <param name="errorKind">Why the pass failed, or <see cref="ChatSpeechRecognitionErrorKind.None"/>.</param>
    /// <param name="exception">The underlying failure, when there is one.</param>
    public ChatSpeechRecognitionEventArgs(
        string? text,
        bool isFinal,
        ChatSpeechRecognitionErrorKind errorKind,
        Exception? exception)
    {
        Text = text ?? string.Empty;
        IsFinal = isFinal;
        ErrorKind = errorKind;
        Exception = exception;
    }

    /// <summary>Gets the recognized text.</summary>
    public string Text { get; }

    /// <summary>Gets whether the recognition pass has ended.</summary>
    public bool IsFinal { get; }

    /// <summary>Gets why the pass failed, or <see cref="ChatSpeechRecognitionErrorKind.None"/>.</summary>
    public ChatSpeechRecognitionErrorKind ErrorKind { get; }

    /// <summary>Gets the underlying failure, when there is one.</summary>
    public Exception? Exception { get; }
}

/// <summary>Streams live speech recognition results into the message box.</summary>
/// <remarks>
/// <para>
/// <see cref="AppBuilderExtensions.UseAIChat"/> registers a default recognizer built on the
/// .NET MAUI Community Toolkit speech-to-text service.
/// </para>
/// <para>
/// The chat view calls the recognizer on its UI thread. <see cref="RecognitionChanged"/> may be raised on any
/// thread; the view handles each result on its UI thread.
/// </para>
/// </remarks>
public interface IChatSpeechRecognizer
{
    /// <summary>Raised for each interim result and once when a pass ends, on any thread.</summary>
    event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

    /// <summary>Gets whether this device supports speech recognition.</summary>
    bool IsSupported { get; }

    /// <summary>Gets whether a recognition pass is active.</summary>
    bool IsListening { get; }

    /// <summary>Requests microphone and speech recognition permissions.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see langword="true"/> when recognition is allowed.</returns>
    Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a recognition pass.</summary>
    /// <param name="culture">The language to recognize.</param>
    /// <param name="reportPartialResults">Whether to raise interim results.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when the pass has started.</returns>
    Task StartAsync(CultureInfo culture, bool reportPartialResults, CancellationToken cancellationToken = default);

    /// <summary>Stops the active pass.</summary>
    /// <param name="cancellationToken">Cancels the stop.</param>
    /// <returns>A task that completes when the microphone is released.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
