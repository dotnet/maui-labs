using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// The model the chat controls render: its <see cref="Participants"/>, ordered <see cref="Messages"/>,
/// <see cref="Status"/>, and the seam that sends a <see cref="ChatDraft"/>.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this class to back a conversation with any transport, or use
/// <see cref="ObservableChatConversation"/> for the in-memory case. Mutating the protected
/// <see cref="MessageList"/>, a message's <see cref="ConversationMessage.Contents"/>, or content itself
/// automatically publishes an ordered <see cref="ChatConversationChange"/> to every subscriber, so a
/// subclass never has to remember to raise anything.
/// </para>
/// <para>
/// <b>Threading:</b> a conversation and everything reachable from it is single-thread affine and not
/// thread-safe. Create it on the UI thread and mutate it only from the UI thread. Subscribers are
/// invoked synchronously, in subscription order, on the mutating thread — there is no marshalling,
/// no queueing, and no coalescing at this layer.
/// </para>
/// </remarks>
public abstract class ChatConversation : BindableObject
{
    private static readonly BindablePropertyKey StatusPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(Status),
            typeof(ChatConversationStatus),
            typeof(ChatConversation),
            ChatConversationStatus.Idle,
            propertyChanged: static (bindable, _, newValue) =>
                ((ChatConversation)bindable).RaiseChange(
                    ChatConversationChange.StatusChanged((ChatConversationStatus)newValue!)));

    /// <summary>Backing property for <see cref="Status"/>.</summary>
    public static readonly BindableProperty StatusProperty = StatusPropertyKey.BindableProperty;

    /// <summary>Backing property for <see cref="LocalParticipant"/>.</summary>
    public static readonly BindableProperty LocalParticipantProperty =
        BindableProperty.Create(nameof(LocalParticipant), typeof(ChatParticipant), typeof(ChatConversation));

    private readonly ObservableCollection<ConversationMessage> _messages = [];
    private readonly ReadOnlyObservableCollection<ConversationMessage> _readOnlyMessages;
    private readonly HashSet<ConversationMessage> _attachedMessages = [];
    private readonly Dictionary<ObservableCollection<MessageContent>, ConversationMessage> _contentsOwners = [];
    private readonly Dictionary<MessageContent, ConversationMessage> _contentOwners = [];

    // Copy-on-write so a subscriber can dispose itself (or subscribe) while a change is being
    // delivered without invalidating the in-flight iteration and without allocating per change.
    private Subscription[] _subscriptions = [];

    /// <summary>Initializes the conversation and starts observing its own collections.</summary>
    protected ChatConversation()
    {
        _readOnlyMessages = new ReadOnlyObservableCollection<ConversationMessage>(_messages);
        _messages.CollectionChanged += OnMessagesChanged;
    }

    /// <summary>Gets the participants, in display order. Mutable so a host can add or remove members.</summary>
    public ObservableCollection<ChatParticipant> Participants { get; } = [];

    /// <summary>Gets the participants currently composing. Mutable; the controls simply render the list.</summary>
    public ObservableCollection<ChatParticipant> TypingParticipants { get; } = [];

    /// <summary>Gets the ordered messages. Mutate them through the owning subclass.</summary>
    public ReadOnlyObservableCollection<ConversationMessage> Messages => _readOnlyMessages;

    /// <summary>Gets the mutable message list. Every mutation publishes a <see cref="ChatConversationChange"/>.</summary>
    protected ObservableCollection<ConversationMessage> MessageList => _messages;

    /// <summary>Gets or sets the participant that represents this device. Their messages render as outgoing.</summary>
    public ChatParticipant? LocalParticipant
    {
        get => (ChatParticipant?)GetValue(LocalParticipantProperty);
        set => SetValue(LocalParticipantProperty, value);
    }

    /// <summary>Gets the conversation state. Setting it publishes <see cref="ChatConversationChangeKind.StatusChanged"/>.</summary>
    public ChatConversationStatus Status
    {
        get => (ChatConversationStatus)GetValue(StatusProperty);
        protected set => SetValue(StatusPropertyKey, value);
    }

    /// <summary>
    /// Subscribes to ordered change notifications. Dispose the result to unsubscribe; disposing during a
    /// notification takes effect immediately, so the subscriber is not called again.
    /// </summary>
    /// <param name="onChange">Invoked synchronously for every change, in subscription order.</param>
    /// <returns>A handle that unsubscribes when disposed. Disposing twice is safe.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onChange"/> is <see langword="null"/>.</exception>
    public IDisposable Subscribe(Action<ChatConversationChange> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        var subscription = new Subscription(this, onChange);
        _subscriptions = [.. _subscriptions, subscription];
        return subscription;
    }

    /// <summary>Publishes a change to every active subscriber, synchronously and in subscription order.</summary>
    /// <param name="change">The change to publish.</param>
    protected void RaiseChange(ChatConversationChange change)
    {
        var subscriptions = _subscriptions;
        for (var i = 0; i < subscriptions.Length; i++)
        {
            var subscription = subscriptions[i];
            if (subscription.IsActive)
                subscription.Callback(change);
        }
    }

    /// <summary>Gets whether <paramref name="draft"/> can be sent right now.</summary>
    /// <param name="draft">The draft the composer would send.</param>
    /// <returns><see langword="true"/> when the draft has content and the conversation is not busy.</returns>
    public virtual bool CanSend(ChatDraft? draft) =>
        draft is not null && !draft.IsEmpty && Status != ChatConversationStatus.Busy;

    /// <summary>
    /// Sends a draft. Returns whether the draft was accepted, which is what tells a composer to clear
    /// its input.
    /// </summary>
    /// <param name="draft">The draft to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns><see langword="true"/> when the draft was accepted; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="draft"/> is <see langword="null"/>.</exception>
    public Task<bool> SendAsync(ChatDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return CanSend(draft)
            ? SendCoreAsync(draft, cancellationToken)
            : Task.FromResult(false);
    }

    /// <summary>
    /// Performs the send. Called only after <see cref="CanSend"/> approved the draft, on the calling
    /// (UI) thread.
    /// </summary>
    /// <param name="draft">The draft to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns><see langword="true"/> when the draft was accepted; otherwise <see langword="false"/>.</returns>
    protected abstract Task<bool> SendCoreAsync(ChatDraft draft, CancellationToken cancellationToken);

    // ── Automatic change publication ──

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                RaiseAdded(e.NewItems, e.NewStartingIndex);
                break;

            case NotifyCollectionChangedAction.Remove:
                RaiseRemoved(e.OldItems, e.OldStartingIndex);
                break;

            case NotifyCollectionChangedAction.Replace:
                RaiseRemoved(e.OldItems, e.OldStartingIndex);
                RaiseAdded(e.NewItems, e.NewStartingIndex);
                break;

            default:
                // Move and Reset both invalidate positions; re-sync hooks and let subscribers re-read.
                ResyncMessageHooks();
                RaiseChange(ChatConversationChange.Reset());
                break;
        }

        void RaiseAdded(System.Collections.IList? items, int startIndex)
        {
            if (items is null)
                return;

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not ConversationMessage message)
                    continue;

                AttachMessage(message);
                RaiseChange(ChatConversationChange.MessageAdded(message, startIndex < 0 ? -1 : startIndex + i));
            }
        }

        void RaiseRemoved(System.Collections.IList? items, int startIndex)
        {
            if (items is null)
                return;

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not ConversationMessage message)
                    continue;

                DetachMessage(message);
                RaiseChange(ChatConversationChange.MessageRemoved(message, startIndex < 0 ? -1 : startIndex + i));
            }
        }
    }

    private void ResyncMessageHooks()
    {
        foreach (var message in _attachedMessages.ToArray())
        {
            if (!_messages.Contains(message))
                DetachMessage(message);
        }

        foreach (var message in _messages)
            AttachMessage(message);
    }

    private void AttachMessage(ConversationMessage message)
    {
        if (!_attachedMessages.Add(message))
            return;

        message.PropertyChanged += OnMessagePropertyChanged;
        message.Contents.CollectionChanged += OnMessageContentsChanged;
        _contentsOwners[message.Contents] = message;

        foreach (var content in message.Contents)
            AttachContent(message, content);
    }

    private void DetachMessage(ConversationMessage message)
    {
        if (!_attachedMessages.Remove(message))
            return;

        message.PropertyChanged -= OnMessagePropertyChanged;
        message.Contents.CollectionChanged -= OnMessageContentsChanged;
        _contentsOwners.Remove(message.Contents);

        foreach (var content in message.Contents)
            DetachContent(content);
    }

    private void AttachContent(ConversationMessage message, MessageContent content)
    {
        if (!_contentOwners.TryAdd(content, message))
            return;

        content.ContentChanged += OnContentChanged;
    }

    private void DetachContent(MessageContent content)
    {
        if (!_contentOwners.Remove(content))
            return;

        content.ContentChanged -= OnContentChanged;
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ConversationMessage message)
            RaiseChange(ChatConversationChange.MessageChanged(message));
    }

    private void OnMessageContentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is not ObservableCollection<MessageContent> contents ||
            !_contentsOwners.TryGetValue(contents, out var message))
        {
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                RaiseAdded(e.NewItems, e.NewStartingIndex);
                break;

            case NotifyCollectionChangedAction.Remove:
                RaiseRemoved(e.OldItems, e.OldStartingIndex);
                break;

            case NotifyCollectionChangedAction.Replace:
                RaiseRemoved(e.OldItems, e.OldStartingIndex);
                RaiseAdded(e.NewItems, e.NewStartingIndex);
                break;

            default:
                ResyncContentHooks(message, contents);
                RaiseChange(ChatConversationChange.MessageChanged(message));
                break;
        }

        void RaiseAdded(System.Collections.IList? items, int startIndex)
        {
            if (items is null)
                return;

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not MessageContent content)
                    continue;

                AttachContent(message, content);
                RaiseChange(ChatConversationChange.ContentAdded(message, content, startIndex < 0 ? -1 : startIndex + i));
            }
        }

        void RaiseRemoved(System.Collections.IList? items, int startIndex)
        {
            if (items is null)
                return;

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not MessageContent content)
                    continue;

                DetachContent(content);
                RaiseChange(ChatConversationChange.ContentRemoved(message, content, startIndex < 0 ? -1 : startIndex + i));
            }
        }
    }

    private void ResyncContentHooks(ConversationMessage message, ObservableCollection<MessageContent> contents)
    {
        foreach (var pair in _contentOwners.ToArray())
        {
            if (pair.Value == message && !contents.Contains(pair.Key))
                DetachContent(pair.Key);
        }

        foreach (var content in contents)
            AttachContent(message, content);
    }

    private void OnContentChanged(object? sender, EventArgs e)
    {
        if (sender is MessageContent content && _contentOwners.TryGetValue(content, out var message))
            RaiseChange(ChatConversationChange.ContentChanged(message, content));
    }

    private sealed class Subscription(ChatConversation owner, Action<ChatConversationChange> callback) : IDisposable
    {
        public Action<ChatConversationChange> Callback { get; } = callback;

        public bool IsActive { get; private set; } = true;

        public void Dispose()
        {
            if (!IsActive)
                return;

            IsActive = false;
            owner.Remove(this);
        }
    }

    private void Remove(Subscription subscription)
    {
        var current = _subscriptions;
        var index = Array.IndexOf(current, subscription);
        if (index < 0)
            return;

        var updated = new Subscription[current.Length - 1];
        Array.Copy(current, updated, index);
        Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
        _subscriptions = updated;
    }
}
