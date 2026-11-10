namespace Microsoft.Maui.Chat.Controls;

internal sealed class ChatTemplatedBubbleView : ChatBubbleView
{
    private readonly View _body;
    private readonly ChatContentPresentation? _presentationOverride;

    public ChatTemplatedBubbleView(
        View body,
        ChatContentPresentation? presentationOverride)
    {
        _body = body;
        _presentationOverride = presentationOverride;
        BubbleContent = body;
    }

    protected override void RefreshContent()
    {
        if (_body is ChatContentView contentView)
            contentView.Item = Item;
        else
            _body.BindingContext = Item;

        base.RefreshContent();
    }

    protected override bool ResolveUsesStandardBubble(ChatContentItem item) =>
        (_presentationOverride ?? item.Content.Presentation) == ChatContentPresentation.Bubble;

    protected override string GetContentDescription()
    {
        var description = SemanticProperties.GetDescription(_body);
        if (!string.IsNullOrWhiteSpace(description))
            return description;

        return Item?.Content.GetType().Name ?? string.Empty;
    }
}
