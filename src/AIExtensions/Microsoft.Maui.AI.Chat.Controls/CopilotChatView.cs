using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A drop-in AI chat control backed by <see cref="AgentContext"/>.
/// </summary>
/// <remarks>
/// The composer, empty state, suggestions, attachments, and virtualized message surface come from
/// <see cref="ChatView"/>. This type only adapts an AI session and keeps the AI-specific content
/// template and appearance aliases used by existing XAML.
/// </remarks>
[ContentProperty(nameof(ContentTemplates))]
public class CopilotChatView : ChatView
{
    /// <summary>Backing property for <see cref="Session"/>.</summary>
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(
            nameof(Session),
            typeof(AgentContext),
            typeof(CopilotChatView),
            default(AgentContext),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((CopilotChatView)bindable).OnSessionChanged(
                    (AgentContext?)oldValue,
                    (AgentContext?)newValue));

    /// <summary>Backing property for <see cref="UserDisplayName"/>.</summary>
    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(
            nameof(UserDisplayName),
            typeof(string),
            typeof(CopilotChatView),
            "You",
            propertyChanged: OnParticipantAliasChanged);

    /// <summary>Backing property for <see cref="AssistantDisplayName"/>.</summary>
    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(
            nameof(AssistantDisplayName),
            typeof(string),
            typeof(CopilotChatView),
            "Assistant",
            propertyChanged: OnParticipantAliasChanged);

    private AgentChatConversation? _agentConversation;

    /// <summary>Initializes a new AI chat view.</summary>
    public CopilotChatView()
    {
        SetDynamicResource(MessageListTemplateProperty, ChatThemeKeys.MessageListTemplate);
        InputAreaCornerRadius = 14;
        ShowBusyIndicator = false;
    }

    /// <summary>Gets or sets the AI session displayed and driven by the control.</summary>
    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Gets or sets the local user display name.</summary>
    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    /// <summary>Gets or sets the assistant display name.</summary>
    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    /// <inheritdoc />
    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is null)
        {
            ReleaseAgentConversation();
            return;
        }

        if (Application.Current is { } application)
            ChatThemeLoader.EnsureLoaded(application.Resources);
        if (_agentConversation is null && Session is not null)
            ReplaceAgentConversation(Session);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        if (MessageListTemplate is null &&
            Application.Current is { } application)
        {
            ChatThemeLoader.EnsureLoaded(application.Resources);
            SetDynamicResource(
                MessageListTemplateProperty,
                ChatThemeKeys.MessageListTemplate);
        }

        base.OnApplyTemplate();
    }

    internal void AttachMessageListPart(MessageListView? messageList)
    {
        if (messageList is not null)
            base.ApplyMessageListProperties(messageList);
    }

    internal new void AttachSuggestionsPart(Layout? suggestionsPart) =>
        base.AttachSuggestionsPart(suggestionsPart);

    internal void UpdateWelcomeVisibility()
    {
        // State is maintained by ChatView's conversation and suggestion subscriptions.
    }

    internal Task SendCurrentTextAsync() =>
        SendAsync();

    internal Task PickAttachmentsFromButtonAsync() =>
        PickAttachmentsAsync();

    internal ChatMessage? TakePendingMessage()
    {
        var draft = CreateDraft();
        if (draft.IsEmpty)
            return null;

        var contents = new List<AIContent>();
        if (draft.HasText)
            contents.Add(new TextContent(draft.Text));

        foreach (var attachment in draft.Attachments)
        {
            if (attachment is ChatAttachment agentAttachment)
            {
                contents.Add(agentAttachment.Content);
            }
            else if (attachment.Uri is { } uri)
            {
                contents.Add(new UriContent(uri, attachment.MediaType));
            }
            else
            {
                contents.Add(new DataContent(
                    attachment.Data,
                    attachment.MediaType)
                {
                    Name = attachment.FileName
                });
            }
        }

        Text = string.Empty;
        ClearAttachments();
        return new ChatMessage(ChatRole.User, contents);
    }

    private void OnSessionChanged(
        AgentContext? oldSession,
        AgentContext? newSession)
    {
        _ = oldSession;
        ReplaceAgentConversation(newSession);
    }

    private void ReplaceAgentConversation(AgentContext? session)
    {
        _agentConversation?.Dispose();
        _agentConversation = session is null
            ? null
            : new AgentChatConversation(session);
        _agentConversation?.UpdateParticipantNames(
            UserDisplayName,
            AssistantDisplayName);
        Conversation = _agentConversation;
    }

    private void ReleaseAgentConversation()
    {
        var conversation = _agentConversation;
        _agentConversation = null;
        conversation?.Dispose();
        if (ReferenceEquals(Conversation, conversation))
            Conversation = null;
    }

    private static void OnParticipantAliasChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        _ = oldValue;
        _ = newValue;
        var view = (CopilotChatView)bindable;
        view._agentConversation?.UpdateParticipantNames(
            view.UserDisplayName,
            view.AssistantDisplayName);
    }

}
