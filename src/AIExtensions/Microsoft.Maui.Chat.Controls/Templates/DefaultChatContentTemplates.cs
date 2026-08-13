namespace Microsoft.Maui.Chat.Controls;

/// <summary>Renders <see cref="TextMessageContent"/> with the default text bubble.</summary>
/// <remarks>Registered as a fallback by <see cref="ChatMessagesView.UseDefaultContentTemplates"/>.</remarks>
public class ChatTextContentTemplate : ChatContentTemplate
{
    /// <summary>Creates the template with <see cref="ChatTextContentView"/> as its view.</summary>
    public ChatTextContentTemplate() => ViewType = typeof(ChatTextContentView);

    /// <inheritdoc />
    public override bool When(ChatContentItem item) => item?.Content is TextMessageContent;
}

/// <summary>Renders image <see cref="MediaMessageContent"/> inline with the default media bubble.</summary>
/// <remarks>Only matches content whose <see cref="MediaMessageContent.IsImage"/> is <see langword="true"/>.</remarks>
public class ChatMediaContentTemplate : ChatContentTemplate
{
    /// <summary>Creates the template with <see cref="ChatMediaContentView"/> as its view.</summary>
    public ChatMediaContentTemplate() => ViewType = typeof(ChatMediaContentView);

    /// <inheritdoc />
    public override bool When(ChatContentItem item) =>
        item?.Content is MediaMessageContent { IsImage: true };
}

/// <summary>Renders non-image <see cref="MediaMessageContent"/> as a file card.</summary>
public class ChatFileContentTemplate : ChatContentTemplate
{
    /// <summary>Creates the template with <see cref="ChatFileContentView"/> as its view.</summary>
    public ChatFileContentTemplate() => ViewType = typeof(ChatFileContentView);

    /// <inheritdoc />
    public override bool When(ChatContentItem item) =>
        item?.Content is MediaMessageContent { IsImage: false };
}
