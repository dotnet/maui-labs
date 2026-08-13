using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Microsoft.Maui.Chat.Controls.Themes;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// Renders the messages of a <see cref="ChatConversation"/> as a flat, virtualized list — one row per
/// <see cref="MessageContent"/> — and nothing else. Use <see cref="ChatView"/> for a full chat surface.
/// </summary>
/// <remarks>
/// <para>
/// The default control template contains a single part, <c>PART_Messages</c>, a
/// <see cref="CollectionView"/> whose <c>ItemsUpdatingScrollMode</c> keeps the last item in view. Rows
/// are never nested lists, so virtualization keeps working no matter how long a conversation grows.
/// </para>
/// <para>
/// Content that changes in place never replaces a row: the item stays the same instance and only its
/// view updates. A single 50 ms coalescer collapses a burst of streaming updates into one refresh and at
/// most one pending scroll request. Structural changes (messages or content added, removed, or reset)
/// are applied immediately.
/// </para>
/// <para>
/// Auto-scrolling pauses as soon as the user scrolls away from the end of the list and resumes when they
/// return to it.
/// </para>
/// <para>
/// <b>Threading:</b> this control, like the models it renders, is single-thread affine. Assign
/// <see cref="Conversation"/> and mutate the conversation on the UI thread only.
/// </para>
/// </remarks>
[ContentProperty(nameof(ContentTemplates))]
public class ChatMessagesView : TemplatedView
{
    /// <summary>The name of the <see cref="CollectionView"/> part in the control template.</summary>
    public const string MessagesPartName = "PART_Messages";

    private static readonly TimeSpan CoalesceInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Backing property for <see cref="Conversation"/>.</summary>
    public static readonly BindableProperty ConversationProperty =
        BindableProperty.Create(
            nameof(Conversation),
            typeof(ChatConversation),
            typeof(ChatMessagesView),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((ChatMessagesView)bindable).OnConversationChanged(
                    oldValue as ChatConversation,
                    newValue as ChatConversation));

    /// <summary>Backing property for <see cref="ContentTemplates"/>.</summary>
    public static readonly BindableProperty ContentTemplatesProperty =
        BindableProperty.Create(
            nameof(ContentTemplates),
            typeof(IList<ChatContentTemplate>),
            typeof(ChatMessagesView),
            defaultValueCreator: static _ => new ObservableCollection<ChatContentTemplate>(),
            propertyChanged: static (bindable, oldValue, newValue) =>
            {
                var view = (ChatMessagesView)bindable;
                if (oldValue is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= view.OnContentTemplatesChanged;
                if (newValue is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += view.OnContentTemplatesChanged;
                view.RebuildTemplateSelector();
            });

    /// <summary>Backing property for <see cref="UseDefaultContentTemplates"/>.</summary>
    public static readonly BindableProperty UseDefaultContentTemplatesProperty =
        BindableProperty.Create(
            nameof(UseDefaultContentTemplates),
            typeof(bool),
            typeof(ChatMessagesView),
            true,
            propertyChanged: static (bindable, _, _) => ((ChatMessagesView)bindable).RebuildTemplateSelector());

    /// <summary>Backing property for <see cref="Appearance"/>.</summary>
    public static readonly BindableProperty AppearanceProperty =
        BindableProperty.Create(
            nameof(Appearance),
            typeof(ChatAppearance),
            typeof(ChatMessagesView),
            defaultValueCreator: static _ => new ChatAppearance(),
            propertyChanged: static (bindable, _, _) => ((ChatMessagesView)bindable).ApplyAppearance());

    /// <summary>Backing property for <see cref="AutoScrollToLatest"/>.</summary>
    public static readonly BindableProperty AutoScrollToLatestProperty =
        BindableProperty.Create(
            nameof(AutoScrollToLatest),
            typeof(bool),
            typeof(ChatMessagesView),
            true,
            propertyChanged: static (bindable, _, _) => ((ChatMessagesView)bindable).UpdateScrollMode());

    /// <summary>Backing property for <see cref="LoadEarlierThreshold"/>.</summary>
    public static readonly BindableProperty LoadEarlierThresholdProperty =
        BindableProperty.Create(nameof(LoadEarlierThreshold), typeof(int), typeof(ChatMessagesView), -1);

    /// <summary>Backing property for <see cref="LoadEarlierCommand"/>.</summary>
    public static readonly BindableProperty LoadEarlierCommandProperty =
        BindableProperty.Create(nameof(LoadEarlierCommand), typeof(ICommand), typeof(ChatMessagesView));

    private static readonly BindablePropertyKey ItemsPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(Items),
            typeof(ReadOnlyObservableCollection<ChatContentItem>),
            typeof(ChatMessagesView),
            null);

    /// <summary>Backing property for <see cref="Items"/>.</summary>
    public static readonly BindableProperty ItemsProperty = ItemsPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey ItemTemplateSelectorPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(ItemTemplateSelector),
            typeof(DataTemplateSelector),
            typeof(ChatMessagesView),
            null,
            defaultValueCreator: static bindable => ((ChatMessagesView)bindable).CreateTemplateSelector());

