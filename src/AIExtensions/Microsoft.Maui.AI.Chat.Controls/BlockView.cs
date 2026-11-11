using System.Runtime.CompilerServices;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Optional base class for views created by a <see cref="BlockTemplate"/>.
/// </summary>
/// <remarks>
/// <see cref="AIChatView"/> sets <see cref="Session"/>, <see cref="Block"/>, and the
/// <see cref="BindableObject.BindingContext"/> (the block) when it renders a row, and calls
/// <see cref="OnBlockChanged"/> when the block is assigned and after each batch of streaming updates.
/// Changes are coalesced, so a view refreshes at most once per batch however many tokens arrived.
/// </remarks>
public class BlockView : ContentView
{
    private static readonly ConditionalWeakTable<ContentBlock, BlockViewState> ViewStates = new();

    private readonly bool _themed = ChatElements.EnsureTheme();

    /// <summary>Backing property for <see cref="Block"/>.</summary>
    public static readonly BindableProperty BlockProperty =
        BindableProperty.Create(
            nameof(Block),
            typeof(ContentBlock),
            typeof(BlockView),
            propertyChanged: static (bindable, _, _) => ((BlockView)bindable).OnBlockChanged());

    /// <summary>Backing property for <see cref="Session"/>.</summary>
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(nameof(Session), typeof(AgentContext), typeof(BlockView));

    /// <summary>Gets or sets the block this view renders.</summary>
    public ContentBlock? Block
    {
        get => (ContentBlock?)GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    /// <summary>Gets or sets the session that produced <see cref="Block"/>.</summary>
    public AgentContext? Session
    {
        get => (AgentContext?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Called when <see cref="Block"/> is assigned and after the block changes. Update the view from
    /// <see cref="Block"/> here.
    /// </summary>
    protected virtual void OnBlockChanged()
    {
    }

    /// <summary>Gets the chat view that hosts this view, used by built-in views to resolve nested templates.</summary>
    internal AIChatView? Owner { get; set; }

    /// <summary>Gets the turn that contains <see cref="Block"/>, when the view is shown in a chat view.</summary>
    internal ConversationTurn? Turn { get; set; }

    /// <summary>
    /// Gets the view state of <see cref="Block"/> (expanded, running). It belongs to the block rather than to this
    /// view, so a list that recycles the view for another block doesn't carry it over, and the block keeps it.
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

    /// <summary>Shows <paramref name="block"/> in a view created by a <see cref="BlockTemplate"/>, or refreshes it.</summary>
    internal static void Bind(View view, ContentBlock block, AIChatView? owner, AgentContext? session, ConversationTurn? turn = null)
    {
        var blockView = view as BlockView;
        if (blockView is not null)
        {
            blockView.Owner = owner;
            blockView.Session = session;
            blockView.Turn = turn;
        }

        // Blocks don't raise property changes: resetting the context re-evaluates every binding once.
        view.BindingContext = null;
        view.BindingContext = block;

        if (blockView is null)
            return;

        if (ReferenceEquals(blockView.Block, block))
            blockView.OnBlockChanged();
        else
            blockView.Block = block;
    }
}

/// <summary>The state built-in block views keep for a block.</summary>
internal sealed class BlockViewState
{
    public bool IsExpanded { get; set; }

    public bool IsRunning { get; set; }
}
