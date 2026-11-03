// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// The reusable state and action surface for a <see cref="ChatView"/> composer.
/// </summary>
/// <remarks>
/// <para>
/// A replacement composer, or the leading and trailing action fragments a consumer supplies to
/// <see cref="ChatView"/>, bind to this context instead of reaching into the shell. The
/// Blazor analogue of the native <c>ChatInputContext</c>.
/// </para>
/// <para>
/// Actions expose plain <see cref="Task"/> / <see cref="ValueTask"/> methods so a consumer can
/// <c>@onclick="() =&gt; composer.SubmitAsync()"</c> or invoke them programmatically without
/// depending on Blazor's <c>EventCallback</c> machinery.
/// </para>
/// </remarks>
public interface IChatComposerContext
{
    /// <summary>Gets or sets the current composer text.</summary>
    string Text { get; set; }

    /// <summary>Gets the attachments staged for the next send.</summary>
    IReadOnlyList<ChatAttachment> Attachments { get; }

    /// <summary>Gets the conversation status the composer reflects.</summary>
    ChatConversationStatus Status { get; }

    /// <summary>Gets whether the current draft can be submitted right now.</summary>
    bool CanSubmit { get; }

    /// <summary>Gets whether an in-flight send or stream can be stopped.</summary>
    bool CanStop { get; }

    /// <summary>Gets whether the attachment picker can be opened.</summary>
    bool CanPickAttachments { get; }

    /// <summary>Gets whether audio capture can be started or stopped.</summary>
    bool CanToggleAudioCapture { get; }

    /// <summary>Gets whether continuous live speech can be started or stopped.</summary>
    bool CanToggleLiveSpeech { get; }

    /// <summary>Gets whether the conversation is streaming or waiting for local input.</summary>
    bool IsConversationBusy { get; }

    /// <summary>Gets whether an attachment, recording, transcription, or speech operation is active.</summary>
    bool IsComposing { get; }

    /// <summary>Gets whether audio is currently being recorded.</summary>
    bool IsRecordingAudio { get; }

    /// <summary>Gets whether captured audio is being transcribed.</summary>
    bool IsTranscribingAudio { get; }

    /// <summary>Gets whether live speech is enabled.</summary>
    bool IsLiveSpeechEnabled { get; }

    /// <summary>Gets whether a speech recognizer is actively listening.</summary>
    bool IsListening { get; }

    /// <summary>Gets a user-safe composer status, or <see langword="null"/> when there is no status message.</summary>
    string? StatusMessage { get; }

    /// <summary>Gets the user-safe composer error, or <see langword="null"/> when there is none.</summary>
    string? ErrorMessage { get; }

    /// <summary>
    /// Submits the current draft. Never throws; expected failures surface through
    /// <see cref="ErrorMessage"/>. When the send is accepted the composer text and staged
    /// attachments are cleared; on rejection or failure the draft is preserved so an
    /// ordinary re-submit re-tries the same request.
    /// </summary>
    Task SubmitAsync();

    /// <summary>Cancels the active send or streaming response.</summary>
    Task StopAsync();

    /// <summary>Opens the configured attachment picker.</summary>
    Task PickAttachmentsAsync();

    /// <summary>Starts, stops, or cancels audio capture.</summary>
    Task ToggleAudioCaptureAsync();

    /// <summary>Starts or stops continuous speech recognition.</summary>
    Task ToggleLiveSpeechAsync();

    /// <summary>Stages an attachment for the next send.</summary>
    /// <param name="attachment">The attachment to stage.</param>
    ValueTask AddAttachmentAsync(ChatAttachment attachment);

    /// <summary>Removes a previously staged attachment.</summary>
    /// <param name="attachment">The attachment to remove.</param>
    /// <returns><see langword="true"/> when the attachment was removed.</returns>
    ValueTask<bool> RemoveAttachmentAsync(ChatAttachment attachment);

    /// <summary>Sets a user-safe composer status message. Use <see langword="null"/> to clear it.</summary>
    /// <param name="value">The status message to display, or <see langword="null"/> to clear it.</param>
    void SetStatusMessage(string? value);

    /// <summary>Sets a user-safe composer error message. Use <see langword="null"/> to clear it.</summary>
    /// <param name="value">The error message to display, or <see langword="null"/> to clear it.</param>
    void SetErrorMessage(string? value);

    /// <summary>Marks a custom asynchronous composer operation as active or inactive.</summary>
    /// <param name="value"><see langword="true"/> to mark composing, <see langword="false"/> to clear it.</param>
    void SetComposing(bool value);
}

