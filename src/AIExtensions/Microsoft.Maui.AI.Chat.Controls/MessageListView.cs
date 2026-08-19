using System.Collections.Specialized;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Displays the content blocks of an <see cref="AgentContext"/> using the neutral,
/// virtualized chat-message projection.
/// </summary>
public class MessageListView : ChatMessagesView
{
    /// <summary>Backing property for <see cref="Session"/>.</summary>
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(
            nameof(Session),
            typeof(AgentContext),
            typeof(MessageListView),
            default(AgentContext),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((MessageListView)bindable).OnSessionChanged(
                    (AgentContext?)oldValue,
                    (AgentContext?)newValue));

    /// <summary>Backing property for <see cref="UserDisplayName"/>.</summary>
    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(
            nameof(UserDisplayName),
            typeof(string),
            typeof(MessageListView),
            "You",
            propertyChanged: OnParticipantAliasChanged);

    /// <summary>Backing property for <see cref="AssistantDisplayName"/>.</summary>
    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(
            nameof(AssistantDisplayName),
            typeof(string),
            typeof(MessageListView),
            "Assistant",
            propertyChanged: OnParticipantAliasChanged);

    private AgentChatConversation? _agentConversation;
    private readonly HashSet<ContentContext> _contexts = [];

    /// <summary>Initializes a new message list.</summary>
    public MessageListView()
    {
        ((INotifyCollectionChanged)Items).CollectionChanged +=
            OnItemsCollectionChanged;
    }

    /// <summary>Gets or sets the AI agent session to display.</summary>
    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Gets or sets the user display name.</summary>
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
    protected override ChatContentItem CreateItem(
        ConversationMessage message,
        MessageContent content,
        ChatConversation? conversation,
        ChatAppearance appearance)
    {
        var agentConversation = _agentConversation ?? conversation as AgentChatConversation;
        if (agentConversation is not null &&
            content is IAgentBlockContent blockContent)
        {
            return new ContentContext(
                agentConversation.Session,
                message,
                content,
                blockContent,
                agentConversation,
                appearance);
        }

        return base.CreateItem(message, content, conversation, appearance);
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

        if (_agentConversation is null && Session is not null)
            ReplaceAgentConversation(Session);
    }

    /// <inheritdoc />
    protected override ChatContentTemplateSelector CreateTemplateSelector()
    {
        var fallbacks = new ContentTemplate[]
        {
            new RichTextContentTemplate { Priority = 100 },
            new ToolApprovalTemplate(),
            new UIActionContentTemplate(),
            new ReasoningContentTemplate(),
            new ThinkingContentTemplate(),
            new ErrorContentTemplate()
        };

        var selector = base.CreateTemplateSelector();
        if (UseDefaultContentTemplates)
        {
            foreach (var fallback in fallbacks)
                selector.FallbackTemplates.Add(fallback);
        }
        return selector;
    }

    internal ChatContentTemplateSelector CreateAiTemplateSelector() =>
        CreateTemplateSelector();

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
        var list = (MessageListView)bindable;
        list._agentConversation?.UpdateParticipantNames(
            list.UserDisplayName,
            list.AssistantDisplayName);
    }

    private void OnItemsCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        _ = sender;

        if (e.OldItems is not null)
        {
            foreach (var context in e.OldItems.OfType<ContentContext>())
            {
                if (_contexts.Remove(context))
                    context.Dispose();
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var context in _contexts)
                context.Dispose();
            _contexts.Clear();
        }

        if (e.NewItems is not null)
        {
            foreach (var context in e.NewItems.OfType<ContentContext>())
                _contexts.Add(context);
        }
    }

}
