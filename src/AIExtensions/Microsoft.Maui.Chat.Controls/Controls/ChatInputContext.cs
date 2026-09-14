using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// The reusable state and action surface for a <see cref="ChatView"/> composer.
/// </summary>
/// <remarks>
/// A control template can bind custom buttons or input views to this object instead of reaching into
/// template parts. The context is single-thread affine, like its owning view and conversation.
/// </remarks>
public sealed class ChatInputContext : BindableObject
{
    private readonly ChatComposerController _controller;
    private readonly Func<ValueTask> _focusAsync;
    private CallbackRegistration[] _callbacks = [];

    internal ChatInputContext(ChatComposerController controller, Func<ValueTask> focusAsync)
    {
        _controller = controller;
        _focusAsync = focusAsync;
        SubmitCommand = new Command(
            () => _ = SubmitAsync(),
            () => CanSubmit);
        CancelCommand = new Command(
            () => _ = CancelAsync(),
            () => CanCancel);
        PickAttachmentsCommand = new Command(
            () => _ = PickAttachmentsAsync(),
            () => CanPickAttachments);
        ToggleAudioCaptureCommand = new Command(
            () => _ = ToggleAudioCaptureAsync(),
            () => CanToggleAudioCapture);
        ToggleLiveSpeechCommand = new Command(
            () => _ = ToggleLiveSpeechAsync(),
            () => CanToggleLiveSpeech);
    }

    /// <summary>Gets or sets the current composer text.</summary>
    public string Text
    {
        get => _controller.Text;
        set => _controller.Text = value;
    }

    /// <summary>Gets the staged attachments.</summary>
    public ReadOnlyObservableCollection<ChatAttachment> Attachments => _controller.Attachments;

    /// <summary>Gets the current conversation state.</summary>
    public ChatConversationStatus Status =>
        _controller.Status;

    /// <summary>Gets whether the conversation is streaming or waiting for local input.</summary>
    public bool IsConversationBusy => _controller.IsConversationBusy;

    /// <summary>Gets whether an attachment, recording, transcription, or speech operation is active.</summary>
    public bool IsComposing => _controller.IsComposing;

    /// <summary>Gets whether the current draft can be submitted.</summary>
    public bool CanSubmit => _controller.CanSubmit;

    /// <summary>Gets whether the active response can be stopped.</summary>
    public bool CanCancel => _controller.CanStop;

    /// <summary>Gets whether the attachment picker can be opened.</summary>
    public bool CanPickAttachments =>
        _controller.CanPickAttachments;

    /// <summary>Gets whether audio capture can be started, stopped, or canceled.</summary>
    public bool CanToggleAudioCapture => _controller.CanToggleAudioCapture;

    /// <summary>Gets whether live speech can be started or stopped.</summary>
    public bool CanToggleLiveSpeech => _controller.CanToggleLiveSpeech;

    /// <summary>Gets whether audio is currently being recorded.</summary>
    public bool IsRecordingAudio => _controller.IsRecordingAudio;

    /// <summary>Gets whether captured audio is being transcribed.</summary>
    public bool IsTranscribingAudio => _controller.IsTranscribingAudio;

    /// <summary>Gets whether live speech is enabled.</summary>
    public bool IsLiveSpeechEnabled => _controller.IsLiveSpeechEnabled;

    /// <summary>Gets whether a speech recognizer is actively listening.</summary>
    public bool IsListening => _controller.IsListening;

    /// <summary>Gets the current user-safe composer status.</summary>
    public string? StatusMessage => _controller.StatusMessage;

    /// <summary>Gets the current user-safe composer error.</summary>
    public string? ErrorMessage =>
        _controller.ErrorMessage;

    /// <summary>Gets the submit command for XAML templates.</summary>
    public ICommand SubmitCommand { get; }

    /// <summary>Gets the response-cancel command for XAML templates.</summary>
    public ICommand CancelCommand { get; }

    /// <summary>Gets the attachment-picker command for XAML templates.</summary>
    public ICommand PickAttachmentsCommand { get; }

