using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The message list: rows projected straight from <see cref="AgentContext.Turns"/>, like the upstream
/// <c>MessageList</c>. Each turn shows the user's bubble, one row per rendered response block, then a thinking
/// row, an error row, a not-sent marker, or a stopped marker, and the app's response footer.
/// </summary>
/// <remarks>
/// Engine notifications only mark the list dirty. One flush per dispatcher tick (about 50 ms) reconciles the
/// rows, re-selects templates for changed blocks, and refreshes each changed row once — so streaming N tokens
/// in a tick costs one refresh per visible row, not N. The list holds the only block subscriptions; row views
/// never subscribe to blocks themselves.
/// </remarks>
internal sealed class ChatMessageList : CollectionView
{
    // A backwards scroll is the user's when it is a drag: a few steps, each soon after the one before, adding up to more
    // than rounding. The platform's own corrections (a row measured after the list scrolled to it, a list at the end
    // clamping its offset after a row got shorter) arrive as single steps.
    private const int DragSteps = 2;
    private const double DragDistance = 4;
    private const long DragStepGapMilliseconds = 120;

    // How long after a row that can get shorter changes backwards scrolls are taken for the platform's.
    private const long SettleMilliseconds = 250;

    // Turns that failed. The engine commits nothing for a failed turn and forgets the failure once another message
    // is sent, so the view keeps it, for as long as the turn lives.
    private static readonly ConditionalWeakTable<ConversationTurn, object> FailedTurns = new();

    private readonly AIChatView _owner;
    private readonly ObservableCollection<ChatRow> _rows = [];
    private readonly Dictionary<ContentBlock, BlockRow> _blockRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ContentBlock, ContentBlockChangedSubscription> _blockSubscriptions = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ContentBlock> _changedBlocks = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ChatRow> _dirtyRows = [];
    private readonly Dictionary<ConversationTurn, UserMessageRow> _userRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ConversationTurn, TurnErrorRow> _errorRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ConversationTurn, TurnNotSentRow> _notSentRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ConversationTurn, TurnStoppedRow> _stoppedRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ConversationTurn, TurnFooterRow> _footerRows = new(ReferenceEqualityComparer.Instance);
    private readonly List<ChatRow> _lastTurnRows = [];
    private readonly ThinkingRow _thinkingRow = new();
    private readonly List<IDisposable> _sessionSubscriptions = [];
    private AgentContext? _session;
    private bool _isAttached;
    private bool _flushScheduled;
    private int _flushGeneration;
    private bool _isPinned = true;
    private bool _isUpdatingRows;
    private bool _statusChanged;
    private long _settleUntil;
    private long _lastBackwardAt;
    private int _backwardSteps;
    private double _backwardDistance;
    private double _awayDistance;
    private ConversationTurn? _lastTurn;
    private bool _followRequested;

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

    /// <summary>Gets or sets the clock, in milliseconds, that times how long rows take to settle. Replaceable for tests.</summary>
    internal Func<long> Clock { get; set; } = static () => Environment.TickCount64;

    internal IReadOnlyList<ChatRow> Rows => _rows;

    internal bool IsPinned => _isPinned;

    internal int SubscriptionCount => _sessionSubscriptions.Count + _blockSubscriptions.Count;

    /// <summary>Starts projecting <paramref name="session"/>, replacing any previous session.</summary>
    public void Attach(AgentContext? session)
    {
        Detach();
        _session = session;
        _isAttached = true;
        if (session is not null)
        {
            _sessionSubscriptions.Add(session.RegisterOnTurnAdded(_ => Invalidate()));
            _sessionSubscriptions.Add(session.RegisterOnStatusChanged(_ => OnStatusChanged()));
            _sessionSubscriptions.Add(session.RegisterOnBlockAdded((_, _) => Invalidate()));
            _sessionSubscriptions.Add(session.RegisterOnResponseBlocksCleared(OnResponseBlocksCleared));
        }

        // The session may have failed while nothing observed it.
        RememberFailedTurn();

        // Rows kept from before the list was detached missed every change made in the meantime.
        _dirtyRows.UnionWith(_rows);
        Flush();
    }

