using System.Collections.ObjectModel;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The message list: rows projected straight from <see cref="AgentContext.Turns"/>, like the upstream
/// <c>MessageList</c>. Each turn shows the user's bubble, one row per rendered response block, then a thinking
/// row, an error row, or the app's response footer.
/// </summary>
/// <remarks>
/// Engine notifications only mark the list dirty. One flush per dispatcher tick (about 50 ms) reconciles the
/// rows, re-selects templates for changed blocks, and refreshes each changed row once — so streaming N tokens
/// in a tick costs one refresh per visible row, not N. The list holds the only block subscriptions; row views
/// never subscribe to blocks themselves.
/// </remarks>
internal sealed class ChatMessageList : CollectionView
{
    private readonly AIChatView _owner;
    private readonly ObservableCollection<ChatRow> _rows = [];
    private readonly Dictionary<ContentBlock, BlockRow> _blockRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ContentBlock, ContentBlockChangedSubscription> _blockSubscriptions = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ContentBlock> _changedBlocks = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ChatRow> _dirtyRows = [];
    private readonly Dictionary<ConversationTurn, UserMessageRow> _userRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ConversationTurn, TurnErrorRow> _errorRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ConversationTurn, TurnFooterRow> _footerRows = new(ReferenceEqualityComparer.Instance);
    private readonly ThinkingRow _thinkingRow = new();
    private readonly List<IDisposable> _sessionSubscriptions = [];
    private AgentContext? _session;
    private bool _flushScheduled;
    private int _flushGeneration;
    private bool _isPinned = true;

