using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The rows of a conversation as one flat list, which a <see cref="CollectionView"/> can virtualize: for each turn, its
/// request blocks, then its response blocks, and then the turn itself once it no longer responds.
/// </summary>
/// <remarks>
/// <para>
/// A list of turns that lays out each turn's blocks creates and measures every block of a turn, also off screen, and
/// measures the whole turn again whenever one of its blocks grows. With a row for each block, the list creates,
/// measures and recycles each block on its own. Use a <see cref="BlockTemplateSelector"/> as the list's item template:
/// its templates show the blocks, and its <see cref="BlockTemplateSelector.TurnTemplate"/> shows the turn's own row,
/// such as how the turn ended and the model and tokens that answered it.
/// </para>
/// <para>
/// The list follows the context's changes as the context raises them, and raises <see cref="CollectionChanged"/> once
/// for each row it adds or removes, with the row's index: a block's row when the block is added, and the turn's row once
/// <see cref="ConversationTurn.IsResponding"/> is <see langword="false"/>. A retry removes the rows of the failed
/// response, last first, and the turn's row, before the new response adds its own. A restore or a clear of the context
/// raises one <see cref="NotifyCollectionChangedAction.Reset"/>.
/// </para>
/// <para>
/// A handler of <see cref="CollectionChanged"/> may change the conversation, such as send the next message once a turn's
/// row is added. The list follows that change once every handler has seen the row, so every handler sees the changes in
/// order. Like the context, the list is single-thread-affine: it changes and raises its events on the thread that
/// changes the context.
/// </para>
/// <para>Dispose the list to stop following the context, such as when the view that shows it goes away.</para>
/// </remarks>
public sealed class ConversationItems : IReadOnlyList<object>, IList, INotifyCollectionChanged, IDisposable
{
    private static readonly NotifyCollectionChangedEventArgs s_reset = new(NotifyCollectionChangedAction.Reset);

    private readonly AgentContext _context;
    private readonly List<object> _items = [];
    private readonly List<TurnRows> _turns = [];
    private bool _raising;
    private bool _changedWhileRaising;
    private bool _turnsReplaced;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ConversationItems"/> class with the rows of the context's turns.</summary>
    /// <param name="context">The conversation to follow.</param>
    public ConversationItems(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        ((INotifyCollectionChanged)context.Turns).CollectionChanged += OnTurnsChanged;
        Load();
    }

    /// <summary>Occurs after a row was added or removed, or after the rows were replaced.</summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Gets the number of rows.</summary>
    public int Count => _items.Count;

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

    /// <summary>Gets the row at <paramref name="index"/>: a <see cref="ContentBlock"/>, or the <see cref="ConversationTurn"/> that ends its blocks.</summary>
    /// <param name="index">The index of the row.</param>
    public object this[int index] => _items[index];

    object? IList.this[int index]
    {
        get => _items[index];
        set => throw ReadOnly();
    }

