// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// Blazor compatibility adapter over the renderer-neutral <see cref="ChatComposerController"/>.
/// </summary>
/// <remarks>
/// The shell retains ownership of platform service resolution and JavaScript lifetime. All observable
/// composer state, draft preservation, and change callbacks come from the shared controller.
/// </remarks>
internal sealed class ChatComposerContext : IChatComposerContext, IDisposable
{
    internal const string DefaultSendErrorMessage = ChatComposerController.DefaultSendErrorMessage;
    internal const string DefaultAttachmentErrorMessage = ChatComposerController.DefaultAttachmentErrorMessage;

    private readonly bool _ownsController;

    internal ChatComposerContext(ChatComposerController? controller = null)
    {
        _ownsController = controller is null;
        Controller = controller ?? new ChatComposerController();
        Controller.Changed += RaiseChanged;
    }

    internal ChatComposerController Controller { get; }

    public event Action? Changed;

    public bool AllowAttachments
    {
        get => Controller.AllowAttachments;
        set => Controller.AllowAttachments = value;
    }

    public bool AllowAudioCapture
    {
        get => Controller.AllowAudioCapture;
        set => Controller.AllowAudioCapture = value;
    }

    public bool AllowLiveSpeech
    {
        get => Controller.AllowLiveSpeech;
        set => Controller.AllowLiveSpeech = value;
    }

    public string Text
    {
        get => Controller.Text;
        set => Controller.Text = value;
    }

    public IReadOnlyList<ChatAttachment> Attachments => Controller.Attachments;

    public ChatConversationStatus Status => Controller.Status;

    public bool CanSubmit => Controller.CanSubmit;

    public bool CanStop => Controller.CanStop;

    public bool CanPickAttachments => Controller.CanPickAttachments;

    public bool CanToggleAudioCapture => Controller.CanToggleAudioCapture;

    public bool CanToggleLiveSpeech => Controller.CanToggleLiveSpeech;

    public bool IsConversationBusy => Controller.IsConversationBusy;

    public bool IsComposing => Controller.IsComposing;

    public bool IsRecordingAudio => Controller.IsRecordingAudio;

    public bool IsTranscribingAudio => Controller.IsTranscribingAudio;

    // A completed pass retains controller intent so a native surface can resume it after an
    // auto-submit. Blazor's compatibility contract exposes only an active pass.
    public bool IsLiveSpeechEnabled =>
        Controller.IsListening || Controller.IsSpeechStarting || Controller.IsSpeechStopping;

    public bool IsListening => Controller.IsListening;

    internal bool IsAudioStarting => Controller.IsAudioStarting;

    internal bool IsSpeechStarting => Controller.IsSpeechStarting;

    internal bool IsSpeechStopping => Controller.IsSpeechStopping;

    internal bool IsAudioActive => Controller.IsAudioActive;

    internal bool IsSpeechActive => Controller.IsSpeechActive;

    public string? StatusMessage => Controller.StatusMessage;

    public string? ErrorMessage => Controller.ErrorMessage;

    public Task SubmitAsync() => Controller.SubmitAsync();

    public Task StopAsync() => Controller.StopAsync();

    public Task PickAttachmentsAsync() => Controller.PickAttachmentsAsync();

    public Task ToggleAudioCaptureAsync() => Controller.ToggleAudioCaptureAsync();

    public Task ToggleLiveSpeechAsync() => Controller.ToggleLiveSpeechAsync();

    public ValueTask AddAttachmentAsync(ChatAttachment attachment)
    {
        Controller.AddAttachment(attachment);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RemoveAttachmentAsync(ChatAttachment attachment) =>
        ValueTask.FromResult(Controller.RemoveAttachment(attachment));

    public void SetStatusMessage(string? value) => Controller.SetStatusMessage(value);

    public void SetErrorMessage(string? value) => Controller.SetErrorMessage(value);

    public void SetComposing(bool value) => Controller.SetComposing(value);

    internal void AttachConversation(ChatConversation? conversation) => Controller.Conversation = conversation;

    internal ChatDraft CreateDraft() => Controller.CreateDraft();

    internal void ClearAcceptedDraft(ChatDraft draft) => Controller.ClearAcceptedDraft(draft);

    public void Dispose()
    {
        Controller.Changed -= RaiseChanged;
        if (_ownsController)
            Controller.Dispose();
        Changed = null;
    }

    private void RaiseChanged() => Changed?.Invoke();
}
