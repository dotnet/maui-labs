// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// Default implementation of <see cref="IChatComposerContext"/> owned by <see cref="ChatView"/>.
/// </summary>
/// <remarks>
/// The composer state machine lives here. All button actions dispatch through this class so
/// custom composers, leading/trailing action fragments, and layer 2 all funnel into the
/// same guarded send/stop path.
/// </remarks>
internal sealed class ChatComposerContext : IChatComposerContext
{
    /// <summary>The generic, user-safe message shown when sending fails.</summary>
    public const string DefaultSendErrorMessage =
        "Your message could not be sent. Please try again.";

    /// <summary>The generic, user-safe message shown when picking an attachment fails.</summary>
    public const string DefaultAttachmentErrorMessage =
        "That attachment could not be added.";

    private static readonly Func<Task> NoAction = () => Task.CompletedTask;

    private readonly ObservableCollection<ChatAttachment> _attachments = new();
    private readonly ReadOnlyObservableCollection<ChatAttachment> _readOnlyAttachments;

    private Func<Task> _onSubmit = NoAction;
    private Func<Task> _onStop = NoAction;
    private Func<Task> _onPickAttachments = NoAction;
    private Func<Task> _onToggleAudioCapture = NoAction;
    private Func<Task> _onToggleLiveSpeech = NoAction;

    private string _text = string.Empty;
    private bool _isSending;
    private bool _isRecordingAudio;
    private bool _isTranscribingAudio;
    private bool _isAudioStarting;
    private bool _isLiveSpeechEnabled;
    private bool _isListening;
    private bool _isSpeechStarting;
    private bool _isComposingOverride;
    private string? _statusMessage;
    private string? _errorMessage;
    private ChatConversation? _conversation;

    /// <summary>Fires when any observable composer state changed.</summary>
    public event Action? Changed;

    /// <summary>Creates an empty composer context. Actions must be attached via <see cref="AttachActions"/>.</summary>
    public ChatComposerContext()
    {
        _readOnlyAttachments = new ReadOnlyObservableCollection<ChatAttachment>(_attachments);
    }

    /// <summary>Wires the shell-owned action delegates. Called once by <see cref="ChatView"/>.</summary>
    /// <param name="onSubmit">Delegate invoked by <see cref="IChatComposerContext.SubmitAsync"/>.</param>
    /// <param name="onStop">Delegate invoked by <see cref="IChatComposerContext.StopAsync"/>.</param>
    /// <param name="onPickAttachments">Delegate invoked by <see cref="IChatComposerContext.PickAttachmentsAsync"/>.</param>
    /// <param name="onToggleAudioCapture">Delegate invoked by <see cref="IChatComposerContext.ToggleAudioCaptureAsync"/>.</param>
    /// <param name="onToggleLiveSpeech">Delegate invoked by <see cref="IChatComposerContext.ToggleLiveSpeechAsync"/>.</param>
    public void AttachActions(
        Func<Task> onSubmit,
        Func<Task> onStop,
        Func<Task> onPickAttachments,
        Func<Task> onToggleAudioCapture,
        Func<Task> onToggleLiveSpeech)
    {
        _onSubmit = onSubmit ?? NoAction;
        _onStop = onStop ?? NoAction;
        _onPickAttachments = onPickAttachments ?? NoAction;
        _onToggleAudioCapture = onToggleAudioCapture ?? NoAction;
        _onToggleLiveSpeech = onToggleLiveSpeech ?? NoAction;
    }

    /// <summary>Gets or sets whether the conversation currently supports attachments.</summary>
    public bool AllowAttachments { get; set; }

    /// <summary>Gets or sets whether the conversation currently supports audio capture.</summary>
    public bool AllowAudioCapture { get; set; }

    /// <summary>Gets or sets whether the conversation currently supports live speech.</summary>
    public bool AllowLiveSpeech { get; set; }