    /// <summary>Backing property for <see cref="ItemTemplateSelector"/>.</summary>
    public static readonly BindableProperty ItemTemplateSelectorProperty =
        ItemTemplateSelectorPropertyKey.BindableProperty;

    private readonly ObservableCollection<ChatContentItem> _items = [];
    private readonly Dictionary<MessageContent, ChatContentItem> _itemsByContent = [];
    private readonly List<MessageContent> _dirtyContents = [];
    private readonly List<Row> _rows = [];
    private readonly HashSet<MessageContent> _rowContents = [];

    private IDisposable? _conversationSubscription;
    private CollectionView? _messagesPart;
    private bool _refreshScheduled;
    private bool _autoScrollRequested;
    private bool _isAtTail = true;
    private bool _loadEarlierRaised;

    /// <summary>Creates the view and applies the default control template.</summary>
    public ChatMessagesView()
    {
        SetValue(ItemsPropertyKey, new ReadOnlyObservableCollection<ChatContentItem>(_items));

        if (ContentTemplates is INotifyCollectionChanged templates)
            templates.CollectionChanged += OnContentTemplatesChanged;

        SetDynamicResource(ControlTemplateProperty, ChatThemeKeys.ChatMessagesViewTemplate);
    }

    /// <summary>Raised when the user scrolled near the start of the list and earlier messages should be loaded.</summary>
    /// <remarks>Only raised when <see cref="LoadEarlierThreshold"/> is zero or greater.</remarks>
    public event EventHandler? LoadEarlierRequested;

    /// <summary>Gets or sets the conversation to render.</summary>
    public ChatConversation? Conversation
    {
        get => (ChatConversation?)GetValue(ConversationProperty);
        set => SetValue(ConversationProperty, value);
    }

    /// <summary>
    /// Gets or sets the consumer templates. Any match here outranks every built-in template, whatever its
    /// priority. This is the content property, so templates can be declared inline in XAML.
    /// </summary>
    public IList<ChatContentTemplate> ContentTemplates
    {
        get => (IList<ChatContentTemplate>)GetValue(ContentTemplatesProperty);
        set => SetValue(ContentTemplatesProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the built-in text, image, and file templates are used when no consumer
    /// template matches. Set to <see langword="false"/> for strict allow-list rendering.
    /// </summary>
    public bool UseDefaultContentTemplates
    {
        get => (bool)GetValue(UseDefaultContentTemplatesProperty);
        set => SetValue(UseDefaultContentTemplatesProperty, value);
    }

    /// <summary>Gets or sets the styling applied to every row. Never <see langword="null"/>.</summary>
    public ChatAppearance Appearance
    {
        get => (ChatAppearance)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the list follows the latest row. Following pauses while the user is scrolled
    /// away from the end and resumes when they return. Defaults to <see langword="true"/>.
    /// </summary>
    public bool AutoScrollToLatest
    {
        get => (bool)GetValue(AutoScrollToLatestProperty);
        set => SetValue(AutoScrollToLatestProperty, value);
    }

    /// <summary>
    /// Gets or sets how close to the start of the list the user must scroll before
    /// <see cref="LoadEarlierRequested"/> and <see cref="LoadEarlierCommand"/> fire. Negative disables the
    /// seam entirely, which is the default.
    /// </summary>
    public int LoadEarlierThreshold
    {
        get => (int)GetValue(LoadEarlierThresholdProperty);
        set => SetValue(LoadEarlierThresholdProperty, value);
    }

    /// <summary>Gets or sets the command invoked alongside <see cref="LoadEarlierRequested"/>.</summary>
    public ICommand? LoadEarlierCommand
    {
        get => (ICommand?)GetValue(LoadEarlierCommandProperty);
        set => SetValue(LoadEarlierCommandProperty, value);
    }

    /// <summary>Gets the projected rows, in order. Observable, so a custom template can bind to it.</summary>
    public ReadOnlyObservableCollection<ChatContentItem> Items =>
        (ReadOnlyObservableCollection<ChatContentItem>)GetValue(ItemsProperty);

    /// <summary>Gets the selector the list uses to pick a view for each row.</summary>
    public DataTemplateSelector? ItemTemplateSelector =>
        (DataTemplateSelector?)GetValue(ItemTemplateSelectorProperty);

    /// <inheritdoc />
    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent is not null)
            ChatControlsTheme.EnsureLoaded();
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_messagesPart is not null)
            _messagesPart.Scrolled -= OnPartScrolled;

        _messagesPart = FindPart<CollectionView>(MessagesPartName);

        if (_messagesPart is null)
            return;

        _messagesPart.Scrolled += OnPartScrolled;

        // A replacement template that does not bind the parts still works.
        if (_messagesPart.ItemsSource is null)
            _messagesPart.ItemsSource = Items;

        if (_messagesPart.ItemTemplate is null)
            _messagesPart.ItemTemplate = ItemTemplateSelector;

        UpdateScrollMode();
        RequestAutoScroll();
    }

