using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Controls.Templates;

namespace Microsoft.Maui.AI.Controls.Controls;

// AgentChatView — BindableProperty declarations
public partial class AgentChatView
{
    // ══════════════════════════════════════════════════════════════
    //  SESSION
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(nameof(Session), typeof(IAgentSession), typeof(AgentChatView),
            propertyChanged: OnSessionChanged);

    public IAgentSession? Session
    {
        get => (IAgentSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  STATE
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty MessagesProperty =
        BindableProperty.Create(nameof(Messages), typeof(ObservableCollection<ChatMessageViewModel>),
            typeof(AgentChatView));

    public ObservableCollection<ChatMessageViewModel>? Messages
    {
        get => (ObservableCollection<ChatMessageViewModel>?)GetValue(MessagesProperty);
        private set => SetValue(MessagesProperty, value);
    }

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(AgentChatView), false);

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        private set => SetValue(IsBusyProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  INPUT
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty InputTextProperty =
        BindableProperty.Create(nameof(InputText), typeof(string), typeof(AgentChatView), "",
            BindingMode.TwoWay);

    public string InputText
    {
        get => (string)GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(AgentChatView), "Ask anything...",
            propertyChanged: (b, _, v) => { if (b is AgentChatView c && c._inputEditor is not null) c._inputEditor.Placeholder = v as string; });

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty SuggestionPromptsProperty =
        BindableProperty.Create(nameof(SuggestionPrompts), typeof(IList<Suggestion>), typeof(AgentChatView));

    public IList<Suggestion>? SuggestionPrompts
    {
        get => (IList<Suggestion>?)GetValue(SuggestionPromptsProperty);
        set => SetValue(SuggestionPromptsProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  TEMPLATES
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty UserMessageTemplateProperty =
        BindableProperty.Create(nameof(UserMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? UserMessageTemplate
    {
        get => (DataTemplate?)GetValue(UserMessageTemplateProperty);
        set => SetValue(UserMessageTemplateProperty, value);
    }

    public static readonly BindableProperty AssistantMessageTemplateProperty =
        BindableProperty.Create(nameof(AssistantMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? AssistantMessageTemplate
    {
        get => (DataTemplate?)GetValue(AssistantMessageTemplateProperty);
        set => SetValue(AssistantMessageTemplateProperty, value);
    }

    public static readonly BindableProperty ToolMessageTemplateProperty =
        BindableProperty.Create(nameof(ToolMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? ToolMessageTemplate
    {
        get => (DataTemplate?)GetValue(ToolMessageTemplateProperty);
        set => SetValue(ToolMessageTemplateProperty, value);
    }

    public static readonly BindableProperty SystemMessageTemplateProperty =
        BindableProperty.Create(nameof(SystemMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? SystemMessageTemplate
    {
        get => (DataTemplate?)GetValue(SystemMessageTemplateProperty);
        set => SetValue(SystemMessageTemplateProperty, value);
    }

    public static readonly BindableProperty ErrorMessageTemplateProperty =
        BindableProperty.Create(nameof(ErrorMessageTemplate), typeof(DataTemplate), typeof(AgentChatView),
            propertyChanged: OnMessageTemplateChanged);

    public DataTemplate? ErrorMessageTemplate
    {
        get => (DataTemplate?)GetValue(ErrorMessageTemplateProperty);
        set => SetValue(ErrorMessageTemplateProperty, value);
    }

    public static readonly BindableProperty SuggestionItemTemplateProperty =
        BindableProperty.Create(nameof(SuggestionItemTemplate), typeof(DataTemplate), typeof(AgentChatView));

    public DataTemplate? SuggestionItemTemplate
    {
        get => (DataTemplate?)GetValue(SuggestionItemTemplateProperty);
        set => SetValue(SuggestionItemTemplateProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  SIDEBAR / HEADER / FOOTER TEMPLATES
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty HeaderTemplateProperty =
        BindableProperty.Create(nameof(HeaderTemplate), typeof(DataTemplate), typeof(AgentChatView));

    /// <summary>Optional template rendered above the message list.</summary>
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public static readonly BindableProperty FooterTemplateProperty =
        BindableProperty.Create(nameof(FooterTemplate), typeof(DataTemplate), typeof(AgentChatView));

    /// <summary>Optional template rendered between the message list and the input area.</summary>
    public DataTemplate? FooterTemplate
    {
        get => (DataTemplate?)GetValue(FooterTemplateProperty);
        set => SetValue(FooterTemplateProperty, value);
    }

    public static readonly BindableProperty SidebarTemplateProperty =
        BindableProperty.Create(nameof(SidebarTemplate), typeof(DataTemplate), typeof(AgentChatView));

    /// <summary>Optional template for a sidebar panel (document editor, plan progress, etc.).</summary>
    public DataTemplate? SidebarTemplate
    {
        get => (DataTemplate?)GetValue(SidebarTemplateProperty);
        set => SetValue(SidebarTemplateProperty, value);
    }

    public static readonly BindableProperty SidebarPlacementProperty =
        BindableProperty.Create(nameof(SidebarPlacement), typeof(SidebarPlacement), typeof(AgentChatView),
            SidebarPlacement.None);

    /// <summary>Where to place the sidebar panel relative to the chat area.</summary>
    public SidebarPlacement SidebarPlacement
    {
        get => (SidebarPlacement)GetValue(SidebarPlacementProperty);
        set => SetValue(SidebarPlacementProperty, value);
    }

    public static readonly BindableProperty StateContextProperty =
        BindableProperty.Create(nameof(StateContext), typeof(object), typeof(AgentChatView));

    /// <summary>Arbitrary context object for the sidebar/header/footer templates to bind against.</summary>
    public object? StateContext
    {
        get => GetValue(StateContextProperty);
        set => SetValue(StateContextProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  APPEARANCE
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty WelcomeTitleProperty =
        BindableProperty.Create(nameof(WelcomeTitle), typeof(string), typeof(AgentChatView), "Welcome");

    public string WelcomeTitle
    {
        get => (string)GetValue(WelcomeTitleProperty);
        set => SetValue(WelcomeTitleProperty, value);
    }

    public static readonly BindableProperty WelcomeMessageProperty =
        BindableProperty.Create(nameof(WelcomeMessage), typeof(string), typeof(AgentChatView),
            "Ask me anything, or tap a suggestion to get started.");

    public string WelcomeMessage
    {
        get => (string)GetValue(WelcomeMessageProperty);
        set => SetValue(WelcomeMessageProperty, value);
    }

    public static readonly BindableProperty WelcomeIconProperty =
        BindableProperty.Create(nameof(WelcomeIcon), typeof(string), typeof(AgentChatView), "💬");

    public string WelcomeIcon
    {
        get => (string)GetValue(WelcomeIconProperty);
        set => SetValue(WelcomeIconProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  AVATARS & IDENTITY
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(nameof(ShowAvatars), typeof(bool), typeof(AgentChatView), true,
            propertyChanged: (b, _, _) => (b as AgentChatView)?.RedecorateAllMessages());

    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(nameof(AvatarSize), typeof(double), typeof(AgentChatView), 28.0);

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(nameof(UserDisplayName), typeof(string), typeof(AgentChatView), "You");

    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(nameof(AssistantDisplayName), typeof(string), typeof(AgentChatView), "Assistant");

    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    public static readonly BindableProperty UserAvatarSourceProperty =
        BindableProperty.Create(nameof(UserAvatarSource), typeof(ImageSource), typeof(AgentChatView));

    public ImageSource? UserAvatarSource
    {
        get => (ImageSource?)GetValue(UserAvatarSourceProperty);
        set => SetValue(UserAvatarSourceProperty, value);
    }

    public static readonly BindableProperty AssistantAvatarSourceProperty =
        BindableProperty.Create(nameof(AssistantAvatarSource), typeof(ImageSource), typeof(AgentChatView));

    public ImageSource? AssistantAvatarSource
    {
        get => (ImageSource?)GetValue(AssistantAvatarSourceProperty);
        set => SetValue(AssistantAvatarSourceProperty, value);
    }

    public static readonly BindableProperty UserAvatarTextProperty =
        BindableProperty.Create(nameof(UserAvatarText), typeof(string), typeof(AgentChatView), "You",
            propertyChanged: (b, _, _) => (b as AgentChatView)?.RedecorateAllMessages());

    public string UserAvatarText
    {
        get => (string)GetValue(UserAvatarTextProperty);
        set => SetValue(UserAvatarTextProperty, value);
    }

    public static readonly BindableProperty AssistantAvatarTextProperty =
        BindableProperty.Create(nameof(AssistantAvatarText), typeof(string), typeof(AgentChatView), "AI",
            propertyChanged: (b, _, _) => (b as AgentChatView)?.RedecorateAllMessages());

    public string AssistantAvatarText
    {
        get => (string)GetValue(AssistantAvatarTextProperty);
        set => SetValue(AssistantAvatarTextProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  TIMESTAMPS & DISPLAY
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(nameof(ShowTimestamps), typeof(bool), typeof(AgentChatView), false,
            propertyChanged: (b, _, _) => (b as AgentChatView)?.RedecorateAllMessages());

    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    public static readonly BindableProperty ShowToolMessagesProperty =
        BindableProperty.Create(nameof(ShowToolMessages), typeof(bool), typeof(AgentChatView), true);

    public bool ShowToolMessages
    {
        get => (bool)GetValue(ShowToolMessagesProperty);
        set => SetValue(ShowToolMessagesProperty, value);
    }

    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(nameof(BubbleCornerRadius), typeof(double), typeof(AgentChatView), 12.0);

    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(nameof(BubbleStrokeThickness), typeof(double), typeof(AgentChatView), 0.0);

    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    public static readonly BindableProperty ShowReasoningProperty =
        BindableProperty.Create(nameof(ShowReasoning), typeof(bool), typeof(AgentChatView), true);

    public bool ShowReasoning
    {
        get => (bool)GetValue(ShowReasoningProperty);
        set => SetValue(ShowReasoningProperty, value);
    }

    public static readonly BindableProperty ShowNewChatButtonProperty =
        BindableProperty.Create(nameof(ShowNewChatButton), typeof(bool), typeof(AgentChatView), false);

    /// <summary>Whether to display a "New Chat" button that calls <see cref="ClearMessages"/>.</summary>
    public bool ShowNewChatButton
    {
        get => (bool)GetValue(ShowNewChatButtonProperty);
        set => SetValue(ShowNewChatButtonProperty, value);
    }

    public static readonly BindableProperty CustomContentTemplateSelectorProperty =
        BindableProperty.Create(nameof(CustomContentTemplateSelector), typeof(ContentTemplateSelector), typeof(AgentChatView));

    /// <summary>
    /// Optional <see cref="ContentTemplateSelector"/> for rendering rich inline content
    /// (weather cards, plan cards, etc.) based on <see cref="AIContent"/> type or function name.
    /// </summary>
    public ContentTemplateSelector? CustomContentTemplateSelector
    {
        get => (ContentTemplateSelector?)GetValue(CustomContentTemplateSelectorProperty);
        set => SetValue(CustomContentTemplateSelectorProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  LOCALIZABLE TEXT
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty SendButtonTextProperty =
        BindableProperty.Create(nameof(SendButtonText), typeof(string), typeof(AgentChatView), "Send",
            propertyChanged: (b, _, _) => (b as AgentChatView)?.SyncSendButtonState());

    public string SendButtonText
    {
        get => (string)GetValue(SendButtonTextProperty);
        set => SetValue(SendButtonTextProperty, value);
    }

    public static readonly BindableProperty StopButtonTextProperty =
        BindableProperty.Create(nameof(StopButtonText), typeof(string), typeof(AgentChatView), "Stop",
            propertyChanged: (b, _, _) => (b as AgentChatView)?.SyncSendButtonState());

    /// <summary>Text shown on the send button while streaming (acts as cancel).</summary>
    public string StopButtonText
    {
        get => (string)GetValue(StopButtonTextProperty);
        set => SetValue(StopButtonTextProperty, value);
    }

    public static readonly BindableProperty TypingIndicatorTextProperty =
        BindableProperty.Create(nameof(TypingIndicatorText), typeof(string), typeof(AgentChatView), "Thinking…");

    public string TypingIndicatorText
    {
        get => (string)GetValue(TypingIndicatorTextProperty);
        set => SetValue(TypingIndicatorTextProperty, value);
    }

    // ══════════════════════════════════════════════════════════════
    //  INTERNAL COMMANDS (for templates to bind to)
    // ══════════════════════════════════════════════════════════════

    public static readonly BindableProperty InternalSendCommandProperty =
        BindableProperty.Create(nameof(InternalSendCommand), typeof(ICommand), typeof(AgentChatView));

    public ICommand InternalSendCommand
    {
        get => (ICommand)GetValue(InternalSendCommandProperty);
        private set => SetValue(InternalSendCommandProperty, value);
    }

    public static readonly BindableProperty InternalSuggestionCommandProperty =
        BindableProperty.Create(nameof(InternalSuggestionCommand), typeof(ICommand), typeof(AgentChatView));

    public ICommand InternalSuggestionCommand
    {
        get => (ICommand)GetValue(InternalSuggestionCommandProperty);
        private set => SetValue(InternalSuggestionCommandProperty, value);
    }
}