    /// <inheritdoc />
    public string Text
    {
        get => _text;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_text, next, StringComparison.Ordinal))
            {
                return;
            }

            _text = next;
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ChatAttachment> Attachments => _readOnlyAttachments;

    /// <inheritdoc />
    public ChatConversationStatus Status => _conversation?.Status ?? ChatConversationStatus.Idle;

    /// <inheritdoc />
    public bool CanSubmit => !_isSending && !IsComposing && _conversation is { } conversation
        && conversation.CanSend(CreateDraft());

    /// <inheritdoc />
    public bool CanStop => (_isSending || _conversation?.CanCancel == true) && !_isRecordingAudio && !_isTranscribingAudio;

    /// <inheritdoc />
    public bool CanPickAttachments =>
        AllowAttachments && !IsConversationBusy && !IsComposing;

    /// <inheritdoc />
    /// <inheritdoc />
    /// <remarks>
    /// Allows stopping/cancelling an active audio operation (recording, transcribing,
    /// or the start-up await). Denies starting a fresh recording while live-speech is
    /// active, its subscription is being torn down, or its startup await is still
    /// in flight — a single-microphone-owner rule prevents two modalities from
    /// racing for the same platform capture device.
    /// </remarks>
    public bool CanToggleAudioCapture
    {
        get
        {
            if (!AllowAudioCapture)
            {
                return false;
            }

            // Stopping the active audio operation is always allowed - the user can always
            // abort what they started.
            if (IsAudioActive)
            {
                return true;
            }

            // Starting audio requires: conversation not busy AND live speech is not the
            // current microphone owner (including its startup await).
            return !IsConversationBusy && !IsSpeechActive;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Symmetric to <see cref="CanToggleAudioCapture"/>: allows stopping active speech,
    /// denies starting while audio recording, transcribing, or its startup await is
    /// in flight.
    /// </remarks>
    public bool CanToggleLiveSpeech
    {
        get
        {
            if (!AllowLiveSpeech)
            {
                return false;
            }

            if (IsSpeechActive)
            {
                return true;
            }

            return !IsConversationBusy && !IsAudioActive;
        }
    }

    /// <inheritdoc />
    public bool IsConversationBusy =>
        Status is ChatConversationStatus.Busy or ChatConversationStatus.AwaitingInput;

    /// <inheritdoc />
    public bool IsComposing =>
        IsAudioActive || IsSpeechActive || _isComposingOverride;

    /// <inheritdoc />
    public bool IsRecordingAudio => _isRecordingAudio;

    /// <inheritdoc />
    public bool IsTranscribingAudio => _isTranscribingAudio;

    /// <inheritdoc />
    public bool IsLiveSpeechEnabled => _isLiveSpeechEnabled;

    /// <inheritdoc />
    public bool IsListening => _isListening;

    /// <summary>Gets whether the audio-capture modality is starting, recording, or transcribing.</summary>
    internal bool IsAudioActive =>
        _isRecordingAudio || _isTranscribingAudio || _isAudioStarting;

    /// <summary>Gets whether the live-speech modality is starting, enabled, or listening.</summary>
    internal bool IsSpeechActive =>
        _isLiveSpeechEnabled || _isListening || _isSpeechStarting;

    /// <inheritdoc />
    public string? StatusMessage => _statusMessage;

    /// <inheritdoc />
    public string? ErrorMessage => _errorMessage;

    /// <inheritdoc />
    public Task SubmitAsync() => _onSubmit();

    /// <inheritdoc />
    public Task StopAsync() => _onStop();

    /// <inheritdoc />
    public Task PickAttachmentsAsync() => _onPickAttachments();

    /// <inheritdoc />
    public Task ToggleAudioCaptureAsync() => _onToggleAudioCapture();

    /// <inheritdoc />
    public Task ToggleLiveSpeechAsync() => _onToggleLiveSpeech();

    /// <summary>Sets the conversation this context tracks.</summary>
    /// <param name="conversation">The new conversation.</param>
    public void AttachConversation(ChatConversation? conversation)
    {
        _conversation = conversation;
        RaiseChanged();
    }

    /// <summary>Creates the draft this context would send right now.</summary>
    /// <returns>The trimmed, staged draft.</returns>
    public ChatDraft CreateDraft() => new(_text, _attachments);

    /// <summary>Clears text and attachments that were accepted.</summary>
    /// <param name="draft">The accepted draft.</param>
    public void ClearAcceptedDraft(ChatDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (string.Equals(_text?.Trim(), draft.Text, StringComparison.Ordinal))
        {
            _text = string.Empty;
        }

        foreach (var attachment in draft.Attachments)
        {
            _attachments.Remove(attachment);
        }

        RaiseChanged();
    }

    /// <inheritdoc />
    public ValueTask AddAttachmentAsync(ChatAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        _attachments.Add(attachment);
        RaiseChanged();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAttachmentAsync(ChatAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        var removed = _attachments.Remove(attachment);
        if (removed)
        {
            RaiseChanged();
        }

        return ValueTask.FromResult(removed);
    }

    /// <inheritdoc />
    public void SetStatusMessage(string? value)
    {
        if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
        {
            return;
        }

        _statusMessage = value;
        RaiseChanged();
    }

    /// <inheritdoc />
    public void SetErrorMessage(string? value)
    {
        if (string.Equals(_errorMessage, value, StringComparison.Ordinal))
        {
            return;
        }

        _errorMessage = value;
        RaiseChanged();
    }

    /// <inheritdoc />
    public void SetComposing(bool value)
    {
        if (_isComposingOverride == value)
        {
            return;
        }

        _isComposingOverride = value;
        RaiseChanged();
    }

    /// <summary>Sets the shell's send-in-flight flag.</summary>
    internal void SetIsSending(bool value)
    {
        if (_isSending == value)
        {
            return;
        }

        _isSending = value;
        RaiseChanged();
    }

    /// <summary>Sets the shell's recording flag.</summary>
    internal void SetIsRecordingAudio(bool value)
    {
        if (_isRecordingAudio == value)
        {
            return;
        }

        _isRecordingAudio = value;
        RaiseChanged();
    }

    /// <summary>Sets the shell's audio-transcription flag.</summary>
    internal void SetIsTranscribingAudio(bool value)
    {
        if (_isTranscribingAudio == value)
        {
            return;
        }

        _isTranscribingAudio = value;
        RaiseChanged();
    }

    /// <summary>Sets the shell's live-speech enabled flag.</summary>
    internal void SetIsLiveSpeechEnabled(bool value)
    {
        if (_isLiveSpeechEnabled == value)
        {
            return;
        }

        _isLiveSpeechEnabled = value;
        RaiseChanged();
    }

    /// <summary>Sets the shell's listening flag.</summary>
    internal void SetIsListening(bool value)
    {
        if (_isListening == value)
        {
            return;
        }

        _isListening = value;
        RaiseChanged();
    }

    /// <summary>
    /// Sets the shell's audio-startup-in-flight flag. Set while the recorder is running
    /// <c>StartAsync</c> so the composer treats the microphone as audio-owned across
    /// the await window, denying live-speech from racing for the same device.
    /// </summary>
    internal void SetIsAudioStarting(bool value)
    {
        if (_isAudioStarting == value)
        {
            return;
        }

        _isAudioStarting = value;
        RaiseChanged();
    }

    /// <summary>
    /// Sets the shell's speech-startup-in-flight flag. Set while the recognizer is
    /// running <c>StartAsync</c> so the composer treats the microphone as speech-owned
    /// across the await window, denying audio capture from racing for the same device.
    /// </summary>
    internal void SetIsSpeechStarting(bool value)
    {
        if (_isSpeechStarting == value)
        {
            return;
        }

        _isSpeechStarting = value;
        RaiseChanged();
    }

    /// <summary>Forces a change notification without mutating any observable value.</summary>
    internal void NotifyChanged() => RaiseChanged();

    private void RaiseChanged() => Changed?.Invoke();
}

