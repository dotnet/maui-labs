using System.Globalization;
using CommunityToolkit.Maui.Media;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>The default <see cref="IChatSpeechRecognizer"/>: the Community Toolkit speech-to-text service.</summary>
internal sealed class MauiChatSpeechRecognizer : IChatSpeechRecognizer
{
    private ISpeechToText? _speechToText;
    private bool? _isSupported;

    public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

    public bool IsSupported => _isSupported ??= GetIsSupported();

    private static bool GetIsSupported()
    {
        try
        {
            var platform = DeviceInfo.Platform;
            return platform == DevicePlatform.Android
                || platform == DevicePlatform.iOS
                || platform == DevicePlatform.MacCatalyst
                || platform == DevicePlatform.WinUI
                || platform == DevicePlatform.Tizen;
        }
        catch
        {
            // No platform (unit tests, unsupported hosts).
            return false;
        }
    }

    public bool IsListening { get; private set; }

    public async Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var microphone = await Permissions.RequestAsync<Permissions.Microphone>();
        if (microphone != PermissionStatus.Granted)
            return false;

        return await SpeechToText.Default.RequestPermissions(cancellationToken);
    }

    public async Task StartAsync(CultureInfo culture, bool reportPartialResults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        cancellationToken.ThrowIfCancellationRequested();

        if (IsListening)
            throw new InvalidOperationException("Speech recognition is already active.");

        var speechToText = SpeechToText.Default;
        _speechToText = speechToText;
        speechToText.RecognitionResultUpdated += OnRecognitionResultUpdated;
        speechToText.RecognitionResultCompleted += OnRecognitionResultCompleted;
        IsListening = true;

        try
        {
            await speechToText.StartListenAsync(
                new CommunityToolkit.Maui.Media.SpeechToTextOptions { Culture = culture, ShouldReportPartialResults = reportPartialResults },
                cancellationToken);
        }
        catch
        {
            Detach(speechToText);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var speechToText = _speechToText;
        if (speechToText is null)
            return;

        try
        {
            if (IsListening)
                await speechToText.StopListenAsync(cancellationToken);
        }
        finally
        {
            Detach(speechToText);
        }
    }

    internal static ChatSpeechRecognitionErrorKind Classify(Exception exception) =>
        exception switch
        {
            PermissionException or UnauthorizedAccessException => ChatSpeechRecognitionErrorKind.PermissionDenied,
            CultureNotFoundException => ChatSpeechRecognitionErrorKind.LanguageNotSupported,
            OperationCanceledException => ChatSpeechRecognitionErrorKind.Aborted,
            HttpRequestException or TimeoutException => ChatSpeechRecognitionErrorKind.Transient,
            _ => ChatSpeechRecognitionErrorKind.Fatal,
        };

    private void OnRecognitionResultUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs e) =>
        RecognitionChanged?.Invoke(this, new ChatSpeechRecognitionEventArgs(e.RecognitionResult, isFinal: false));

    private void OnRecognitionResultCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e)
    {
        if (_speechToText is { } speechToText)
            Detach(speechToText);

        var result = e.RecognitionResult;
        var errorKind = result.Exception is null ? ChatSpeechRecognitionErrorKind.None : Classify(result.Exception);
        RecognitionChanged?.Invoke(
            this,
            new ChatSpeechRecognitionEventArgs(result.Text, isFinal: true, errorKind, result.Exception));
    }

    private void Detach(ISpeechToText speechToText)
    {
        speechToText.RecognitionResultUpdated -= OnRecognitionResultUpdated;
        speechToText.RecognitionResultCompleted -= OnRecognitionResultCompleted;
        if (ReferenceEquals(_speechToText, speechToText))
            _speechToText = null;
        IsListening = false;
    }
}
