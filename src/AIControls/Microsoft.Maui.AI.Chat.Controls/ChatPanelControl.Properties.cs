namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Bindable customization properties for <see cref="ChatPanelControl"/>.
/// </summary>
public partial class ChatPanelControl
{
    // ═══════════════════════════════════════════════════════════════
    //  INPUT AREA
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(ChatPanelControl), "Type a message...");

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty SendButtonTextProperty =
        BindableProperty.Create(nameof(SendButtonText), typeof(string), typeof(ChatPanelControl), "\u27A4");

    public string SendButtonText
    {
        get => (string)GetValue(SendButtonTextProperty);
        set => SetValue(SendButtonTextProperty, value);
    }

    public static readonly BindableProperty SendButtonBackgroundColorProperty =
        BindableProperty.Create(nameof(SendButtonBackgroundColor), typeof(Color), typeof(ChatPanelControl));

    public Color? SendButtonBackgroundColor
    {
        get => (Color?)GetValue(SendButtonBackgroundColorProperty);
        set => SetValue(SendButtonBackgroundColorProperty, value);
    }

    public static readonly BindableProperty InputAreaBackgroundColorProperty =
        BindableProperty.Create(nameof(InputAreaBackgroundColor), typeof(Color), typeof(ChatPanelControl));

    public Color? InputAreaBackgroundColor
    {
        get => (Color?)GetValue(InputAreaBackgroundColorProperty);
        set => SetValue(InputAreaBackgroundColorProperty, value);
    }

    public static readonly BindableProperty InputAreaCornerRadiusProperty =
        BindableProperty.Create(nameof(InputAreaCornerRadius), typeof(double), typeof(ChatPanelControl), 14.0);

    public double InputAreaCornerRadius
    {
        get => (double)GetValue(InputAreaCornerRadiusProperty);
        set => SetValue(InputAreaCornerRadiusProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  WELCOME MESSAGE
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty WelcomeMessageProperty =
        BindableProperty.Create(nameof(WelcomeMessage), typeof(string), typeof(ChatPanelControl),
            propertyChanged: (b, _, _) => ((ChatPanelControl)b).UpdateWelcomeVisibility());

    public string? WelcomeMessage
    {
        get => (string?)GetValue(WelcomeMessageProperty);
        set => SetValue(WelcomeMessageProperty, value);
    }

    public static readonly BindableProperty WelcomeIconProperty =
        BindableProperty.Create(nameof(WelcomeIcon), typeof(string), typeof(ChatPanelControl), "💬");

    public string WelcomeIcon
    {
        get => (string)GetValue(WelcomeIconProperty);
        set => SetValue(WelcomeIconProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  AVATARS
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(nameof(ShowAvatars), typeof(bool), typeof(ChatPanelControl), false);

    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(nameof(AvatarSize), typeof(double), typeof(ChatPanelControl), 28.0);

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(nameof(UserDisplayName), typeof(string), typeof(ChatPanelControl), "You");

    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(nameof(AssistantDisplayName), typeof(string), typeof(ChatPanelControl), "Assistant");

    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  TIMESTAMPS & TOOL VISIBILITY
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(nameof(ShowTimestamps), typeof(bool), typeof(ChatPanelControl), false);

    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    public static readonly BindableProperty ShowToolCallsProperty =
        BindableProperty.Create(nameof(ShowToolCalls), typeof(bool), typeof(ChatPanelControl), true);

    public bool ShowToolCalls
    {
        get => (bool)GetValue(ShowToolCallsProperty);
        set => SetValue(ShowToolCallsProperty, value);
    }

    public static readonly BindableProperty ShowToolResultsProperty =
        BindableProperty.Create(nameof(ShowToolResults), typeof(bool), typeof(ChatPanelControl), true);

    public bool ShowToolResults
    {
        get => (bool)GetValue(ShowToolResultsProperty);
        set => SetValue(ShowToolResultsProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  MESSAGE BUBBLE STYLING
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(nameof(BubbleCornerRadius), typeof(double), typeof(ChatPanelControl), 16.0);

    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(nameof(BubbleStrokeThickness), typeof(double), typeof(ChatPanelControl), 0.0);

    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    public static readonly BindableProperty BubbleStrokeColorProperty =
        BindableProperty.Create(nameof(BubbleStrokeColor), typeof(Color), typeof(ChatPanelControl));

    public Color? BubbleStrokeColor
    {
        get => (Color?)GetValue(BubbleStrokeColorProperty);
        set => SetValue(BubbleStrokeColorProperty, value);
    }

    public static readonly BindableProperty MaxBubbleWidthProperty =
        BindableProperty.Create(nameof(MaxBubbleWidth), typeof(double), typeof(ChatPanelControl), 340.0);

    public double MaxBubbleWidth
    {
        get => (double)GetValue(MaxBubbleWidthProperty);
        set => SetValue(MaxBubbleWidthProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  LAYOUT TEMPLATES
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty HeaderTemplateProperty =
        BindableProperty.Create(nameof(HeaderTemplate), typeof(DataTemplate), typeof(ChatPanelControl));

    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public static readonly BindableProperty FooterTemplateProperty =
        BindableProperty.Create(nameof(FooterTemplate), typeof(DataTemplate), typeof(ChatPanelControl));

    public DataTemplate? FooterTemplate
    {
        get => (DataTemplate?)GetValue(FooterTemplateProperty);
        set => SetValue(FooterTemplateProperty, value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SUGGESTIONS
    // ═══════════════════════════════════════════════════════════════

    public static readonly BindableProperty SuggestionPromptsProperty =
        BindableProperty.Create(nameof(SuggestionPrompts), typeof(IList<string>), typeof(ChatPanelControl));

    public IList<string>? SuggestionPrompts
    {
        get => (IList<string>?)GetValue(SuggestionPromptsProperty);
        set => SetValue(SuggestionPromptsProperty, value);
    }
}
