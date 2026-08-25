using System.Globalization;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>Classifies a speech-recognition interruption.</summary>
public enum ChatSpeechRecognitionErrorKind
{
    /// <summary>No error occurred.</summary>
    None,

    /// <summary>No speech was detected. Continuous recognition can retry silently.</summary>
    NoSpeech,

    /// <summary>The operation was intentionally aborted. Continuous recognition can retry silently.</summary>
    Aborted,

    /// <summary>Microphone or speech-recognition permission was denied.</summary>
    PermissionDenied,

    /// <summary>The requested language is not supported.</summary>
    LanguageNotSupported,

    /// <summary>A recoverable interruption occurred, such as a network failure.</summary>
    Transient,

    /// <summary>A non-recoverable configuration or platform failure occurred.</summary>
    Fatal,
}

/// <summary>Describes a partial result, final result, or failure from an <see cref="IChatSpeechRecognizer"/>.</summary>
public sealed class ChatSpeechRecognitionEventArgs : EventArgs
{
    /// <summary>Creates a recognition event.</summary>
    /// <param name="text">The recognized text, or an empty string for an error.</param>
    /// <param name="isFinal">Whether <paramref name="text"/> is a finalized utterance.</param>
    public ChatSpeechRecognitionEventArgs(string? text, bool isFinal)
        : this(text, isFinal, ChatSpeechRecognitionErrorKind.None, exception: null)
    {
    }

    /// <summary>Creates a recognition event.</summary>
    /// <param name="text">The recognized text, or an empty string for an error.</param>
    /// <param name="isFinal">Whether <paramref name="text"/> is a finalized utterance.</param>
    /// <param name="errorKind">The failure category.</param>
    /// <param name="exception">The underlying failure, when available.</param>
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

    /// <summary>Gets whether <see cref="Text"/> is a finalized utterance.</summary>
    public bool IsFinal { get; }

    /// <summary>Gets the failure category.</summary>
    public ChatSpeechRecognitionErrorKind ErrorKind { get; }

    /// <summary>Gets the underlying failure, when available.</summary>
    public Exception? Exception { get; }
}

/// <summary>Converts microphone input into partial and final text for a <see cref="ChatView"/>.</summary>
/// <remarks>
/// The built-in implementation uses CommunityToolkit.Maui speech-to-text. Replace this service to
/// use an offline recognizer, a provider SDK, or deterministic test input.
/// </remarks>
public interface IChatSpeechRecognizer
{
    /// <summary>Raised for partial results, final results, and recognition failures.</summary>
    event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

    /// <summary>Gets whether speech recognition is supported on the current device.</summary>
    bool IsSupported { get; }

    /// <summary>Gets whether recognition is active.</summary>
    bool IsListening { get; }

    /// <summary>Requests microphone and speech-recognition permissions.</summary>
    /// <param name="cancellationToken">Cancels the permission request.</param>
    /// <returns><see langword="true"/> when both permissions were granted.</returns>
    Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts one recognition pass.</summary>
    /// <param name="culture">The recognition language.</param>
    /// <param name="reportPartialResults">Whether partial text should be reported.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    Task StartAsync(
        CultureInfo culture,
        bool reportPartialResults,
        CancellationToken cancellationToken = default);

    /// <summary>Stops the current recognition pass.</summary>
    /// <param name="cancellationToken">Cancels stopping.</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
