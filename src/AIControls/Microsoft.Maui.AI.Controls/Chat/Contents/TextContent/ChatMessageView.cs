using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Controls.Chat;

/// <summary>
/// Unified text message view for both User and Assistant roles.
/// Uses VisualStateManager to switch styling based on <see cref="MessageRole"/>.
/// Custom templates can include a root named <c>PART_Root</c>; if omitted,
/// the view falls back to applying visual states to itself.
/// </summary>
public class ChatMessageView : ContentContextView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(ChatMessageView));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty MessageRoleProperty =
        BindableProperty.Create(nameof(MessageRole), typeof(string), typeof(ChatMessageView));

    public string? MessageRole
    {
        get => (string?)GetValue(MessageRoleProperty);
        set => SetValue(MessageRoleProperty, value);
    }

    private VisualElement? _stateRoot;

    protected override void RefreshFromContentContext()
    {
        if (ContentContext is null)
            return;

        Text = (ContentContext.Content as TextContent)?.Text;
        MessageRole = ContentContext.Role.ToString();
        ApplyRoleState();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _stateRoot = GetTemplateChild("PART_Root") as VisualElement;
        ApplyRoleState();
    }

    private void ApplyRoleState()
    {
        if (ContentContext is null)
            return;

        VisualStateManager.GoToState(_stateRoot ?? this, ContentContext.Role.ToString());
    }
}
