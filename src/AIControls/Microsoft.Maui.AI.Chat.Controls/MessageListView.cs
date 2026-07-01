using System.Collections.ObjectModel;
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

    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private readonly ObservableCollection<ContentTemplate> _contentTemplates = [];
    private readonly ObservableCollection<ContentContext> _items = [];

    private CollectionView? _messagesPart;

    private IDisposable? _turnAddedReg;
    private IDisposable? _statusChangedReg;
    private IDisposable? _blockAddedReg;
    private IDisposable? _blockRemovedReg;
    private readonly List<IDisposable> _blockSubscriptions = [];

    /// <summary>The content templates used to render each block, highest matching priority wins.</summary>
    public IList<ContentTemplate> ContentTemplates => _contentTemplates;

    /// <summary>The number of blocks currently rendered.</summary>
    public int ItemCount => _items.Count;

    /// <summary>Raised whenever the rendered item count changes (block added, removed, or rebuilt).</summary>
    public event EventHandler? ItemsChanged;

    public MessageListView()
    {
        _contentTemplates.CollectionChanged += (_, _) => RebuildTemplateSelector();
        SetDynamicResource(ControlTemplateProperty, Themes.ChatThemeKeys.MessageListViewTemplate);
    }

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
        foreach (var t in _contentTemplates)
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

        _blockRemovedReg = ctx.RegisterOnBlockRemoved((turn, block) =>
            Dispatcher.Dispatch(() => OnBlockRemoved(block)));
    }

    private void UnsubscribeFromSession()
    {
        _turnAddedReg?.Dispose();
        _statusChangedReg?.Dispose();
        _blockAddedReg?.Dispose();
        _blockRemovedReg?.Dispose();
        _turnAddedReg = null;
        _statusChangedReg = null;
        _blockAddedReg = null;
        _blockRemovedReg = null;

        foreach (var sub in _blockSubscriptions)
            sub.Dispose();
        _blockSubscriptions.Clear();
    }

    private void OnStatusChanged(ConversationStatus status)
    {
        // If the session was cleared (idle with no turns), rebuild to empty.
        if (status == ConversationStatus.Idle && Session?.Turns.Count == 0)
            RebuildFromSession();
    }

    private void OnBlockAdded(ConversationTurn turn, ContentBlock block)
    {
        if (Session is null)
            return;

        _items.Add(new ContentContext(Session, block));
        NotifyItemsChanged();
        ScrollToLatestMessage();

        var subscription = block.OnChanged(() => Dispatcher.Dispatch(() => OnBlockChanged(block)));
        _blockSubscriptions.Add(subscription);
    }

    private void OnBlockRemoved(ContentBlock block)
    {
        // A transient block (e.g. the "Thinking…" placeholder) was removed from its turn.
        for (int i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i].Block, block))
            {
                _items.RemoveAt(i);
                NotifyItemsChanged();
                return;
            }
        }
    }

    private void OnBlockChanged(ContentBlock block)
    {
        if (Session is null)
            return;

        for (int i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i].Block, block))
            {
                _items[i] = new ContentContext(Session, block);
                ScrollToLatestMessage();
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

        if (Session is null)
        {
            NotifyItemsChanged();
            return;
        }

        foreach (var turn in Session.Turns)
        {
            foreach (var block in turn.RequestBlocks)
            {
                _items.Add(new ContentContext(Session, block));
                _blockSubscriptions.Add(block.OnChanged(() => Dispatcher.Dispatch(() => OnBlockChanged(block))));
            }
            foreach (var block in turn.ResponseBlocks)
            {
                _items.Add(new ContentContext(Session, block));
                _blockSubscriptions.Add(block.OnChanged(() => Dispatcher.Dispatch(() => OnBlockChanged(block))));
            }
        }

        NotifyItemsChanged();
        ScrollToLatestMessage();
    }

    private void NotifyItemsChanged() => ItemsChanged?.Invoke(this, EventArgs.Empty);

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