    /// <summary>Gets the audio-capture command for XAML templates.</summary>
    public ICommand ToggleAudioCaptureCommand { get; }

    /// <summary>Gets the live-speech command for XAML templates.</summary>
    public ICommand ToggleLiveSpeechCommand { get; }

    /// <summary>Stages an attachment.</summary>
    public ValueTask AddAttachmentAsync(ChatAttachment attachment)
    {
        _controller.AddAttachment(attachment);
        return ValueTask.CompletedTask;
    }

    /// <summary>Removes a staged attachment.</summary>
    public ValueTask RemoveAttachmentAsync(ChatAttachment attachment)
    {
        _controller.RemoveAttachment(attachment);
        return ValueTask.CompletedTask;
    }

    /// <summary>Submits the current draft.</summary>
    public Task SubmitAsync() => _controller.SubmitAsync();

    /// <summary>Stops the active response.</summary>
    public Task CancelAsync() => _controller.StopAsync();

    /// <summary>Opens the configured attachment picker.</summary>
    public Task PickAttachmentsAsync(CancellationToken cancellationToken = default) =>
        _controller.PickAttachmentsAsync(cancellationToken);

    /// <summary>Starts or stops audio capture, or cancels an active transcription.</summary>
    public Task ToggleAudioCaptureAsync() => _controller.ToggleAudioCaptureAsync();

    /// <summary>Starts or stops continuous speech recognition.</summary>
    public Task ToggleLiveSpeechAsync() => _controller.ToggleLiveSpeechAsync();

    /// <summary>Focuses the composer input when the template supplies one.</summary>
    public ValueTask FocusAsync() => _focusAsync();

    /// <summary>
    /// Marks a custom asynchronous composer operation as active or inactive.
    /// </summary>
    public void SetComposing(bool value) => _controller.SetComposing(value);

    /// <summary>Sets a user-safe composer status, or clears it with <see langword="null"/>.</summary>
    public void SetStatusMessage(string? value) => _controller.SetStatusMessage(value);

    /// <summary>Sets a user-safe composer error, or clears it with <see langword="null"/>.</summary>
    public void SetErrorMessage(string? value) => _controller.SetErrorMessage(value);

    /// <summary>Registers a synchronous state-change callback.</summary>
    public IDisposable RegisterOnChanged(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var registration = new CallbackRegistration(this, callback);
        _callbacks = [.. _callbacks, registration];
        return registration;
    }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Attachments));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsConversationBusy));
        OnPropertyChanged(nameof(IsComposing));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanPickAttachments));
        OnPropertyChanged(nameof(CanToggleAudioCapture));
        OnPropertyChanged(nameof(CanToggleLiveSpeech));
        OnPropertyChanged(nameof(IsRecordingAudio));
        OnPropertyChanged(nameof(IsTranscribingAudio));
        OnPropertyChanged(nameof(IsLiveSpeechEnabled));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ErrorMessage));

        ((Command)SubmitCommand).ChangeCanExecute();
        ((Command)CancelCommand).ChangeCanExecute();
        ((Command)PickAttachmentsCommand).ChangeCanExecute();
        ((Command)ToggleAudioCaptureCommand).ChangeCanExecute();
        ((Command)ToggleLiveSpeechCommand).ChangeCanExecute();

        foreach (var registration in _callbacks.ToArray())
        {
            if (registration.IsActive)
                registration.Callback();
        }
    }

    private void Unregister(CallbackRegistration registration) =>
        _callbacks = _callbacks
            .Where(existing => !ReferenceEquals(existing, registration))
            .ToArray();

    private sealed class CallbackRegistration(
        ChatInputContext owner,
        Action callback) : IDisposable
    {
        private ChatInputContext? _owner = owner;

        internal Action Callback { get; } = callback;

        internal bool IsActive => _owner is not null;

        public void Dispose()
        {
            var current = _owner;
            _owner = null;
            current?.Unregister(this);
        }
    }
}
