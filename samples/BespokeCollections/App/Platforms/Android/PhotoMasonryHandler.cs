using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using BespokeCollections.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using AView = Android.Views.View;

namespace BespokeCollections.App;

public partial class PhotoMasonryHandler : ViewHandler<PhotoMasonryView, RecyclerView>, IPhotoMasonryNative
{
    IReadOnlyList<PhotoItem> _items = [];
    readonly List<WeakReference<PhotoHolder>> _holders = [];
    AsyncWorkGate? _decode;
    MasonryManager? _manager;
    PhotoAdapter? _adapter;
    PhotoScroll? _scroll;
    long _epoch;
    bool _connected;
    float Density => Context.Resources?.DisplayMetrics?.Density ?? 1;

    protected override RecyclerView CreatePlatformView()
    {
        var recycler = new RecyclerView(Context) { ContentDescription = "Photo masonry collection" };
        recycler.HasFixedSize = true;
        recycler.SetItemAnimator(null);
        _manager = new(this);
        recycler.SetLayoutManager(_manager);
        return recycler;
    }

    protected override void ConnectHandler(RecyclerView platformView)
    {
        base.ConnectHandler(platformView);
        _connected = true;
        _decode = new();
        _items = [];
        _epoch = -1;
        _adapter = new(this);
        _adapter.HasStableIds = true;
        _scroll = new(this);
        platformView.SetAdapter(_adapter);
        platformView.AddOnScrollListener(_scroll);
        VirtualView.Feed.Changed += OnFeedChanged;
        OnFeedChanged(this, EventArgs.Empty);
    }

    protected override void DisconnectHandler(RecyclerView platformView)
    {
        _connected = false;
        VirtualView.Feed.Changed -= OnFeedChanged;
        foreach (var reference in _holders)
            if (reference.TryGetTarget(out var holder)) holder.Unbind();
        _holders.Clear();
        platformView.RemoveOnScrollListener(_scroll!);
        platformView.SetAdapter(null);
        _adapter?.Dispose();
        _scroll?.Dispose();
        _decode?.Dispose();
        _decode = null;
        base.DisconnectHandler(platformView);
    }

    void OnFeedChanged(object? sender, EventArgs args)
    {
        if (!_connected) return;
        var state = VirtualView.Feed.State;
        if (ReferenceEquals(_items, state.Items) && _epoch == state.Epoch) return;
        var old = _items;
        var anchor = Inspect().Cells.OrderBy(cell => cell.Frame.Y).FirstOrDefault();
        var reset = _epoch != state.Epoch || old.Count == 0 || state.Items.Count == 0;
        _epoch = state.Epoch;
        _items = state.Items;
        _manager!.InvalidateGeometry();
        if (reset)
        {
            foreach (var reference in _holders)
                if (reference.TryGetTarget(out var holder)) holder.Unbind();
            _manager.OffsetPixels = 0;
            _adapter!.NotifyDataSetChanged();
            return;
        }
        var removed = Array.FindIndex(old.ToArray(), item => item.Id == state.Items[0].Id);
        if (removed < 0) removed = old.Count;
        var retained = old.Count - removed;
        var inserted = state.Items.Count - retained;
        if (inserted < 0 || !old.Skip(removed).Select(item => item.Id).SequenceEqual(state.Items.Take(retained).Select(item => item.Id)))
            throw new InvalidOperationException("The prototype supports append/window eviction or an explicit epoch reset.");
        if (removed > 0) _adapter!.NotifyItemRangeRemoved(0, removed);
        if (inserted > 0) _adapter!.NotifyItemRangeInserted(retained, inserted);
        if (anchor is not null && PlatformView.Width > 0)
        {
            _manager.PrepareGeometry();
            var index = Array.FindIndex(_items.ToArray(), item => item.Id == anchor.Key);
            if (index >= 0)
                _manager.OffsetPixels = (int)Math.Round((_manager.Geometry![index].Y - (anchor.Frame.Y - _manager.OffsetPixels / Density)) * Density);
        }
        PlatformView.RequestLayout();
    }

    public void ScrollToOffset(double offset)
    {
        if (!_connected) return;
        _manager!.PrepareGeometry();
        var maximum = Math.Max(0, (_manager.Geometry?.Height ?? 0) - PlatformView.Height / Density);
        _manager.OffsetPixels = (int)Math.Round(Math.Clamp(offset, 0, maximum) * Density);
        PlatformView.RequestLayout();
    }

    public NativeMasonrySnapshot Inspect()
    {
        var cells = new List<NativeCellProof>();
        for (var index = 0; index < PlatformView.ChildCount; index++)
        {
            var child = PlatformView.GetChildAt(index)!;
            if (PlatformView.GetChildViewHolder(child) is PhotoHolder holder && holder.Key > 0)
                cells.Add(new(holder.Key, holder.ImageKey, holder.Caption,
                    new(child.Left / Density, (child.Top + (_manager?.OffsetPixels ?? 0)) / Density,
                        child.Width / Density, child.Height / Density), holder.Epoch));
        }
        var counters = VirtualView.Counters;
        counters.Realized(cells.Count);
        return new("android", PlatformView.GetType().FullName!, _manager!.GetType().FullName!, _connected, PlatformView.IsLayoutRequested,
            _items.Count, cells.Count, counters.Created, counters.Binds, counters.Recycled, counters.PeakRealized,
            counters.Candidates, _manager.OffsetPixels / Density,
            new(0, _manager.OffsetPixels / Density, PlatformView.Width / Density, PlatformView.Height / Density),
            counters.DecodedBytes, counters.PeakDecodedBytes, counters.Stale, counters.Errors, cells.ToArray());
    }

