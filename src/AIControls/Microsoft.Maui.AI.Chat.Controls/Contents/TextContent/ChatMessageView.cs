using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

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

    public static readonly BindableProperty TimestampTextProperty =
        BindableProperty.Create(nameof(TimestampText), typeof(string), typeof(ChatMessageView));

    public string? TimestampText
    {
        get => (string?)GetValue(TimestampTextProperty);
        set => SetValue(TimestampTextProperty, value);
    }

    public static readonly BindableProperty ShowTimestampProperty =
        BindableProperty.Create(nameof(ShowTimestamp), typeof(bool), typeof(ChatMessageView), false);

    public bool ShowTimestamp
    {
        get => (bool)GetValue(ShowTimestampProperty);
        set => SetValue(ShowTimestampProperty, value);
    }

    private VisualElement? _stateRoot;

    protected override void RefreshFromContentContext()
    {
        if (ContentContext is null)
            return;

        Text = ContentContext.Block is RichContentBlock rcb ? rcb.RawText : ContentContext.Block?.ToString();
        MessageRole = ContentContext.Role?.ToString();
        TimestampText = DateTimeOffset.Now.ToLocalTime().ToString("h:mm tt");

        // Look up ancestor CopilotChatView for ShowTimestamps setting
        var panel = FindAncestor<CopilotChatView>();
        ShowTimestamp = panel?.ShowTimestamps ?? false;

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

        var roleName = ContentContext.Role == ChatRole.User ? "User"
            : ContentContext.Role == ChatRole.Assistant ? "Assistant"
            : "Tool";

        VisualStateManager.GoToState(_stateRoot ?? this, roleName);
    }

    private T? FindAncestor<T>() where T : Element
    {
        Element? current = Parent;
        while (current is not null)
        {
            if (current is T found)
                return found;
            current = current.Parent;
        }
        return null;
    }
}