    /// <summary>Stops observing the session so nothing it owns references this list.</summary>
    public void Detach()
    {
        _isAttached = false;
        _session = null;
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

    /// <summary>
    /// Re-selects every block's template on the next flush (templates were added, removed, or edited). Rows whose
    /// template and its <see cref="BlockTemplate.Version"/> are unchanged keep their views.
    /// </summary>
    public void InvalidateTemplates() => Invalidate();

    /// <summary>Scrolls to the newest row and keeps following new content.</summary>
    public void PinToEnd()
    {
        Pin();
        ScrollToEnd();
    }

    /// <summary>Applies all pending changes now. Does nothing while detached, so nothing re-subscribes to the session.</summary>
    internal void Flush()
    {
        _flushScheduled = false;
        if (!_isAttached)
            return;

        bool structureChanged;
        var refreshed = false;
        var mayShrink = false;

        // Removing a row (the thinking row, a cleared response) shrinks the content, and a list scrolled to the end
        // reports the clamped offset as a scroll back through the history. Those scrolls are ours, not the user's.
        _isUpdatingRows = true;
        try
        {
            var desired = BuildRows();
            structureChanged = Apply(desired);

            foreach (var block in _changedBlocks)
            {
                if (_blockRows.TryGetValue(block, out var row))
                    _dirtyRows.Add(row);
            }
            _changedBlocks.Clear();

            // A turn's rows show its state (a call still running, an approval still open, the footer's usage):
            // refresh them when the conversation's status changes, since the blocks themselves did not.
            if (_statusChanged)
            {
                _statusChanged = false;
                _dirtyRows.UnionWith(_lastTurnRows);
            }

            foreach (var row in _dirtyRows)
            {
                if (_rows.Contains(row))
                {
                    row.NotifyChanged();
                    refreshed = true;

                    // Streaming text only grows; other rows (an approval or a tool card settling) can get shorter.
                    mayShrink |= row is not BlockRow { Block: RichContentBlock };
                }
            }
            _dirtyRows.Clear();
        }
        finally
        {
            _isUpdatingRows = false;
        }

        if (structureChanged || mayShrink)
            _settleUntil = Clock() + SettleMilliseconds;

        // An empty list has nothing to read back through, and a new turn (sent from the composer or by the app, or
        // restored) is what the user wants to see: follow the end again.
        var lastTurn = _session is { Turns.Count: > 0 } session ? session.Turns[^1] : null;
        if (_rows.Count == 0 || (lastTurn is not null && !ReferenceEquals(lastTurn, _lastTurn)))
            Pin();
        _lastTurn = lastTurn;

        if ((structureChanged || refreshed || _followRequested) && _isPinned)
            ScrollToEnd();
        _followRequested = false;
    }

    /// <summary>
    /// Keeps a list that follows the conversation at the end when content in <paramref name="element"/>'s row changed
    /// size on its own, after the row was laid out (an image that finished loading, or fell back to a chip).
    /// </summary>
    internal static void FollowResizedContent(Element element)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ChatMessageList list)
            {
                if (list._isPinned)
                {
                    list._followRequested = true;
                    list.Invalidate();
                }

                return;
            }
        }
    }

    private void Invalidate()
    {
        // The engine notifies on the thread that called it; the rows belong to the list's thread.
        if (this.GetDispatcherIfRequired() is { } owner)
        {
            owner.Dispatch(Invalidate);
            return;
        }

        if (_flushScheduled || !_isAttached)
            return;

        _flushScheduled = true;
        var dispatcher = Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread();
        if (dispatcher is null)
        {
            // No dispatcher at all (unit tests): apply synchronously.
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

    private void OnStatusChanged()
    {
        if (this.GetDispatcherIfRequired() is { } owner)
        {
            owner.Dispatch(OnStatusChanged);
            return;
        }

        // Remember a failure now: by the next flush another message may already have been sent.
        RememberFailedTurn();
        _statusChanged = true;
        Invalidate();
    }

    private void RememberFailedTurn()
    {
        if (_session is { Status: ConversationStatus.Error, Turns: [.., var failed] })
            FailedTurns.AddOrUpdate(failed, failed);
    }

    private void OnResponseBlocksCleared(ConversationTurn turn)
    {
        if (this.GetDispatcherIfRequired() is { } owner)
        {
            owner.Dispatch(() => OnResponseBlocksCleared(turn));
            return;
        }

        // A retry sends the turn again.
        FailedTurns.Remove(turn);
        Invalidate();
    }

    private void OnBlockChanged(ContentBlock block)
    {
        if (this.GetDispatcherIfRequired() is { } owner)
        {
            owner.Dispatch(() => OnBlockChanged(block));
            return;
        }

        _changedBlocks.Add(block);
        Invalidate();
    }

    private List<ChatRow> BuildRows()
    {
        var desired = new List<ChatRow>();
        var liveBlocks = new HashSet<ContentBlock>(ReferenceEqualityComparer.Instance);
        var liveTurns = new HashSet<ConversationTurn>(ReferenceEqualityComparer.Instance);
        _lastTurnRows.Clear();

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
                var start = desired.Count;
                liveTurns.Add(turn);

                AddRequestRows(desired, liveBlocks, session, turn, templates);

                ContentBlock? lastVisible = null;
                foreach (var block in turn.ResponseBlocks)
                {
                    Track(block, liveBlocks);
                    var template = BuiltInBlockTemplates.Select(block, templates, includeBuiltIns: true);
                    if (template is { CanRender: true } && !IsEmptyBuiltInReasoning(block, template))
                    {
                        desired.Add(GetBlockRow(block, template, session, turn));
                        lastVisible = block;
                    }
                }

                if (isLast && status == ConversationStatus.Streaming &&
                    lastVisible is not (RichContentBlock or ReasoningContentBlock or MediaContentBlock))
                {
                    desired.Add(_thinkingRow);
                }

                var failed = FailedTurns.TryGetValue(turn, out _);
                if (isLast && status == ConversationStatus.Error)
                {
                    failed = true;
                    desired.Add(GetRow(_errorRows, turn, () => new TurnErrorRow(turn, session)));
                }
                else if (failed)
                {
                    // Only the last turn can be retried; an earlier failed one stays out of the conversation.
                    desired.Add(GetRow(_notSentRows, turn, () => new TurnNotSentRow(turn)));
                }

                if (turn.IsStopped)
                    desired.Add(GetRow(_stoppedRows, turn, () => new TurnStoppedRow(turn)));

                var isComplete = !failed && (!isLast || status == ConversationStatus.Idle);
                if (footer is not null && isComplete && turn.ResponseBlocks.Count > 0)
                    desired.Add(GetFooterRow(turn, footer));

                if (isLast)
                    _lastTurnRows.AddRange(desired.Skip(start));
            }
        }

        Prune(liveBlocks, liveTurns);
        return desired;
    }

    /// <summary>
    /// Gets whether the built-in reasoning view would have nothing to show: reasoning without text, such as the
    /// encrypted reasoning some providers return in a block of its own. An app template still gets such blocks.
    /// </summary>
    private static bool IsEmptyBuiltInReasoning(ContentBlock block, BlockTemplate template) =>
        block is ReasoningContentBlock reasoning && template.Factory is not null && string.IsNullOrWhiteSpace(reasoning.Text);

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
                    desired.Add(GetBlockRow(block, template, session, turn));
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

    private BlockRow GetBlockRow(ContentBlock block, BlockTemplate template, AgentContext session, ConversationTurn turn)
    {
        if (_blockRows.TryGetValue(block, out var row) &&
            ReferenceEquals(row.Template, template) &&
            row.TemplateVersion == template.Version &&
            ReferenceEquals(row.Session, session))
        {
            return row;
        }

        row = new BlockRow(block, template, session, turn, _owner);
        _blockRows[block] = row;
        return row;
    }

    private TurnFooterRow GetFooterRow(ConversationTurn turn, DataTemplate footer)
    {
        if (!_footerRows.TryGetValue(turn, out var row) || !ReferenceEquals(row.Template, footer))
        {
            row = new TurnFooterRow(turn, footer);
            _footerRows[turn] = row;
        }

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
        PruneTurns(_notSentRows, liveTurns);
        PruneTurns(_stoppedRows, liveTurns);
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

    private void Pin()
    {
        _isPinned = true;
        _backwardSteps = 0;
        _backwardDistance = 0;
        _awayDistance = 0;
    }

    private void ScrollToEnd()
    {
        if (_rows.Count > 0)
            ScrollTo(_rows.Count - 1, position: ScrollToPosition.End, animate: false);
    }

    private void OnScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (_isUpdatingRows)
            return;

        var now = Clock();
        var newestVisible = e.LastVisibleItemIndex >= _rows.Count - 1;
        var delta = e.VerticalDelta;

        if (delta < 0)
        {
            // Right after a row that can get shorter changed (an approval collapsing to its decision), a list at the
            // end clamps its offset in the next layout pass, after the flush has finished. That is not the user.
            if (newestVisible && now < _settleUntil)
                return;

            if (now - _lastBackwardAt > DragStepGapMilliseconds)
            {
                _backwardSteps = 0;
                _backwardDistance = 0;
            }

            _lastBackwardAt = now;
            _backwardSteps++;
            _backwardDistance -= delta;

            if (!_isPinned)
            {
                _awayDistance -= delta;
            }
            else if ((_backwardSteps >= DragSteps && _backwardDistance >= DragDistance) || (delta < -1 && !newestVisible))
            {
                // A drag back, however slow, even while a tall answer keeps the newest row on screen; or the newest
                // row scrolled out of view. A single step with the newest row on screen is a layout correction.
                _isPinned = false;
                _awayDistance = _backwardDistance;
            }
        }
        else if (delta > 0 && !_isPinned)
        {
            // Following resumes once the user scrolls back down to where it stopped.
            _awayDistance -= delta;
            if (newestVisible && _awayDistance <= 1)
                _isPinned = true;
        }
    }

    private sealed class RowTemplateSelector : DataTemplateSelector
    {
        private static readonly DataTemplate Empty = new(static () => new ContentView());
        private readonly DataTemplate _user = new(static () => new UserMessageView());
        private readonly DataTemplate _thinking = new(static () => new ThinkingView());
        private readonly DataTemplate _error = new(static () => new TurnErrorView());
        private readonly DataTemplate _notSent = new(static () => new TurnNotSentView());
        private readonly DataTemplate _stopped = new(static () => new TurnStoppedView());
        private readonly Dictionary<DataTemplate, DataTemplate> _footers = [];

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
            item switch
            {
                BlockRow row => row.Template.GetDataTemplate(),
                UserMessageRow => _user,
                ThinkingRow => _thinking,
                TurnErrorRow => _error,
                TurnNotSentRow => _notSent,
                TurnStoppedRow => _stopped,
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