    /// <summary>
    /// Finds a template part by name, tolerating templates that were built in code and therefore have no
    /// name scope. A replacement template is allowed to omit any part.
    /// </summary>
    /// <typeparam name="T">The expected part type.</typeparam>
    /// <param name="name">The part name.</param>
    /// <returns>The part, or <see langword="null"/> when the template does not provide it.</returns>
    protected T? FindPart<T>(string name)
        where T : Element
    {
        try
        {
            return GetTemplateChild(name) as T;
        }
        catch (InvalidOperationException)
        {
            // A control template created in code has no name scope, so it simply has no named parts.
            return null;
        }
    }

    /// <summary>Creates the selector for the current templates: consumer templates first, then the built-ins.</summary>
    /// <returns>The new selector.</returns>
    protected virtual ChatContentTemplateSelector CreateTemplateSelector()
    {
        var selector = new ChatContentTemplateSelector();

        foreach (var template in ContentTemplates)
        {
            if (template is not null)
                selector.Templates.Add(template);
        }

        if (UseDefaultContentTemplates)
        {
            selector.FallbackTemplates.Add(new ChatTextContentTemplate());
            selector.FallbackTemplates.Add(new ChatMediaContentTemplate());
            selector.FallbackTemplates.Add(new ChatFileContentTemplate());
        }

        return selector;
    }

    private void RebuildTemplateSelector()
    {
        var selector = CreateTemplateSelector();
        SetValue(ItemTemplateSelectorPropertyKey, selector);

        if (_messagesPart is not null)
            _messagesPart.ItemTemplate = selector;
    }

    private void OnContentTemplatesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildTemplateSelector();

    // ── Conversation ──

    private void OnConversationChanged(ChatConversation? oldConversation, ChatConversation? newConversation)
    {
        _conversationSubscription?.Dispose();
        _conversationSubscription = null;
        _isAtTail = true;

        if (oldConversation is not null)
            oldConversation.PropertyChanged -= OnConversationPropertyChanged;

        if (newConversation is not null)
        {
            newConversation.PropertyChanged += OnConversationPropertyChanged;
            _conversationSubscription = newConversation.Subscribe(OnConversationChange);
        }

        _dirtyContents.Clear();
        RebuildItems();
    }

