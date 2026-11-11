using System.Runtime.CompilerServices;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Optional base class for views created by a <see cref="BlockTemplate"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AIChatView"/> sets <see cref="Session"/>, <see cref="Block"/>, and the
/// <see cref="BindableObject.BindingContext"/> (the block) when it renders a row. The binding context stays the
/// block while it streams, so bindings follow the standard .NET contract: a binding refreshes when the property it
/// reads raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>. Two kinds of binding keep
/// their first value: a binding to the block itself (<c>{Binding .}</c>, or <c>{Binding Converter=…}</c> over the
/// whole block), which no property change refreshes, and a binding that hands a view an object or list the block
/// changes in place (<c>ItemsSource="{Binding Rows}"</c>), because reading the property again returns the same
/// instance.
/// </para>
/// <para>
/// <see cref="OnBlockChanged"/> runs when the block is assigned and after the block reports a change, at most once
/// per batch of streaming updates however many tokens arrived. Put logic over the whole block there, and read the
/// state a binding can't follow, such as a <see cref="List{T}"/> the block changes in place. To bind such state
/// instead, expose it as a property of the block that raises its change, and a list as an
/// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>.
/// </para>
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
    /// Called when <see cref="Block"/> is assigned and after the block reports a change, at most once per batch of
    /// streaming updates. Update the view from <see cref="Block"/> here.
    /// </summary>
    /// <remarks>
    /// A block reports a change through its <see cref="ContentBlock.OnChanged(System.Action)"/> callbacks. The
    /// engine's blocks report each of their changes; a block of your own reports one when its handler updates or
    /// completes it, or when it calls <see cref="ContentBlock.NotifyChanged"/>. Raising
    /// <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/> alone refreshes bindings but
    /// doesn't call this method.
    /// </remarks>
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

        // The block raises its property changes, so the bindings follow it without a new context.
        if (!ReferenceEquals(view.BindingContext, block))
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
