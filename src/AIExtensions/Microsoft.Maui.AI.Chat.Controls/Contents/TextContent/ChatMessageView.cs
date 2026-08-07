using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;
using System.Globalization;

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
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(ChatMessageView),
            propertyChanged: OnAppearancePropertyChanged);

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty MessageRoleProperty =
        BindableProperty.Create(
            nameof(MessageRole),
            typeof(string),
            typeof(ChatMessageView),
            propertyChanged: OnAppearancePropertyChanged);

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

    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(nameof(ShowAvatars), typeof(bool), typeof(ChatMessageView), false);

    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(nameof(AvatarSize), typeof(double), typeof(ChatMessageView), 28.0);

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(
            nameof(UserDisplayName),
            typeof(string),
            typeof(ChatMessageView),
            "You",
            propertyChanged: OnAppearancePropertyChanged);

    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(
            nameof(AssistantDisplayName),
            typeof(string),
            typeof(ChatMessageView),
            "Assistant",
            propertyChanged: OnAppearancePropertyChanged);

    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    public static readonly BindableProperty DisplayNameProperty =
        BindableProperty.Create(nameof(DisplayName), typeof(string), typeof(ChatMessageView));

    public string? DisplayName
    {
        get => (string?)GetValue(DisplayNameProperty);
        private set => SetValue(DisplayNameProperty, value);
    }

    public static readonly BindableProperty AvatarTextProperty =
        BindableProperty.Create(nameof(AvatarText), typeof(string), typeof(ChatMessageView));

    public string? AvatarText
    {
        get => (string?)GetValue(AvatarTextProperty);
        private set => SetValue(AvatarTextProperty, value);
    }

    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(
            nameof(BubbleCornerRadius),
            typeof(double),
            typeof(ChatMessageView),
            16.0,
            propertyChanged: OnAppearancePropertyChanged);

    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    public static readonly BindableProperty BubbleCornerRadiiProperty =
        BindableProperty.Create(
            nameof(BubbleCornerRadii),
            typeof(CornerRadius),
            typeof(ChatMessageView),
            new CornerRadius(16));

    public CornerRadius BubbleCornerRadii
    {
        get => (CornerRadius)GetValue(BubbleCornerRadiiProperty);
        private set => SetValue(BubbleCornerRadiiProperty, value);
    }

    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(nameof(BubbleStrokeThickness), typeof(double), typeof(ChatMessageView), 0.0);

    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    public static readonly BindableProperty BubbleStrokeColorProperty =
        BindableProperty.Create(nameof(BubbleStrokeColor), typeof(Color), typeof(ChatMessageView));

    public Color? BubbleStrokeColor
    {
        get => (Color?)GetValue(BubbleStrokeColorProperty);
        set => SetValue(BubbleStrokeColorProperty, value);
    }

    public static readonly BindableProperty MaxBubbleWidthProperty =
        BindableProperty.Create(nameof(MaxBubbleWidth), typeof(double), typeof(ChatMessageView), 340.0);

    public double MaxBubbleWidth
    {
        get => (double)GetValue(MaxBubbleWidthProperty);
        set => SetValue(MaxBubbleWidthProperty, value);
    }

    public static readonly BindableProperty SemanticDescriptionProperty =
        BindableProperty.Create(nameof(SemanticDescription), typeof(string), typeof(ChatMessageView));

    public string? SemanticDescription
    {
        get => (string?)GetValue(SemanticDescriptionProperty);
        private set => SetValue(SemanticDescriptionProperty, value);
    }

    private VisualElement? _stateRoot;
    private MessageListView? _appearanceOwner;

    public ChatMessageView()
    {
        AutomationId = "ChatMessage";
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext is not { } context)
            return;

        Text = context.Block is RichContentBlock rich
            ? rich.RawText
            : context.Block.ToString();
        MessageRole = context.Role?.ToString();
        TimestampText = (context.Block.CreatedAt ?? DateTimeOffset.Now)
            .ToLocalTime()
            .ToString("h:mm tt");

        BindAppearance(context.Owner);
        RefreshComputedAppearance();

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

    private void BindAppearance(MessageListView? owner)
    {
        if (owner is null)
            return;

        if (ReferenceEquals(owner, _appearanceOwner))
            return;

        if (_appearanceOwner is not null)
        {
            RemoveBinding(ShowTimestampProperty);
            RemoveBinding(ShowAvatarsProperty);
            RemoveBinding(AvatarSizeProperty);
            RemoveBinding(UserDisplayNameProperty);
            RemoveBinding(AssistantDisplayNameProperty);
            RemoveBinding(BubbleCornerRadiusProperty);
            RemoveBinding(BubbleStrokeThicknessProperty);
            RemoveBinding(BubbleStrokeColorProperty);
            RemoveBinding(MaxBubbleWidthProperty);
        }

        SetBinding(ShowTimestampProperty, CreateOwnerBinding(owner, nameof(MessageListView.ShowTimestamps)));
        SetBinding(ShowAvatarsProperty, CreateOwnerBinding(owner, nameof(MessageListView.ShowAvatars)));
        SetBinding(AvatarSizeProperty, CreateOwnerBinding(owner, nameof(MessageListView.AvatarSize)));
        SetBinding(UserDisplayNameProperty, CreateOwnerBinding(owner, nameof(MessageListView.UserDisplayName)));
        SetBinding(AssistantDisplayNameProperty, CreateOwnerBinding(owner, nameof(MessageListView.AssistantDisplayName)));
        SetBinding(BubbleCornerRadiusProperty, CreateOwnerBinding(owner, nameof(MessageListView.BubbleCornerRadius)));
        SetBinding(BubbleStrokeThicknessProperty, CreateOwnerBinding(owner, nameof(MessageListView.BubbleStrokeThickness)));
        SetBinding(BubbleStrokeColorProperty, CreateOwnerBinding(owner, nameof(MessageListView.BubbleStrokeColor)));
        SetBinding(MaxBubbleWidthProperty, CreateOwnerBinding(owner, nameof(MessageListView.MaxBubbleWidth)));
        _appearanceOwner = owner;
    }

    private static Binding CreateOwnerBinding(MessageListView owner, string path) =>
        new(path, source: owner);

    private static void OnAppearancePropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (ChatMessageView)bindable;
        view.RefreshComputedAppearance();
        view.ApplyRoleState();
    }

    private void RefreshComputedAppearance()
    {
        var isUser = string.Equals(MessageRole, ChatRole.User.ToString(), StringComparison.Ordinal);
        var displayName = isUser ? UserDisplayName : AssistantDisplayName;
        var radius = Math.Max(0, BubbleCornerRadius);
        var tailRadius = Math.Min(4, radius);

        DisplayName = displayName;
        AvatarText = string.IsNullOrWhiteSpace(displayName)
            ? null
            : StringInfo.GetNextTextElement(displayName.Trim()).ToUpperInvariant();
        BubbleCornerRadii = isUser
            ? new CornerRadius(
                topLeft: radius,
                topRight: radius,
                bottomLeft: radius,
                bottomRight: tailRadius)
            : new CornerRadius(
                topLeft: radius,
                topRight: radius,
                bottomLeft: tailRadius,
                bottomRight: radius);

        SemanticDescription = string.IsNullOrWhiteSpace(displayName)
            ? Text
            : $"{displayName}: {Text}";
        SemanticProperties.SetDescription(this, SemanticDescription);
    }
}
