// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components;
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

    private readonly ObservableCollection<ChatAttachment> _attachments = new();
    private readonly ReadOnlyObservableCollection<ChatAttachment> _readOnlyAttachments;

    private string _text = string.Empty;
    private bool _isSending;
    private bool _isRecordingAudio;
    private bool _isTranscribingAudio;
    private bool _isLiveSpeechEnabled;
    private bool _isListening;
    private bool _isComposingOverride;
    private string? _statusMessage;
    private string? _errorMessage;
    private ChatConversation? _conversation;

    /// <summary>Fires when any observable composer state changed.</summary>
    public event Action? Changed;

    /// <summary>Creates the context and wires the immutable callbacks.</summary>
    /// <param name="onSubmit">The submit callback the shell installs.</param>
    /// <param name="onStop">The stop callback the shell installs.</param>
    /// <param name="onPickAttachments">The attachment-pick callback the shell installs.</param>
    /// <param name="onToggleAudioCapture">The audio-capture callback the shell installs.</param>
    /// <param name="onToggleLiveSpeech">The live-speech callback the shell installs.</param>
    public ChatComposerContext(
        EventCallback onSubmit,
        EventCallback onStop,
        EventCallback onPickAttachments,
        EventCallback onToggleAudioCapture,
        EventCallback onToggleLiveSpeech)
    {
        SubmitCallback = onSubmit;
        StopCallback = onStop;
        PickAttachmentsCallback = onPickAttachments;
        ToggleAudioCaptureCallback = onToggleAudioCapture;
        ToggleLiveSpeechCallback = onToggleLiveSpeech;

        _readOnlyAttachments = new ReadOnlyObservableCollection<ChatAttachment>(_attachments);
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
    public bool CanToggleAudioCapture =>
        AllowAudioCapture && !IsConversationBusy;

    /// <inheritdoc />
    public bool CanToggleLiveSpeech =>
        AllowLiveSpeech && !IsConversationBusy;

    /// <inheritdoc />
    public bool IsConversationBusy =>
        Status is ChatConversationStatus.Busy or ChatConversationStatus.AwaitingInput;

    /// <inheritdoc />
    public bool IsComposing =>
        _isRecordingAudio || _isTranscribingAudio || _isListening || _isComposingOverride;

    /// <inheritdoc />
    public bool IsRecordingAudio => _isRecordingAudio;

    /// <inheritdoc />
    public bool IsTranscribingAudio => _isTranscribingAudio;

    /// <inheritdoc />
    public bool IsLiveSpeechEnabled => _isLiveSpeechEnabled;

    /// <inheritdoc />
    public bool IsListening => _isListening;

    /// <inheritdoc />
    public string? StatusMessage => _statusMessage;

    /// <inheritdoc />
    public string? ErrorMessage => _errorMessage;

    /// <inheritdoc />
    public EventCallback SubmitCallback { get; }

    /// <inheritdoc />
    public EventCallback StopCallback { get; }

    /// <inheritdoc />
    public EventCallback PickAttachmentsCallback { get; }

    /// <inheritdoc />
    public EventCallback ToggleAudioCaptureCallback { get; }

    /// <inheritdoc />
    public EventCallback ToggleLiveSpeechCallback { get; }

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

    /// <summary>Forces a change notification without mutating any observable value.</summary>
    internal void NotifyChanged() => RaiseChanged();

    private void RaiseChanged() => Changed?.Invoke();
}