    /// <summary>Returns an enumerator over the rows.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>Stops following the context. The rows stay as they are.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ((INotifyCollectionChanged)_context.Turns).CollectionChanged -= OnTurnsChanged;
        foreach (var rows in _turns)
            rows.Unsubscribe();
    }

    bool IList.Contains(object? value) => value is not null && _items.Contains(value);

    int IList.IndexOf(object? value) => value is null ? -1 : _items.IndexOf(value);

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    int IList.Add(object? value) => throw ReadOnly();

    void IList.Clear() => throw ReadOnly();

    void IList.Insert(int index, object? value) => throw ReadOnly();

    void IList.Remove(object? value) => throw ReadOnly();

    void IList.RemoveAt(int index) => throw ReadOnly();

    private static NotSupportedException ReadOnly() =>
        new("The rows follow the conversation, so they can't be changed directly.");

    private void OnTurnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A send adds its turn at the end; anything else, such as a restore or a clear, replaces the turns.
        if (e.Action != NotifyCollectionChangedAction.Add)
            _turnsReplaced = true;
        Sync();
    }

    /// <summary>
    /// Brings the rows in line with the conversation. A change made while a row's change is raised, by a handler of it,
    /// is followed once every handler has seen that row's change.
    /// </summary>
    private void Sync()
    {
        if (_raising)
        {
            _changedWhileRaising = true;
            return;
        }

        do
        {
            _changedWhileRaising = false;
            SyncRows();
        }
        while (_changedWhileRaising && !_disposed);
    }

    private void SyncRows()
    {
        if (_disposed)
            return;

        var turns = _context.Turns;
        if (_turnsReplaced || !StartsWithFollowedTurns(turns))
        {
            Reload();
            return;
        }

        var start = 0;
        for (var i = 0; i < turns.Count; i++)
        {
            if (i == _turns.Count)
            {
                var added = new TurnRows(this, turns[i]);
                _turns.Add(added);
                added.Subscribe();
            }

            // Each turn's rows start where the previous turn's end. A sync stops at the first row whose handlers changed
            // the conversation, and starts over.
            var rows = _turns[i];
            if (!SyncBlocks(rows, start, request: true)
                || !SyncBlocks(rows, start + rows.RequestCount, request: false)
                || !SyncEnd(rows, start + rows.RequestCount + rows.ResponseCount))
            {
                return;
            }

            start += rows.Count;
        }
    }

    private bool StartsWithFollowedTurns(IReadOnlyList<ConversationTurn> turns)
    {
        if (_turns.Count > turns.Count)
            return false;

        for (var i = 0; i < _turns.Count; i++)
        {
            if (!ReferenceEquals(_turns[i].Turn, turns[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Brings the rows of a turn's request or response blocks in line with the blocks: keeps the rows that still show
    /// the blocks in order, removes the rest, last first, and adds a row for each block after them. A block added at the
    /// end therefore adds just its row.
    /// </summary>
    /// <returns>Whether the sync can go on.</returns>
    private bool SyncBlocks(TurnRows rows, int start, bool request)
    {
        var blocks = request ? rows.Turn.RequestBlocks : rows.Turn.ResponseBlocks;
        var kept = 0;
        while (kept < rows.Shown(request) && kept < blocks.Count && ReferenceEquals(_items[start + kept], blocks[kept]))
            kept++;

        while (rows.Shown(request) > kept)
        {
            var shown = rows.Shown(request) - 1;
            rows.SetShown(request, shown);
            if (!RemoveRow(start + shown))
                return false;
        }

        while (rows.Shown(request) < blocks.Count)
        {
            var shown = rows.Shown(request);
            rows.SetShown(request, shown + 1);
            if (!InsertRow(start + shown, blocks[shown]))
                return false;
        }

        return true;
    }

    /// <summary>Adds the turn's own row once the turn no longer responds, after its blocks, and removes it when it responds again.</summary>
    /// <returns>Whether the sync can go on.</returns>
    private bool SyncEnd(TurnRows rows, int index)
    {
        var ended = !rows.Turn.IsResponding;
        if (ended == rows.HasEnd)
            return true;

        rows.HasEnd = ended;
        return ended ? InsertRow(index, rows.Turn) : RemoveRow(index);
    }

    /// <summary>Adds the rows of the context's turns without raising anything, and follows the turns.</summary>
    private void Load()
    {
        foreach (var turn in _context.Turns)
        {
            var rows = new TurnRows(this, turn);
            _turns.Add(rows);
            _items.AddRange(turn.RequestBlocks);
            rows.RequestCount = turn.RequestBlocks.Count;
            _items.AddRange(turn.ResponseBlocks);
            rows.ResponseCount = turn.ResponseBlocks.Count;
            if (!turn.IsResponding)
            {
                _items.Add(turn);
                rows.HasEnd = true;
            }

            rows.Subscribe();
        }
    }

    private void Reload()
    {
        _turnsReplaced = false;
        foreach (var rows in _turns)
            rows.Unsubscribe();
        _turns.Clear();
        _items.Clear();
        Load();
        Raise(s_reset);
    }

    private bool InsertRow(int index, object row)
    {
        _items.Insert(index, row);
        return Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, row, index));
    }

    private bool RemoveRow(int index)
    {
        var row = _items[index];
        _items.RemoveAt(index);
        return Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, row, index));
    }

    /// <summary>Raises a change of the rows, which are already in place.</summary>
    /// <returns>Whether the sync can go on: <see langword="false"/> when a handler changed the conversation or disposed the list.</returns>
    private bool Raise(NotifyCollectionChangedEventArgs e)
    {
        _raising = true;
        try
        {
            CollectionChanged?.Invoke(this, e);
        }
        finally
        {
            _raising = false;
        }

        return !_changedWhileRaising && !_disposed;
    }

    /// <summary>The rows of one turn: how many of its request and response blocks have a row, and whether it has its own row.</summary>
    private sealed class TurnRows(ConversationItems owner, ConversationTurn turn)
    {
        public ConversationTurn Turn { get; } = turn;

        public int RequestCount { get; set; }

        public int ResponseCount { get; set; }

        public bool HasEnd { get; set; }

        public int Count => RequestCount + ResponseCount + (HasEnd ? 1 : 0);

        public int Shown(bool request) => request ? RequestCount : ResponseCount;

        public void SetShown(bool request, int count)
        {
            if (request)
                RequestCount = count;
            else
                ResponseCount = count;
        }

        public void Subscribe()
        {
            ((INotifyCollectionChanged)Turn.RequestBlocks).CollectionChanged += OnBlocksChanged;
            ((INotifyCollectionChanged)Turn.ResponseBlocks).CollectionChanged += OnBlocksChanged;
            Turn.PropertyChanged += OnTurnChanged;
        }

        public void Unsubscribe()
        {
            ((INotifyCollectionChanged)Turn.RequestBlocks).CollectionChanged -= OnBlocksChanged;
            ((INotifyCollectionChanged)Turn.ResponseBlocks).CollectionChanged -= OnBlocksChanged;
            Turn.PropertyChanged -= OnTurnChanged;
        }

        private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e) => owner.Sync();

        // Any change of the turn can report that it responds again or no longer does: the engine sets
        // IsResponding with the turn's outcome and raises the outcome's changes, such as its error, first.
        private void OnTurnChanged(object? sender, PropertyChangedEventArgs e) => owner.Sync();
    }
}
