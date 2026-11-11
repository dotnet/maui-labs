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

    /// <summary>Shows <paramref name="block"/> in a view created by a <see cref="BlockTemplate"/>, or refreshes it.</summary>
    internal static void Bind(View view, ContentBlock block, AIChatView? owner, AgentContext? session)
    {
        var blockView = view as BlockView;
        if (blockView is not null)
        {
            blockView.Owner = owner;
            blockView.Session = session;
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
