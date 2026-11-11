using System.ComponentModel;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A view that shows one <see cref="ContentBlock"/>, its <see cref="BindableObject.BindingContext"/>, and updates
/// itself in <see cref="OnBlockChanged"/>.
/// </summary>
/// <remarks>
/// <para>
/// Derive from it when bindings aren't enough: logic over the whole block, or state a binding can't follow, such as a
/// <see cref="List{T}"/> the block changes in place. <see cref="OnBlockChanged"/> runs when the view gets its block,
/// and again after the block raises <see cref="INotifyPropertyChanged.PropertyChanged"/>, once for all the changes
/// raised before the view's dispatcher runs it, however many tokens streamed in. Bindings in the view keep working as
/// usual (see <see cref="BlockTemplate"/>).
/// </para>
/// <para>
/// The block holds the view only weakly, like a binding does, so a session that outlives a page doesn't keep its views
/// alive. Derive from <see cref="BlockView{TBlock}"/> to get <see cref="Block"/> as a specific block type.
/// </para>
/// </remarks>
public class BlockView : ContentView
{
    private readonly object _gate = new();
    private ContentBlock? _block;
    private Subscription? _subscription;
    private IDispatcher? _dispatcher;
    private int _generation;
    private bool _scheduled;

    /// <summary>Gets the block the view shows: its binding context, when that is a <see cref="ContentBlock"/>.</summary>
    public ContentBlock? Block => _block;

    /// <summary>
    /// Called when <see cref="Block"/> changes (including to <see langword="null"/>) and after the block reports a
    /// change, at most once per pass of the view's dispatcher. Update the view from <see cref="Block"/> here.
    /// </summary>
    /// <remarks>Runs on the view's thread.</remarks>
    protected virtual void OnBlockChanged()
    {
    }

    /// <summary>
    /// Called for each <see cref="INotifyPropertyChanged.PropertyChanged"/> event of <see cref="Block"/>, on the thread
    /// that changed the block. The base implementation schedules <see cref="OnBlockChanged"/> on the view's
    /// dispatcher, once until it runs; override to react to particular properties, and call the base implementation
    /// to keep that.
    /// </summary>
    /// <param name="propertyName">The property that changed. Empty when every property may have changed.</param>
    protected virtual void OnBlockPropertyChanged(string? propertyName)
    {
        int generation;
        lock (_gate)
        {
            if (_scheduled)
                return;
            _scheduled = true;
            generation = _generation;
        }

        if (_dispatcher is not { } dispatcher || !dispatcher.Dispatch(() => RunScheduled(generation)))
            RunScheduled(generation);
    }

    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        var block = BindingContext as ContentBlock;
        if (ReferenceEquals(block, _block))
            return;

        _subscription?.Dispose();
        lock (_gate)
        {
            _block = block;
            _generation++;
            _scheduled = false;
        }

        // The dispatcher of the thread that gives the view its block, the view's thread, runs the scheduled updates.
        _dispatcher = Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread();
        _subscription = block is null ? null : new Subscription(this, block);
        OnBlockChanged();
    }

    private void RunScheduled(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return;
            _scheduled = false;
        }

        OnBlockChanged();
    }

    private void OnSubscribedBlockChanged(ContentBlock block, string? propertyName)
    {
        if (ReferenceEquals(block, _block))
            OnBlockPropertyChanged(propertyName);
    }

    /// <summary>Forwards the block's changes to the view while the view is alive, and then unsubscribes.</summary>
    private sealed class Subscription
    {
        private readonly WeakReference<BlockView> _view;
        private readonly ContentBlock _block;

        public Subscription(BlockView view, ContentBlock block)
        {
            _view = new WeakReference<BlockView>(view);
            _block = block;
            block.PropertyChanged += OnPropertyChanged;
        }

        public void Dispose() => _block.PropertyChanged -= OnPropertyChanged;

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_view.TryGetTarget(out var view))
                view.OnSubscribedBlockChanged(_block, e.PropertyName);
            else
                Dispose();
        }
    }
}

/// <summary>A <see cref="BlockView"/> for blocks of one type.</summary>
/// <typeparam name="TBlock">The type of block the view shows.</typeparam>
public class BlockView<TBlock> : BlockView
    where TBlock : ContentBlock
{
    /// <summary>Gets the block the view shows, or <see langword="null"/> when its binding context is not a <typeparamref name="TBlock"/>.</summary>
    public new TBlock? Block => base.Block as TBlock;
}
