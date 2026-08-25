using System.Globalization;
using CommunityToolkit.Maui.Media;

namespace Microsoft.Maui.Chat.Controls;

internal sealed class MauiChatSpeechRecognizer : IChatSpeechRecognizer
{
    private ISpeechToText? _speechToText;

    public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

    public bool IsSupported =>
        DeviceInfo.Platform == DevicePlatform.Android
        || DeviceInfo.Platform == DevicePlatform.iOS
        || DeviceInfo.Platform == DevicePlatform.MacCatalyst
        || DeviceInfo.Platform == DevicePlatform.WinUI
        || DeviceInfo.Platform == DevicePlatform.Tizen;

    public bool IsListening { get; private set; }

    public async Task<bool> RequestPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var microphone = await Permissions.RequestAsync<Permissions.Microphone>()
            .ConfigureAwait(true);
        if (microphone != PermissionStatus.Granted)
            return false;

        return await SpeechToText.Default
            .RequestPermissions(cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task StartAsync(
        CultureInfo culture,
        bool reportPartialResults,
        CancellationToken cancellationToken = default)
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
                new SpeechToTextOptions
                {
                    Culture = culture,
                    ShouldReportPartialResults = reportPartialResults,
                },
                cancellationToken).ConfigureAwait(true);
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
            {
                await speechToText.StopListenAsync(cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        finally
        {
            Detach(speechToText);
        }
    }

    private void OnRecognitionResultUpdated(
        object? sender,
        SpeechToTextRecognitionResultUpdatedEventArgs e) =>
        RecognitionChanged?.Invoke(
            this,
            new ChatSpeechRecognitionEventArgs(
                e.RecognitionResult,
                isFinal: false));

    private void OnRecognitionResultCompleted(
        object? sender,
        SpeechToTextRecognitionResultCompletedEventArgs e)
    {
        var speechToText = _speechToText;
        if (speechToText is not null)
            Detach(speechToText);

        var result = e.RecognitionResult;
        var errorKind = result.Exception is null
            ? ChatSpeechRecognitionErrorKind.None
            : Classify(result.Exception);
        RecognitionChanged?.Invoke(
            this,
            new ChatSpeechRecognitionEventArgs(
                result.Text,
                isFinal: true,
                errorKind,
                result.Exception));
    }

    private void Detach(ISpeechToText speechToText)
    {
        speechToText.RecognitionResultUpdated -= OnRecognitionResultUpdated;
        speechToText.RecognitionResultCompleted -= OnRecognitionResultCompleted;
        if (ReferenceEquals(_speechToText, speechToText))
            _speechToText = null;
        IsListening = false;
    }

    internal static ChatSpeechRecognitionErrorKind Classify(Exception exception) =>
        exception switch
        {
            PermissionException or UnauthorizedAccessException =>
                ChatSpeechRecognitionErrorKind.PermissionDenied,
            CultureNotFoundException =>
                ChatSpeechRecognitionErrorKind.LanguageNotSupported,
            OperationCanceledException =>
                ChatSpeechRecognitionErrorKind.Aborted,
            HttpRequestException or TimeoutException =>
                ChatSpeechRecognitionErrorKind.Transient,
            FeatureNotSupportedException =>
                ChatSpeechRecognitionErrorKind.Fatal,
            _ => ChatSpeechRecognitionErrorKind.Fatal,
        };
}