    public ChatMessageList(AIChatView owner)
    {
        _owner = owner;
        AutomationId = "AIChatMessages";
        SelectionMode = SelectionMode.None;
        ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems;
        ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 4 };
        ItemTemplate = new RowTemplateSelector();
        ItemsSource = _rows;
        Footer = new BoxView { HeightRequest = 8, Color = Colors.Transparent };
        Scrolled += OnScrolled;
    }

    /// <summary>Gets or sets how long engine notifications are batched before the rows refresh.</summary>
    internal TimeSpan FlushDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    internal IReadOnlyList<ChatRow> Rows => _rows;

    internal bool IsPinned => _isPinned;

    internal int SubscriptionCount => _sessionSubscriptions.Count + _blockSubscriptions.Count;

    /// <summary>Starts projecting <paramref name="session"/>, replacing any previous session.</summary>
    public void Attach(AgentContext? session)
    {
        Detach();
        _session = session;
        if (session is not null)
        {
            _sessionSubscriptions.Add(session.RegisterOnTurnAdded(_ => Invalidate()));
            _sessionSubscriptions.Add(session.RegisterOnStatusChanged(_ => Invalidate()));
            _sessionSubscriptions.Add(session.RegisterOnBlockAdded((_, _) => Invalidate()));
            _sessionSubscriptions.Add(session.RegisterOnResponseBlocksCleared(_ => Invalidate()));
        }

        Flush();
    }

    /// <summary>Stops observing the session so nothing it owns references this list.</summary>
    public void Detach()
    {
        _flushGeneration++;
        _flushScheduled = false;

        foreach (var subscription in _sessionSubscriptions)
            subscription.Dispose();
        _sessionSubscriptions.Clear();

        foreach (var subscription in _blockSubscriptions.Values)
            subscription.Dispose();
        _blockSubscriptions.Clear();
        _changedBlocks.Clear();
    }

    /// <summary>Re-selects every block's template on the next flush (templates were added, removed, or edited).</summary>
    public void InvalidateTemplates()
    {
        _blockRows.Clear();
        _footerRows.Clear();
        Invalidate();
    }

    /// <summary>Scrolls to the newest row and keeps following new content.</summary>
    public void PinToEnd()
    {
        _isPinned = true;
        ScrollToEnd();
    }

    /// <summary>Applies all pending changes now.</summary>
    internal void Flush()
    {
        _flushScheduled = false;

        var desired = BuildRows();
        var structureChanged = Apply(desired);

        foreach (var block in _changedBlocks)
        {
            if (_blockRows.TryGetValue(block, out var row))
                _dirtyRows.Add(row);
        }
        _changedBlocks.Clear();

        var refreshed = false;
        foreach (var row in _dirtyRows)
        {
            if (_rows.Contains(row))
            {
                row.NotifyChanged();
                refreshed = true;
            }
        }
        _dirtyRows.Clear();

        if ((structureChanged || refreshed) && _isPinned)
            ScrollToEnd();
    }

    private void Invalidate()
    {
        if (_flushScheduled)
            return;

        _flushScheduled = true;
        var dispatcher = Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread();
        if (dispatcher is null)
        {
            // No dispatcher (unit tests, or a background thread): apply synchronously.
            Flush();
            return;
        }

        var generation = _flushGeneration;
        dispatcher.DispatchDelayed(FlushDelay, () =>
        {
            if (generation == _flushGeneration && _flushScheduled)
                Flush();
        });
    }

    private void OnBlockChanged(ContentBlock block)
    {
        _changedBlocks.Add(block);
        Invalidate();
    }

    private List<ChatRow> BuildRows()
    {
        var desired = new List<ChatRow>();
        var liveBlocks = new HashSet<ContentBlock>(ReferenceEqualityComparer.Instance);
        var liveTurns = new HashSet<ConversationTurn>(ReferenceEqualityComparer.Instance);

        if (_session is { } session)
        {
            var templates = _owner.ContentTemplates;
            var footer = _owner.ResponseFooterTemplate;
            var status = session.Status;
            var turns = session.Turns;

            for (var index = 0; index < turns.Count; index++)
            {
                var turn = turns[index];
                var isLast = index == turns.Count - 1;
                liveTurns.Add(turn);

                AddRequestRows(desired, liveBlocks, session, turn, templates);

                ContentBlock? lastVisible = null;
                foreach (var block in turn.ResponseBlocks)
                {
                    Track(block, liveBlocks);
                    var template = BuiltInBlockTemplates.Select(block, templates, includeBuiltIns: true);
                    if (template is { CanRender: true })
                    {
                        desired.Add(GetBlockRow(block, template, session));
                        lastVisible = block;
                    }
                }

                if (isLast && status == ConversationStatus.Streaming &&
                    lastVisible is not (RichContentBlock or ReasoningContentBlock or MediaContentBlock))
                {
                    desired.Add(_thinkingRow);
                }

                if (isLast && status == ConversationStatus.Error)
                    desired.Add(GetRow(_errorRows, turn, () => new TurnErrorRow(turn, session)));

                var isComplete = !isLast || status == ConversationStatus.Idle;
                if (footer is not null && isComplete && turn.ResponseBlocks.Count > 0)
                    desired.Add(GetRow(_footerRows, turn, () => new TurnFooterRow(turn, footer)));
            }
        }

        Prune(liveBlocks, liveTurns);
        return desired;
    }

    private void AddRequestRows(
        List<ChatRow> desired,
        HashSet<ContentBlock> liveBlocks,
        AgentContext session,
        ConversationTurn turn,
        IList<BlockTemplate> templates)
    {
        UserMessageRow? userRow = null;
        List<ContentBlock>? userBlocks = null;

        foreach (var block in turn.RequestBlocks)
        {
            Track(block, liveBlocks);

            // App templates may claim request blocks; the built-ins never do (a continuation's tool results would
            // otherwise render twice).
            if (BuiltInBlockTemplates.Select(block, templates, includeBuiltIns: false) is { } template)
            {
                if (template.CanRender)
                    desired.Add(GetBlockRow(block, template, session));
                continue;
            }

            if (block.Role == ChatRole.User && block is RichContentBlock or MediaContentBlock)
            {
                if (userRow is null)
                {
                    userRow = GetRow(_userRows, turn, () => new UserMessageRow(turn));
                    userBlocks = [];
                    desired.Add(userRow);
                }

                userBlocks!.Add(block);
            }
        }

        if (userRow is not null && userRow.SetBlocks(userBlocks!))
            _dirtyRows.Add(userRow);
    }

    private void Track(ContentBlock block, HashSet<ContentBlock> liveBlocks)
    {
        liveBlocks.Add(block);
        if (!_blockSubscriptions.ContainsKey(block))
            _blockSubscriptions[block] = block.OnChanged(() => OnBlockChanged(block));

        // A user row shows every request block of its turn, so a change to one refreshes the bubble.
        if (_changedBlocks.Contains(block))
        {
            foreach (var row in _userRows.Values)
            {
                if (row.Blocks.Contains(block))
                    _dirtyRows.Add(row);
            }
        }
    }

    private BlockRow GetBlockRow(ContentBlock block, BlockTemplate template, AgentContext session)
    {
        if (_blockRows.TryGetValue(block, out var row) &&
            ReferenceEquals(row.Template, template) &&
            ReferenceEquals(row.Session, session))
        {
            return row;
        }

        row = new BlockRow(block, template, session, _owner);
        _blockRows[block] = row;
        return row;
    }

    private static TRow GetRow<TRow>(Dictionary<ConversationTurn, TRow> rows, ConversationTurn turn, Func<TRow> create)
        where TRow : ChatRow
    {
        if (!rows.TryGetValue(turn, out var row))
        {
            row = create();
            rows[turn] = row;
        }

        return row;
    }

    private void Prune(HashSet<ContentBlock> liveBlocks, HashSet<ConversationTurn> liveTurns)
    {
        foreach (var block in _blockSubscriptions.Keys.Where(block => !liveBlocks.Contains(block)).ToArray())
        {
            _blockSubscriptions[block].Dispose();
            _blockSubscriptions.Remove(block);
        }

        foreach (var block in _blockRows.Keys.Where(block => !liveBlocks.Contains(block)).ToArray())
            _blockRows.Remove(block);

        PruneTurns(_userRows, liveTurns);
        PruneTurns(_errorRows, liveTurns);
        PruneTurns(_footerRows, liveTurns);
    }

    private static void PruneTurns<TRow>(Dictionary<ConversationTurn, TRow> rows, HashSet<ConversationTurn> liveTurns)
    {
        foreach (var turn in rows.Keys.Where(turn => !liveTurns.Contains(turn)).ToArray())
            rows.Remove(turn);
    }

    /// <summary>Edits the visible rows into <paramref name="desired"/> with one contiguous replace.</summary>
    private bool Apply(List<ChatRow> desired)
    {
        var prefix = 0;
        while (prefix < _rows.Count && prefix < desired.Count && ReferenceEquals(_rows[prefix], desired[prefix]))
            prefix++;

        if (prefix == _rows.Count && prefix == desired.Count)
            return false;

        var suffix = 0;
        while (suffix < _rows.Count - prefix &&
               suffix < desired.Count - prefix &&
               ReferenceEquals(_rows[_rows.Count - 1 - suffix], desired[desired.Count - 1 - suffix]))
        {
            suffix++;
        }

        for (var index = _rows.Count - suffix - 1; index >= prefix; index--)
            _rows.RemoveAt(index);

        for (var index = prefix; index < desired.Count - suffix; index++)
            _rows.Insert(index, desired[index]);

        return true;
    }

    private void ScrollToEnd()
    {
        if (_rows.Count > 0)
            ScrollTo(_rows.Count - 1, position: ScrollToPosition.End, animate: false);
    }

    private void OnScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (e.VerticalDelta < -1)
            _isPinned = false;
        else if (e.LastVisibleItemIndex >= _rows.Count - 1)
            _isPinned = true;
    }

    private sealed class RowTemplateSelector : DataTemplateSelector
    {
        private static readonly DataTemplate Empty = new(static () => new ContentView());
        private readonly DataTemplate _user = new(static () => new UserMessageView());
        private readonly DataTemplate _thinking = new(static () => new ThinkingView());
        private readonly DataTemplate _error = new(static () => new TurnErrorView());
        private readonly Dictionary<DataTemplate, DataTemplate> _footers = [];

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
            item switch
            {
                BlockRow row => row.Template.GetDataTemplate(),
                UserMessageRow => _user,
                ThinkingRow => _thinking,
                TurnErrorRow => _error,
                TurnFooterRow row => GetFooter(row.Template),
                _ => Empty,
            };

        // One host template per footer template, so changing the footer never reuses hosts built for the old one.
        private DataTemplate GetFooter(DataTemplate source)
        {
            if (!_footers.TryGetValue(source, out var template))
            {
                template = new DataTemplate(static () => new TurnFooterHost());
                _footers[source] = template;
            }

            return template;
        }
    }
}
