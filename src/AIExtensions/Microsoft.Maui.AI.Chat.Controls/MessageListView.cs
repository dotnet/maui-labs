using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A drop-in view that renders the messages of an <see cref="AgentContext"/> and nothing else — no
/// header, welcome panel, suggestions, or input box. It is the message list extracted from
/// <see cref="CopilotChatView"/>, which now hosts one internally.
/// </summary>
/// <remarks>
/// Bind <see cref="Session"/> to an <see cref="AgentContext"/> and add <see cref="ContentTemplate"/>s to
/// <see cref="ContentTemplates"/> to control how each <see cref="ContentBlock"/> renders. Because it is
/// session-driven it updates live as blocks stream in. Use it directly for a minimal chat surface, or to
/// compare template sets side by side (e.g. a fully templated <see cref="CopilotChatView"/> next to a
/// bare <see cref="MessageListView"/> that uses only the default templates). The single template part is
/// <c>PART_Messages</c> (a <see cref="CollectionView"/>).
/// </remarks>
[ContentProperty(nameof(ContentTemplates))]
public partial class MessageListView : TemplatedView
{
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(
            nameof(Session),
            typeof(AgentContext),
            typeof(MessageListView),
            propertyChanged: OnSessionChanged);

    public static readonly BindableProperty ContentTemplatesProperty =
        BindableProperty.Create(
            nameof(ContentTemplates),
            typeof(IList<ContentTemplate>),
            typeof(MessageListView),
            defaultValueCreator: _ => new ObservableCollection<ContentTemplate>(),
            propertyChanged: (b, o, n) =>
            {
                var self = (MessageListView)b;
                if (o is INotifyCollectionChanged oldNcc)
                    oldNcc.CollectionChanged -= self.OnContentTemplatesChanged;
                if (n is INotifyCollectionChanged newNcc)
                    newNcc.CollectionChanged += self.OnContentTemplatesChanged;
                self.RebuildTemplateSelector();
            });

