using Microsoft.Maui.Chat.Controls;

namespace ChatControls.Sample;

public partial class MainPage : ContentPage
{
    private readonly SimulatedChatAudioRecorder _simulatedAudioRecorder = new();
    private readonly SimulatedChatSpeechRecognizer _simulatedSpeechRecognizer = new();

    public MainPage()
    {
        InitializeComponent();
        BindingContext = new TeamChatViewModel();
    }

    private void OnStagePhotoClicked(object? sender, EventArgs e) =>
        Chat.AddAttachment(new ChatAttachment(
            "garden-photo.png",
            "image/png",
            new Uri("dotnet_bot.png", UriKind.Relative),
            "A purple .NET bot garden mascot"));

    private void OnStageFileClicked(object? sender, EventArgs e) =>
        Chat.AddAttachment(new ChatAttachment(
            "spring-layout.pdf",
            "application/pdf",
            new Uri("https://example.invalid/garden-layout.pdf")));

    private void OnClearAttachmentsClicked(object? sender, EventArgs e) =>
        Chat.ClearAttachments();

    private void OnSimulatedVoiceToggled(object? sender, ToggledEventArgs e)
    {
        if (e.Value)
        {
            _simulatedSpeechRecognizer.Reset();
            Chat.AudioRecorder = _simulatedAudioRecorder;
            Chat.SpeechRecognizer = _simulatedSpeechRecognizer;
            Chat.InputContext.SetStatusMessage("Simulated microphone enabled.");
            return;
        }

        Chat.ClearValue(ChatView.AudioRecorderProperty);
        Chat.ClearValue(ChatView.SpeechRecognizerProperty);
        Chat.InputContext.SetStatusMessage("Real microphone enabled.");
    }

    private void OnClearClicked(object? sender, EventArgs e) =>
        ClearComposer();

    private void OnResetClicked(object? sender, EventArgs e) =>
        ClearComposer();

    private void ClearComposer()
    {
        Chat.Text = string.Empty;
        Chat.ClearAttachments();
    }
}
