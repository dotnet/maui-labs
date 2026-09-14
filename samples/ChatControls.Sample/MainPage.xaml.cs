using Microsoft.Maui.Chat.Controls;

namespace ChatControls.Sample;

public partial class MainPage : ContentPage
{
    private readonly TeamChatViewModel _viewModel;
    private readonly IChatAudioRecorder _simulatedAudioRecorder;
    private readonly IChatSpeechRecognizer _simulatedSpeechRecognizer;
    private readonly IChatAudioRecorder _platformAudioRecorder;
    private readonly IChatSpeechRecognizer _platformSpeechRecognizer;

    public MainPage(
        TeamChatViewModel viewModel,
        IEnumerable<IChatAudioRecorder> audioRecorders,
        IEnumerable<IChatSpeechRecognizer> speechRecognizers)
    {
        _viewModel = viewModel;
        _simulatedAudioRecorder = audioRecorders
            .OfType<SimulatedChatAudioRecorder>()
            .Single();
        _platformAudioRecorder = audioRecorders
            .First(recorder => recorder is not SimulatedChatAudioRecorder);
        _simulatedSpeechRecognizer = speechRecognizers
            .OfType<SimulatedChatSpeechRecognizer>()
            .Single();
        _platformSpeechRecognizer = speechRecognizers
            .First(recognizer => recognizer is not SimulatedChatSpeechRecognizer);
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnStagePhotoClicked(object? sender, EventArgs e) =>
        StageAttachment(new ChatAttachment(
            "garden-photo.png",
            "image/png",
            new Uri("dotnet_bot.png", UriKind.Relative),
            "A purple .NET bot garden mascot"));

    private void OnStageFileClicked(object? sender, EventArgs e) =>
        StageAttachment(new ChatAttachment(
            "spring-layout.pdf",
            "application/pdf",
            new Uri("https://example.invalid/garden-layout.pdf")));

    private void StageAttachment(ChatAttachment attachment)
    {
        _viewModel.ComposerController.AddAttachment(attachment);
    }

    private void OnClearAttachmentsClicked(object? sender, EventArgs e) =>
        ClearAttachments();

    private async void OnSimulatedVoiceToggled(object? sender, ToggledEventArgs e)
    {
        await _viewModel.ComposerController.CancelAudioCaptureAsync();
        await _viewModel.ComposerController.StopLiveSpeechAsync();

        if (e.Value)
        {
            _viewModel.ComposerController.AudioRecorder = _simulatedAudioRecorder;
            _viewModel.ComposerController.SpeechRecognizer = _simulatedSpeechRecognizer;
            _viewModel.ComposerController.SetStatusMessage("Simulated microphone enabled.");
            return;
        }

        _viewModel.ComposerController.AudioRecorder = _platformAudioRecorder;
        _viewModel.ComposerController.SpeechRecognizer = _platformSpeechRecognizer;
        _viewModel.ComposerController.SetStatusMessage("Platform microphone enabled.");
    }

    private void OnClearClicked(object? sender, EventArgs e) =>
        ClearComposer();

    private void OnResetClicked(object? sender, EventArgs e) =>
        ClearComposer();

    private void ClearComposer()
    {
        _viewModel.ComposerController.Text = string.Empty;
        ClearAttachments();
    }

    private void ClearAttachments()
    {
        foreach (var attachment in _viewModel.ComposerController.Attachments.ToArray())
            _viewModel.ComposerController.RemoveAttachment(attachment);
    }
}
