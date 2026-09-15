namespace Microsoft.Maui.Chat.Controls.Themes;

/// <summary>
/// The resource keys the built-in chat theme defines. Override any of them in your application
/// resources to restyle the controls without replacing a template.
/// </summary>
/// <remarks>Every key is prefixed <c>MauiChat.</c> so it cannot collide with host application resources.</remarks>
public static class ChatThemeKeys
{
    /// <summary>The <see cref="ControlTemplate"/> for <see cref="ChatMessagesView"/>.</summary>
    public const string ChatMessagesViewTemplate = "MauiChat.ChatMessagesViewTemplate";

    /// <summary>The <see cref="ControlTemplate"/> for <see cref="ChatView"/>.</summary>
    public const string ChatViewTemplate = "MauiChat.ChatViewTemplate";

    /// <summary>The <see cref="Style"/> applied to incoming bubbles.</summary>
    public const string IncomingBubbleStyle = "MauiChat.Bubble.Incoming";

    /// <summary>The <see cref="Style"/> applied to outgoing bubbles.</summary>
    public const string OutgoingBubbleStyle = "MauiChat.Bubble.Outgoing";

    /// <summary>The <see cref="Style"/> applied to labels inside incoming bubbles.</summary>
    public const string IncomingTextStyle = "MauiChat.Text.Incoming";

    /// <summary>The <see cref="Style"/> applied to labels inside outgoing bubbles.</summary>
    public const string OutgoingTextStyle = "MauiChat.Text.Outgoing";

    /// <summary>The <see cref="Style"/> applied to spans inside incoming rich text.</summary>
    public const string IncomingSpanStyle = "MauiChat.Span.Incoming";

    /// <summary>The <see cref="Style"/> applied to spans inside outgoing rich text.</summary>
    public const string OutgoingSpanStyle = "MauiChat.Span.Outgoing";

    /// <summary>The <see cref="Style"/> applied to the participant name label.</summary>
    public const string ParticipantNameStyle = "MauiChat.ParticipantName";

    /// <summary>The <see cref="Style"/> applied to the timestamp and status label.</summary>
    public const string MetadataStyle = "MauiChat.Metadata";

    /// <summary>The <see cref="Style"/> applied to the avatar container.</summary>
    public const string AvatarStyle = "MauiChat.Avatar";

    /// <summary>The <see cref="Style"/> applied to the avatar initials label.</summary>
    public const string AvatarTextStyle = "MauiChat.AvatarText";

    /// <summary>The <see cref="Style"/> applied to the file card shown for non-image media.</summary>
    public const string FileCardStyle = "MauiChat.FileCard";

    /// <summary>The <see cref="Style"/> applied to the file name label of a file card.</summary>
    public const string FileNameStyle = "MauiChat.FileName";

    /// <summary>The <see cref="Style"/> applied to the secondary label of a file card.</summary>
    public const string FileDetailStyle = "MauiChat.FileDetail";

    /// <summary>The <see cref="Style"/> applied to suggestion chips.</summary>
    public const string SuggestionStyle = "MauiChat.Suggestion";

    /// <summary>The <see cref="Style"/> applied to composer attachment chips.</summary>
    public const string AttachmentStyle = "MauiChat.Attachment";

    /// <summary>The <see cref="Style"/> applied to the composer input area.</summary>
    public const string InputAreaStyle = "MauiChat.InputArea";

    /// <summary>The <see cref="Style"/> applied to the composer entry.</summary>
    public const string InputEntryStyle = "MauiChat.InputEntry";

    /// <summary>The <see cref="Style"/> applied to the send button.</summary>
    public const string SendButtonStyle = "MauiChat.SendButton";

    /// <summary>The <see cref="Style"/> applied to the attach button.</summary>
    public const string AttachButtonStyle = "MauiChat.AttachButton";

    /// <summary>The <see cref="Style"/> applied to the stop button.</summary>
    public const string StopButtonStyle = "MauiChat.StopButton";

    /// <summary>The <see cref="Style"/> applied to the audio-capture button.</summary>
    public const string AudioButtonStyle = "MauiChat.AudioButton";

    /// <summary>The <see cref="Style"/> applied to the live-speech button.</summary>
    public const string LiveSpeechButtonStyle = "MauiChat.LiveSpeechButton";

    /// <summary>The <see cref="Style"/> applied to audio-message play/pause buttons.</summary>
    public const string AudioPlaybackButtonStyle = "MauiChat.AudioPlaybackButton";

    /// <summary>The <see cref="Style"/> applied to error labels.</summary>
    public const string ErrorTextStyle = "MauiChat.ErrorText";

    /// <summary>The <see cref="Style"/> applied to the typing-participant indicator.</summary>
    public const string TypingIndicatorStyle = "MauiChat.TypingIndicator";

    /// <summary>The <see cref="Style"/> applied to the welcome panel heading.</summary>
    public const string WelcomeIconStyle = "MauiChat.WelcomeIcon";

    /// <summary>The <see cref="Style"/> applied to the welcome panel message.</summary>
    public const string WelcomeMessageStyle = "MauiChat.WelcomeMessage";
}
