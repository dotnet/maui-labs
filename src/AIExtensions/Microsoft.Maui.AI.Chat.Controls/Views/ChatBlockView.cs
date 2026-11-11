using System.Runtime.CompilerServices;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A <see cref="BlockView"/> that also knows the session that produced its block, for views shown by
/// <see cref="AIChatView"/> that read the conversation's state, such as whether the block's turn is still running.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AIChatView"/> sets <see cref="Session"/> before it gives the view its block. Inside the chat view, the
/// message list refreshes the views it shows once per batch of changes, together with the rest of the row and the
/// scroll position, and when the conversation's status changes; so such a view doesn't also schedule its own refresh
/// for each change of its block. Outside a chat view it behaves like any <see cref="BlockView"/>.
/// </para>
/// </remarks>
public class ChatBlockView : BlockView
{
    private static readonly ConditionalWeakTable<ContentBlock, BlockViewState> ViewStates = new();

    private readonly bool _themed = ChatElements.EnsureTheme();

    /// <summary>Backing property for <see cref="Session"/>.</summary>
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(nameof(Session), typeof(AgentContext), typeof(ChatBlockView));

    /// <summary>Gets or sets the session that produced <see cref="BlockView.Block"/>.</summary>
    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Gets the chat view that hosts this view, used by built-in views to resolve nested templates.</summary>
    internal AIChatView? Owner { get; set; }

    /// <summary>Gets the turn that contains <see cref="BlockView.Block"/>, when the view is shown in a chat view.</summary>
    internal ConversationTurn? Turn { get; set; }

    /// <summary>
    /// Gets the view state of <see cref="BlockView.Block"/> (expanded, running). It belongs to the block rather than to
    /// this view, so a list that recycles the view for another block doesn't carry it over, and the block keeps it.
    /// </summary>
    internal BlockViewState ViewState => Block is { } block ? ViewStates.GetOrCreateValue(block) : new BlockViewState();

    /// <summary>
    /// Gets whether the block's turn is still running, so a call without a result may still get one and an
    /// approval or UI action can still be answered. Without a session nothing says the turn ended.
    /// </summary>
    internal bool IsTurnActive =>
        Session is not { } session ||
        (session.Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput &&
         (Turn is null || (session.Turns.Count > 0 && ReferenceEquals(session.Turns[^1], Turn))));

    /// <inheritdoc />
    /// <remarks>Inside an <see cref="AIChatView"/>, the message list refreshes the view instead.</remarks>
    protected override void OnBlockPropertyChanged(string? propertyName)
    {
        if (Owner is null)
            base.OnBlockPropertyChanged(propertyName);
    }

    /// <summary>Shows <paramref name="block"/> in a view a <see cref="BlockTemplate"/> created, or refreshes it.</summary>
    internal static void Bind(View view, ContentBlock block, AIChatView? owner, AgentContext? session, ConversationTurn? turn = null)
    {
        var chatView = view as ChatBlockView;
        if (chatView is not null)
        {
            chatView.Owner = owner;
            chatView.Session = session;
            chatView.Turn = turn;
        }

        // The block raises its property changes, so the bindings follow it without a new context. A new block runs
        // the view's OnBlockChanged; the same block runs it again only for a view the list refreshes.
        if (!ReferenceEquals(view.BindingContext, block))
            view.BindingContext = block;
        else
            chatView?.OnBlockChanged();
    }
}

/// <summary>The state built-in block views keep for a block.</summary>
internal sealed class BlockViewState
{
    public bool IsExpanded { get; set; }

    public bool IsRunning { get; set; }
}