    sealed class MasonryManager(PhotoMasonryHandler owner) : RecyclerView.LayoutManager
    {
        IReadOnlyList<PhotoItem>? _lastItems;
        int _poolCapacity;
        public MasonryGeometry? Geometry { get; private set; }
        public int OffsetPixels { get; set; }
        public void InvalidateGeometry() { _lastItems = null; RequestLayout(); }
        public void PrepareGeometry()
        {
            var width = Width / owner.Density;
            if (width <= 24) return;
            if (ReferenceEquals(_lastItems, owner._items) && Geometry is not null && Math.Abs(Geometry.Width - width) < 0.5) return;
            Geometry = MasonryGeometry.Create(owner._items, width, Math.Clamp((int)(width / 160), 2, 4));
            _lastItems = owner._items;
        }
        public override RecyclerView.LayoutParams GenerateDefaultLayoutParams() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        public override bool CanScrollVertically() => true;
        public override void OnLayoutChildren(RecyclerView.Recycler? recycler, RecyclerView.State? state)
        {
            ArgumentNullException.ThrowIfNull(recycler);
            ArgumentNullException.ThrowIfNull(state);
            PrepareGeometry();
            if (Geometry is null || state.ItemCount == 0) { RemoveAndRecycleAllViews(recycler); return; }
            OffsetPixels = Math.Clamp(OffsetPixels, 0, Math.Max(0, (int)Math.Round(Geometry.Height * owner.Density) - Height));
            Fill(recycler);
        }
        public override int ScrollVerticallyBy(int dy, RecyclerView.Recycler? recycler, RecyclerView.State? state)
        {
            ArgumentNullException.ThrowIfNull(recycler);
            if (Geometry is null) return 0;
            var next = Math.Clamp(OffsetPixels + dy, 0, Math.Max(0, (int)Math.Round(Geometry.Height * owner.Density) - Height));
            var consumed = next - OffsetPixels;
            OffsetPixels = next;
            OffsetChildrenVertical(-consumed);
            Fill(recycler);
            return consumed;
        }
        void Fill(RecyclerView.Recycler recycler)
        {
            var geometry = Geometry!;
            var density = owner.Density;
            var wanted = geometry.Intersecting(new(0, OffsetPixels / density - 150, Width / density, Height / density + 300), out var visited);
            owner.VirtualView.Counters.Candidates = visited;
            if (wanted.Length > _poolCapacity)
            {
                // A nonoverlapping jump recycles a whole window before acquiring the next.
                _poolCapacity = wanted.Length;
                owner.PlatformView.GetRecycledViewPool()?.SetMaxRecycledViews(0, _poolCapacity);
            }
            var set = wanted.ToHashSet();
            for (var index = ChildCount - 1; index >= 0; index--)
            {
                var child = GetChildAt(index)!;
                if (!set.Contains(GetPosition(child))) RemoveAndRecycleView(child, recycler);
            }
            foreach (var index in wanted)
            {
                var child = FindViewByPosition(index);
                if (child is null) { child = recycler.GetViewForPosition(index); AddView(child); }
                var frame = geometry[index];
                var width = (int)Math.Round(frame.Width * density);
                var height = (int)Math.Round(frame.Height * density);
                if (child.IsLayoutRequested || child.MeasuredWidth != width || child.MeasuredHeight != height)
                    child.Measure(AView.MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly), AView.MeasureSpec.MakeMeasureSpec(height, MeasureSpecMode.Exactly));
                var left = (int)Math.Round(frame.X * density);
                var top = (int)Math.Round(frame.Y * density) - OffsetPixels;
                LayoutDecoratedWithMargins(child, left, top, left + width, top + height);
            }
            owner.VirtualView.Counters.Realized(ChildCount);
        }
        public override int ComputeVerticalScrollExtent(RecyclerView.State state) => Height;
        public override int ComputeVerticalScrollOffset(RecyclerView.State state) => OffsetPixels;
        public override int ComputeVerticalScrollRange(RecyclerView.State state) => (int)Math.Round((Geometry?.Height ?? 0) * owner.Density);
    }

    sealed class PhotoAdapter(PhotoMasonryHandler owner) : RecyclerView.Adapter
    {
        public override int ItemCount => owner._items.Count;
        public override long GetItemId(int position) => owner._items[position].Id;
        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var holder = new PhotoHolder(new PhotoCard(parent.Context!), owner);
            owner._holders.RemoveAll(reference => !reference.TryGetTarget(out _));
            owner._holders.Add(new(holder));
            owner.VirtualView.Counters.Created++;
            return holder;
        }
        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position) => ((PhotoHolder)holder).Bind(owner._items[position]);
        public override void OnViewRecycled(Java.Lang.Object holder)
        {
            if (holder is PhotoHolder photo) photo.Unbind();
            base.OnViewRecycled(holder);
        }
    }

    sealed class PhotoScroll(PhotoMasonryHandler owner) : RecyclerView.OnScrollListener
    {
        public override void OnScrolled(RecyclerView recyclerView, int dx, int dy)
        {
            if (owner._connected && owner._items.Count > 0 && owner._manager!.Geometry is { } geometry &&
                owner._manager.OffsetPixels / owner.Density + recyclerView.Height / owner.Density > geometry.Height - 600)
                _ = owner.VirtualView.DemandNextAsync();
        }
    }

    sealed class PhotoCard : FrameLayout
    {
        public ImageView Image { get; }
        public TextView Text { get; }
        readonly float _density;
        public PhotoCard(Context context) : base(context)
        {
            _density = context.Resources?.DisplayMetrics?.Density ?? 1;
            Image = new(context);
            Image.SetScaleType(ImageView.ScaleType.CenterCrop);
            Text = new(context) { TextSize = 14 };
            Text.SetTextColor(Android.Graphics.Color.White);
            Text.SetMaxLines(2);
            Text.SetPadding((int)(6 * _density), 0, (int)(6 * _density), 0);
            SetBackgroundColor(Android.Graphics.Color.Rgb(38, 58, 82));
            AddView(Image);
            AddView(Text);
            LayoutParameters = new RecyclerView.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        }
        protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
        {
            var caption = (int)Math.Round(48 * _density);
            Image.Layout(0, 0, Width, Math.Max(0, Height - caption));
            Text.Layout(0, Math.Max(0, Height - caption + (int)(4 * _density)), Width, Height);
        }
    }

    sealed class PhotoHolder(PhotoCard card, PhotoMasonryHandler owner) : RecyclerView.ViewHolder(card)
    {
        CancellationTokenSource? _binding;
        Bitmap? _bitmap;
        int _generation;
        long _cost;
        public long Key { get; private set; }
        public long ImageKey { get; private set; }
        public long Epoch { get; private set; }
        public string Caption => card.Text.Text ?? "";

        public void Bind(PhotoItem item)
        {
            Unbind();
            Key = item.Id;
            Epoch = owner._epoch;
            card.Text.Text = item.Caption;
            card.ContentDescription = item.Caption;
            owner.VirtualView.Counters.Binds++;
            _binding = new();
            var generation = ++_generation;
            var width = Math.Clamp((int)((owner._manager?.Geometry?.Width ?? 390) /
                (owner._manager?.Geometry?.ColumnCount ?? 2) * owner.Density), 64, 384);
            _ = LoadImageAsync(item, width, generation, Epoch, _binding.Token);
        }

        async Task LoadImageAsync(PhotoItem item, int width, int generation, long epoch, CancellationToken token)
        {
            Bitmap? prepared = null;
            var view = owner.VirtualView;
            var decode = owner._decode ?? throw new InvalidOperationException("The image decoder is disconnected.");
            try
            {
                var bytes = await view.Thumbnails.GetAsync(item, width, token).ConfigureAwait(false);
                prepared = await decode.RunAsync(() => BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length)
                    ?? throw new InvalidDataException("The fixture thumbnail is not a bitmap."), token).ConfigureAwait(false);
                var bitmap = prepared;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!view.Dispatcher.Dispatch(() =>
                {
                    try
                    {
                        if (!owner._connected || token.IsCancellationRequested || generation != _generation || Key != item.Id || epoch != owner._epoch)
                        {
                            view.Counters.Stale++;
                            bitmap.Dispose();
                        }
                        else
                        {
                            var cost = bitmap.AllocationByteCount;
                            view.Counters.AddDecoded(cost);
                            _cost = cost;
                            _bitmap = bitmap;
                            card.Image.SetImageBitmap(bitmap);
                            ImageKey = item.Id;
                        }
                        completion.TrySetResult();
                    }
                    catch (Exception exception) { bitmap.Dispose(); completion.TrySetException(exception); }
                }))
                    throw new InvalidOperationException("The image dispatcher rejected publication.");
                prepared = null;
                await completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Interlocked.Increment(ref view.Counters.Errors);
                view.Logger.LogError(exception, "Android thumbnail {Key} failed", item.Id);
            }
            finally { prepared?.Dispose(); }
        }

        public void Unbind()
        {
            _generation++;
            _binding?.Cancel();
            _binding?.Dispose();
            _binding = null;
            card.Image.SetImageDrawable(null);
            _bitmap?.Dispose();
            _bitmap = null;
            owner.VirtualView.Counters.RemoveDecoded(_cost);
            if (Key != 0) owner.VirtualView.Counters.Recycled++;
            _cost = 0;
            Key = ImageKey = Epoch = 0;
        }
    }
}
