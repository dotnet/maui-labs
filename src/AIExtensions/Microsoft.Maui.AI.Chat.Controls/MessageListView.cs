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
/// Bind <see cref="Session"/> to an <see cref="AgentContext"/> for a zero-configuration chat surface with
/// built-in text, approval, UI-action, reasoning, media, thinking, and error rendering. Add consumer <see cref="ContentTemplate"/>s
/// to <see cref="ContentTemplates"/> to replace those fallbacks for matching blocks, or set
/// <see cref="UseDefaultContentTemplates"/> to <see langword="false"/> for strict allow-list rendering.
/// Because it is session-driven it updates live as blocks stream in. The single template part is
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

    public static readonly BindableProperty UseDefaultContentTemplatesProperty =
        BindableProperty.Create(
            nameof(UseDefaultContentTemplates),
            typeof(bool),
            typeof(MessageListView),
            true,
            propertyChanged: (b, _, _) => ((MessageListView)b).RebuildTemplateSelector());

    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(nameof(ShowAvatars), typeof(bool), typeof(MessageListView), false);

    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(nameof(AvatarSize), typeof(double), typeof(MessageListView), 28.0);

    public static readonly BindableProperty UserDisplayNameProperty =
        BindableProperty.Create(nameof(UserDisplayName), typeof(string), typeof(MessageListView), "You");

    public static readonly BindableProperty AssistantDisplayNameProperty =
        BindableProperty.Create(nameof(AssistantDisplayName), typeof(string), typeof(MessageListView), "Assistant");

    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(nameof(ShowTimestamps), typeof(bool), typeof(MessageListView), false);

    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(nameof(BubbleCornerRadius), typeof(double), typeof(MessageListView), 16.0);

    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(nameof(BubbleStrokeThickness), typeof(double), typeof(MessageListView), 0.0);

    public static readonly BindableProperty BubbleStrokeColorProperty =
        BindableProperty.Create(nameof(BubbleStrokeColor), typeof(Color), typeof(MessageListView));

    public static readonly BindableProperty MaxBubbleWidthProperty =
        BindableProperty.Create(nameof(MaxBubbleWidth), typeof(double), typeof(MessageListView), 340.0);

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

    /// <summary>
    /// Consumer content templates used to render blocks. Any matching consumer template outranks the built-in
    /// fallback templates; numeric priority and declaration order resolve matches within this collection.
    /// </summary>
    public IList<ContentTemplate> ContentTemplates
    {
        get => (IList<ContentTemplate>)GetValue(ContentTemplatesProperty);
        set => SetValue(ContentTemplatesProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the built-in text, approval, UI-action, reasoning, media, thinking, and error templates are used when no
    /// consumer template matches. Set this to <see langword="false"/> to restore strict allow-list rendering.
    /// </summary>
    public bool UseDefaultContentTemplates
    {
        get => (bool)GetValue(UseDefaultContentTemplatesProperty);
        set => SetValue(UseDefaultContentTemplatesProperty, value);
    }

    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    public string UserDisplayName
    {
        get => (string)GetValue(UserDisplayNameProperty);
        set => SetValue(UserDisplayNameProperty, value);
    }

    public string AssistantDisplayName
    {
        get => (string)GetValue(AssistantDisplayNameProperty);
        set => SetValue(AssistantDisplayNameProperty, value);
    }

    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    public Color? BubbleStrokeColor
    {
        get => (Color?)GetValue(BubbleStrokeColorProperty);
        set => SetValue(BubbleStrokeColorProperty, value);
    }

    public double MaxBubbleWidth
    {
        get => (double)GetValue(MaxBubbleWidthProperty);
        set => SetValue(MaxBubbleWidthProperty, value);
    }

    /// <summary>
    /// The rendered items, in order (including any transient thinking/error items). This is an observable,
    /// read-only collection — a container can bind its <c>ItemsSource</c> to it and track live changes.
    /// </summary>
    public ReadOnlyObservableCollection<ContentContext> Items =>
        (ReadOnlyObservableCollection<ContentContext>)GetValue(ItemsProperty);

    private readonly ObservableCollection<ContentContext> _items = [];
    private readonly IReadOnlyList<ContentTemplate> _defaultContentTemplates =
    [
        new TextContentTemplate { Role = "User", Priority = -10_000 },
        new TextContentTemplate { Role = "Assistant", Priority = -10_000 },
        new ToolApprovalTemplate { Priority = -10_000 },
        new UIActionContentTemplate { Priority = -10_000 },
        new ReasoningContentTemplate { Priority = -10_000 },
        new MediaContentTemplate { Priority = -10_000 },
        new ThinkingContentTemplate { Priority = -10_000 },
        new ErrorContentTemplate { Priority = -10_000 },
    ];

    private CollectionView? _messagesPart;

    private IDisposable? _turnAddedReg;
    private IDisposable? _statusChangedReg;
    private IDisposable? _blockAddedReg;
    private readonly Dictionary<ContentBlock, IDisposable> _blockSubscriptions =
        new(ReferenceEqualityComparer.Instance);

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

        SetDynamicResource(MaxBubbleWidthProperty, Themes.ChatThemeKeys.BubbleMaxWidth);
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

        _messagesPart.ItemTemplate = CreateTemplateSelector();
    }

    internal ContentTemplateSelector CreateTemplateSelector()
    {
        var selector = new ContentTemplateSelector();
        foreach (var t in ContentTemplates)
            selector.Templates.Add(t);

        if (UseDefaultContentTemplates)
        {
            foreach (var t in _defaultContentTemplates)
                selector.FallbackTemplates.Add(t);
        }

        return selector;
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

        foreach (var sub in _blockSubscriptions.Values)
            sub.Dispose();
        _blockSubscriptions.Clear();
        _dirtyBlocks.Clear();
    }

    private void OnStatusChanged(ConversationStatus status)
    {
        // Cancellation removes partial response blocks from the engine turn. Re-project only
        // when that makes the UI projection differ, avoiding a full rebuild after normal turns.
        if (status == ConversationStatus.Idle && Session?.Turns.Count == 0)
        {
            RebuildFromSession();
            return;
        }

        if (status == ConversationStatus.Idle && !ProjectionMatchesSession())
            ReconcileItemsWithSession();

        // A failure surfaces via status/Error only — render a sticky error item (once).
        if (status == ConversationStatus.Error && Session?.Error is { } ex && !ReferenceEquals(ex, _shownError))
        {
            RemoveThinkingItem();
            _shownError = ex;
            _items.Add(CreateContentContext(new ErrorContentBlock(ErrorContentBlock.DefaultUserMessage)));
            ScrollToLatestMessage();
            return;
        }

        UpdateThinkingItem();
    }

    private bool ProjectionMatchesSession()
    {
        if (Session is null)
            return _items.Count == 0;

        var projectedBlocks = _items
            .Where(item => item.Block is not ThinkingContentBlock and not ErrorContentBlock)
            .Select(item => item.Block);
        var sessionBlocks = Session.Turns.SelectMany(turn =>
            turn.RequestBlocks.Concat(turn.ResponseBlocks));

        return projectedBlocks.SequenceEqual(sessionBlocks, ReferenceEqualityComparer.Instance);
    }

    internal void ReconcileItemsWithSession()
    {
        if (Session is null)
            return;

        var sessionBlocks = Session.Turns
            .SelectMany(turn => turn.RequestBlocks.Concat(turn.ResponseBlocks))
            .ToHashSet(ReferenceEqualityComparer.Instance);

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var block = _items[i].Block;
            if (block is ThinkingContentBlock or ErrorContentBlock || sessionBlocks.Contains(block))
                continue;

            _items.RemoveAt(i);
            if (_blockSubscriptions.Remove(block, out var subscription))
                subscription.Dispose();
            _dirtyBlocks.Remove(block);
        }
    }

    private void OnBlockAdded(ConversationTurn turn, ContentBlock block)
    {
        if (Session is null)
            return;

        // Keep any transient thinking item as the very last row.
        RemoveThinkingItem();

        _items.Add(CreateContentContext(block));
        SubscribeToBlock(block);

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
                _items[i] = CreateContentContext(block);
                return;
            }
        }
    }

    private void RebuildFromSession()
    {
        foreach (var sub in _blockSubscriptions.Values)
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
                _items.Add(CreateContentContext(block));
                SubscribeToBlock(block);
            }
            foreach (var block in turn.ResponseBlocks)
            {
                _items.Add(CreateContentContext(block));
                SubscribeToBlock(block);
            }
        }

        // Re-project the current transient state (the error is not stored in turns).
        if (Session.Status == ConversationStatus.Error && Session.Error is { } ex)
        {
            _shownError = ex;
            _items.Add(CreateContentContext(new ErrorContentBlock(ErrorContentBlock.DefaultUserMessage)));
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
            _thinkingItem = CreateContentContext(new ThinkingContentBlock());
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
            && block is RichContentBlock or ReasoningContentBlock or MediaContentBlock;
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

    private ContentContext CreateContentContext(ContentBlock block) =>
        new(Session!, block, this);

    private void SubscribeToBlock(ContentBlock block)
    {
        if (_blockSubscriptions.Remove(block, out var existing))
            existing.Dispose();

        _blockSubscriptions[block] =
            block.OnChanged(() => Dispatcher.Dispatch(() => MarkBlockDirty(block)));
    }
}