    private void OnConversationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The local participant decides which rows are outgoing, so re-evaluate the grouping flags.
        if (e.PropertyName == ChatConversation.LocalParticipantProperty.PropertyName)
            ReconcileItems();
    }

    private void OnConversationChange(ChatConversationChange change)
    {
        switch (change.Kind)
        {
            case ChatConversationChangeKind.MessageAdded:
            case ChatConversationChangeKind.ContentAdded:
                ReconcileItems();
                RequestAutoScroll();
                break;

            case ChatConversationChangeKind.MessageRemoved:
            case ChatConversationChangeKind.ContentRemoved:
                ReconcileItems();
                break;

            case ChatConversationChangeKind.Reset:
                // A reset means "re-read everything", not "throw the rows away": reconciling keeps the
                // rows that survived, so surviving cells are not rebuilt.
                ReconcileItems();
                RequestAutoScroll();
                break;

            case ChatConversationChangeKind.MessageChanged:
                NotifyMessageUpdated(change.Message);
                break;

            case ChatConversationChangeKind.ContentChanged:
                MarkContentDirty(change.Content);
                break;

            case ChatConversationChangeKind.StatusChanged:
            default:
                break;
        }
    }

    private void RebuildItems()
    {
        _items.Clear();
        _itemsByContent.Clear();
        _loadEarlierRaised = false;

        ReconcileItems();
    }

    /// <summary>
    /// Brings the projected rows in line with the conversation, reusing every row whose message and
    /// content are unchanged so in-place edits never replace list items.
    /// </summary>
    private void ReconcileItems()
    {
        BuildRows();

        // Drop rows whose content is gone. Walking backwards keeps the indices valid.
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var content = _items[i].Content;
            if (_rowContents.Contains(content))
                continue;

            _items.RemoveAt(i);
            _itemsByContent.Remove(content);
        }

        var appearance = Appearance;

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];

            if (i < _items.Count && Matches(_items[i], row))
            {
                // Already in the right place.
            }
            else if (_itemsByContent.TryGetValue(row.Content, out var existing) && Matches(existing, row))
            {
                var from = _items.IndexOf(existing);
                if (from >= 0 && from != i)
                    _items.Move(from, i);
                else if (from < 0)
                    _items.Insert(i, existing);
            }
            else
            {
                // Either the row is new, or its content moved to a different message and the old row
                // would report the wrong participant and timestamp.
                if (existing is not null)
                {
                    var stale = _items.IndexOf(existing);
                    if (stale >= 0)
                        _items.RemoveAt(stale);
                }

                var created = CreateItem(
                    row.Message,
                    row.Content,
                    Conversation,
                    appearance);
                _itemsByContent[row.Content] = created;
                _items.Insert(i, created);
            }

            var item = _items[i];
            item.Appearance = appearance;
            item.UpdateFlags(
                ChatContentItem.IsOutgoingFor(Conversation, row.Message.Participant),
                row.IsFirstInMessage,
                row.IsLastInMessage,
                row.IsFirstFromParticipant,
                row.IsLastFromParticipant);
        }

        while (_items.Count > _rows.Count)
        {
            var index = _items.Count - 1;
            _itemsByContent.Remove(_items[index].Content);
            _items.RemoveAt(index);
        }

        _loadEarlierRaised = false;
    }

    /// <summary>
    /// Creates one projected row. Derived provider controls can return a richer context type while
    /// retaining the neutral virtualization and update pipeline.
    /// </summary>
    /// <param name="message">The message that owns the content.</param>
    /// <param name="content">The content to render.</param>
    /// <param name="conversation">The current conversation.</param>
    /// <param name="appearance">The shared appearance.</param>
    /// <returns>The projected row.</returns>
    protected virtual ChatContentItem CreateItem(
        ConversationMessage message,
        MessageContent content,
        ChatConversation? conversation,
        ChatAppearance appearance) =>
        new(message, content, conversation, appearance);

    /// <summary>Flattens the conversation into rows and precomputes every grouping flag in two passes.</summary>
    private void BuildRows()
    {
        _rows.Clear();
        _rowContents.Clear();

        var messages = Conversation?.Messages;
        if (messages is null)
            return;

        foreach (var message in messages)
        {
            var contents = message.Contents;
            for (var j = 0; j < contents.Count; j++)
            {
                var content = contents[j];

                // The same instance twice would make row identity ambiguous; render it once.
                if (!_rowContents.Add(content))
                    continue;

                _rows.Add(new Row(message, content, j == 0, j == contents.Count - 1));
            }
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];

            var firstFrom = row.IsFirstInMessage
                && (i == 0 || !SameParticipant(_rows[i - 1].Message, row.Message));
            var lastFrom = row.IsLastInMessage
                && (i == _rows.Count - 1 || !SameParticipant(_rows[i + 1].Message, row.Message));

            _rows[i] = row with { IsFirstFromParticipant = firstFrom, IsLastFromParticipant = lastFrom };
        }
    }

    private static bool SameParticipant(ConversationMessage left, ConversationMessage right) =>
        ReferenceEquals(left.Participant, right.Participant)
        || string.Equals(left.Participant.Id, right.Participant.Id, StringComparison.Ordinal);

    private static bool Matches(ChatContentItem item, Row row) =>
        ReferenceEquals(item.Content, row.Content) && ReferenceEquals(item.Message, row.Message);

    private void NotifyMessageUpdated(ConversationMessage? message)
    {
        if (message is null)
            return;

        foreach (var content in message.Contents)
        {
            if (_itemsByContent.TryGetValue(content, out var item))
                item.NotifyMessageUpdated();
        }
    }

    private void ApplyAppearance()
    {
        var appearance = Appearance;
        foreach (var item in _items)
            item.Appearance = appearance;
    }

    // ── Coalescing ──

    /// <summary>Marks content as changed and schedules the single coalesced refresh.</summary>
    private void MarkContentDirty(MessageContent? content)
    {
        if (content is null)
            return;

        if (!_dirtyContents.Contains(content))
            _dirtyContents.Add(content);

        RequestAutoScroll();
    }

    private void ScheduleRefresh()
    {
        if (_refreshScheduled)
            return;

        var dispatcher = GetDispatcher();
        if (dispatcher is null)
        {
            // Without a realized list there is nothing to coalesce for, so apply immediately.
            FlushPending();
            return;
        }

        _refreshScheduled = true;
        dispatcher.DispatchDelayed(CoalesceInterval, FlushPending);
    }

    /// <summary>
    /// Records that the list should follow the tail. The request is flushed through the same coalescer
    /// as content refreshes, so a burst of additions scrolls once.
    /// </summary>
    private void RequestAutoScroll()
    {
        _autoScrollRequested = true;
        ScheduleRefresh();
    }

    private void FlushPending()
    {
        _refreshScheduled = false;

        if (_dirtyContents.Count > 0)
        {
            foreach (var content in _dirtyContents)
            {
                if (_itemsByContent.TryGetValue(content, out var item))
                    item.NotifyContentUpdated();
            }

            _dirtyContents.Clear();
        }

        if (!_autoScrollRequested)
            return;

        _autoScrollRequested = false;
        ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        if (!AutoScrollToLatest || !_isAtTail || _messagesPart is null || _items.Count == 0)
            return;

        _messagesPart.ScrollTo(_items.Count - 1, position: ScrollToPosition.End, animate: false);
    }

    private void UpdateScrollMode()
    {
        if (_messagesPart is null)
            return;

        _messagesPart.ItemsUpdatingScrollMode = AutoScrollToLatest && _isAtTail
            ? ItemsUpdatingScrollMode.KeepLastItemInView
            : ItemsUpdatingScrollMode.KeepItemsInView;
    }

    private void OnPartScrolled(object? sender, ItemsViewScrolledEventArgs e) =>
        OnScrolled(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);

    /// <summary>
    /// Applies a scroll position: pauses or resumes auto-scrolling and raises the load-earlier seam.
    /// Exposed for the template part and for tests, which have no real scrolling surface.
    /// </summary>
    internal void OnScrolled(int firstVisibleIndex, int lastVisibleIndex)
    {
        var count = _items.Count;

        _isAtTail = count == 0 || lastVisibleIndex < 0 || lastVisibleIndex >= count - 1;
        UpdateScrollMode();

        var threshold = LoadEarlierThreshold;
        if (threshold < 0 || count == 0 || firstVisibleIndex < 0 || firstVisibleIndex > threshold)
            return;

        if (_loadEarlierRaised)
            return;

        _loadEarlierRaised = true;
        LoadEarlierRequested?.Invoke(this, EventArgs.Empty);

        var command = LoadEarlierCommand;
        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    private IDispatcher? GetDispatcher() =>
        // Only a realized list needs coalescing, and a realized list always has a dispatcher.
        _messagesPart is null ? null : Application.Current?.Dispatcher ?? Dispatcher;

    private readonly record struct Row(
        ConversationMessage Message,
        MessageContent Content,
        bool IsFirstInMessage,
        bool IsLastInMessage,
        bool IsFirstFromParticipant = false,
        bool IsLastFromParticipant = false);
}