    private static readonly BindablePropertyKey ItemsPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(Items),
            typeof(ReadOnlyObservableCollection<ContentContext>),
            typeof(MessageListView),
            null);

    public static readonly BindableProperty ItemsProperty = ItemsPropertyKey.BindableProperty;

    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>The content templates used to render each block; the highest matching priority wins.</summary>
    public IList<ContentTemplate> ContentTemplates
    {
        get => (IList<ContentTemplate>)GetValue(ContentTemplatesProperty);
        set => SetValue(ContentTemplatesProperty, value);
    }

    /// <summary>
    /// The rendered items, in order (including any transient thinking/error items). This is an observable,
    /// read-only collection — a container can bind its <c>ItemsSource</c> to it and track live changes.
    /// </summary>
    public ReadOnlyObservableCollection<ContentContext> Items =>
        (ReadOnlyObservableCollection<ContentContext>)GetValue(ItemsProperty);

    private readonly ObservableCollection<ContentContext> _items = [];

    private CollectionView? _messagesPart;

    private IDisposable? _turnAddedReg;
    private IDisposable? _statusChangedReg;
    private IDisposable? _blockAddedReg;
    private readonly List<IDisposable> _blockSubscriptions = [];

    // Streaming coalescing: a block can raise a change per token. Applying each one immediately
    // replaces the CollectionView item (recreating the whole cell) and rebuilds custom views from
    // scratch, which stalls the UI during a burst. Instead we mark the block dirty and flush the
    // batch at most once per interval, so N token updates collapse into a few refreshes.
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(50);
    private readonly List<ContentBlock> _dirtyBlocks = [];
    private bool _flushScheduled;

    // Purely visual, UI-only items — never part of the engine's turns or the message thread.
    private ContentContext? _thinkingItem;   // transient tail while streaming
    private Exception? _shownError;           // dedupe: the error currently rendered

    public MessageListView()
    {
        SetValue(ItemsPropertyKey, new ReadOnlyObservableCollection<ContentContext>(_items));

        if (ContentTemplates is INotifyCollectionChanged ncc)
            ncc.CollectionChanged += OnContentTemplatesChanged;

        SetDynamicResource(ControlTemplateProperty, Themes.ChatThemeKeys.MessageListViewTemplate);
    }

    private void OnContentTemplatesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildTemplateSelector();

    protected override void OnParentSet()
    {
        base.OnParentSet();

        // Ensure the ChatTheme resources (which provide the default ControlTemplate
        // and item templates) are merged when the control joins the visual tree.
        if (Parent is not null && Application.Current is { } app)
            ChatThemeLoader.EnsureLoaded(app.Resources);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _messagesPart = GetTemplateChild("PART_Messages") as CollectionView;

        if (_messagesPart is not null)
        {
            _messagesPart.ItemsSource = _items;
            RebuildTemplateSelector();
        }

        RebuildFromSession();
    }

    private void RebuildTemplateSelector()
    {
        if (_messagesPart is null)
            return;

        var selector = new ContentTemplateSelector();
        foreach (var t in ContentTemplates)
            selector.Templates.Add(t);
        _messagesPart.ItemTemplate = selector;
    }

    // ── Session management ──

    private static void OnSessionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (MessageListView)bindable;
        control.UnsubscribeFromSession();

        if (newValue is AgentContext ctx)
            control.SubscribeToSession(ctx);

        control.RebuildFromSession();
    }

    private void SubscribeToSession(AgentContext ctx)
    {
        _turnAddedReg = ctx.RegisterOnTurnAdded(_ => { });

        _statusChangedReg = ctx.RegisterOnStatusChanged(status =>
            Dispatcher.Dispatch(() => OnStatusChanged(status)));

        _blockAddedReg = ctx.RegisterOnBlockAdded((turn, block) =>
            Dispatcher.Dispatch(() => OnBlockAdded(turn, block)));
    }

    private void UnsubscribeFromSession()
    {
        _turnAddedReg?.Dispose();
        _statusChangedReg?.Dispose();
        _blockAddedReg?.Dispose();
        _turnAddedReg = null;
        _statusChangedReg = null;
        _blockAddedReg = null;

        foreach (var sub in _blockSubscriptions)
            sub.Dispose();
        _blockSubscriptions.Clear();
        _dirtyBlocks.Clear();
    }

    private void OnStatusChanged(ConversationStatus status)
    {
        // If the session was cleared (idle with no turns), rebuild to empty.
        if (status == ConversationStatus.Idle && Session?.Turns.Count == 0)
        {
            RebuildFromSession();
            return;
        }

        // A failure surfaces via status/Error only — render a sticky error item (once).
        if (status == ConversationStatus.Error && Session?.Error is { } ex && !ReferenceEquals(ex, _shownError))
        {
            RemoveThinkingItem();
            _shownError = ex;
            _items.Add(new ContentContext(Session, new ErrorContentBlock(ex.Message)));
            ScrollToLatestMessage();
            return;
        }

        UpdateThinkingItem();
    }

    private void OnBlockAdded(ConversationTurn turn, ContentBlock block)
    {
        if (Session is null)
            return;

        // Keep any transient thinking item as the very last row.
        RemoveThinkingItem();

        _items.Add(new ContentContext(Session, block));
        _blockSubscriptions.Add(block.OnChanged(() => Dispatcher.Dispatch(() => MarkBlockDirty(block))));

        UpdateThinkingItem();
        ScrollToLatestMessage();
    }

    /// <summary>Marks a block as needing a UI refresh and schedules a single coalesced flush.</summary>
    private void MarkBlockDirty(ContentBlock block)
    {
        if (!_dirtyBlocks.Contains(block))
            _dirtyBlocks.Add(block);

        if (_flushScheduled)
            return;

        _flushScheduled = true;
        Dispatcher.DispatchDelayed(FlushInterval, FlushDirtyBlocks);
    }

    private void FlushDirtyBlocks()
    {
        _flushScheduled = false;

        if (_dirtyBlocks.Count == 0)
            return;

        var dirty = _dirtyBlocks.ToArray();
        _dirtyBlocks.Clear();

        if (Session is null)
            return;

        foreach (var block in dirty)
            ApplyBlockChanged(block);

        // The tail may now be streaming assistant content, which hides the thinking item.
        UpdateThinkingItem();
        ScrollToLatestMessage();
    }

    private void ApplyBlockChanged(ContentBlock block)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i].Block, block))
            {
                _items[i] = new ContentContext(Session!, block);
                return;
            }
        }
    }

    private void RebuildFromSession()
    {
        foreach (var sub in _blockSubscriptions)
            sub.Dispose();
        _blockSubscriptions.Clear();

        _items.Clear();
        _dirtyBlocks.Clear();
        _thinkingItem = null;
        _shownError = null;

        if (Session is null)
        {
            return;
        }

        foreach (var turn in Session.Turns)
        {
            foreach (var block in turn.RequestBlocks)
            {
                _items.Add(new ContentContext(Session, block));
                _blockSubscriptions.Add(block.OnChanged(() => Dispatcher.Dispatch(() => MarkBlockDirty(block))));
            }
            foreach (var block in turn.ResponseBlocks)
            {
                _items.Add(new ContentContext(Session, block));
                _blockSubscriptions.Add(block.OnChanged(() => Dispatcher.Dispatch(() => MarkBlockDirty(block))));
            }
        }

        // Re-project the current transient state (the error is not stored in turns).
        if (Session.Status == ConversationStatus.Error && Session.Error is { } ex)
        {
            _shownError = ex;
            _items.Add(new ContentContext(Session, new ErrorContentBlock(ex.Message)));
        }

        UpdateThinkingItem();
        ScrollToLatestMessage();
    }

    // ── Transient (UI-only) items: thinking + error ──

    /// <summary>Shows/hides the transient "Thinking…" item as the last row based on the session status.</summary>
    private void UpdateThinkingItem()
    {
        var want = ShouldShowThinking();

        if (want && _thinkingItem is null)
        {
            _thinkingItem = new ContentContext(Session!, new ThinkingContentBlock());
            _items.Add(_thinkingItem);
            ScrollToLatestMessage();
        }
        else if (!want && _thinkingItem is not null)
        {
            RemoveThinkingItem();
        }
    }

    private void RemoveThinkingItem()
    {
        if (_thinkingItem is null)
            return;

        _items.Remove(_thinkingItem);
        _thinkingItem = null;
    }

    private bool ShouldShowThinking()
    {
        if (Session?.Status != ConversationStatus.Streaming)
            return false;

        // Find the last real (non-thinking) item.
        ContentContext? lastReal = null;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_items[i], _thinkingItem))
                continue;
            lastReal = _items[i];
            break;
        }

        // Nothing yet — don't show "Thinking…" before the user's message appears.
        if (lastReal is null)
            return false;

        // Hide while the assistant is actively streaming visible content (its own bubble is the indicator).
        var block = lastReal.Block;
        var isAssistantContent = block.Role == ChatRole.Assistant
            && block is TextContentBlock or MediaContentBlock;
        return !isAssistantContent;
    }

    private void ScrollToLatestMessage()
    {
        if (_messagesPart is null || _items.Count == 0)
            return;

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
        {
            if (_items.Count == 0 || _messagesPart is null)
                return;

            _messagesPart.ScrollTo(_items.Count - 1, position: ScrollToPosition.End, animate: false);
        });
    }
}
