using BespokeCollections.Core;
using CoreGraphics;
using Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using ObjCRuntime;
using UIKit;

namespace BespokeCollections.App;

public partial class PhotoMasonryHandler : ViewHandler<PhotoMasonryView, UICollectionView>, IPhotoMasonryNative
{
    IReadOnlyList<PhotoItem> _items = [];
    readonly List<WeakReference<PhotoCell>> _cells = [];
    readonly HashSet<PhotoCell> _displayedCells = [];
    AsyncWorkGate? _decode;
    MasonryLayout? _layout;
    PhotoDataSource? _source;
    PhotoDelegate? _delegate;
    long _epoch;
    bool _connected;
    bool _updating;
    PhotoFeedState? _pending;
    double? _requestedOffset;

    protected override UICollectionView CreatePlatformView()
    {
        _layout = new(this);
        var collection = new UICollectionView(CGRect.Empty, _layout)
        {
            BackgroundColor = UIColor.FromRGB(16, 34, 61),
            AlwaysBounceVertical = true,
            AccessibilityIdentifier = "native-photo-masonry"
        };
        collection.RegisterClassForCell(typeof(PhotoCell), PhotoCell.Reuse);
        return collection;
    }

    protected override void ConnectHandler(UICollectionView platformView)
    {
        base.ConnectHandler(platformView);
        _connected = true;
        _decode = new();
        _items = [];
        _epoch = -1;
        _updating = false;
        _pending = null;
        _requestedOffset = null;
        _source = new(this);
        _delegate = new(this);
        platformView.DataSource = _source;
        platformView.Delegate = _delegate;
        VirtualView.Feed.Changed += OnFeedChanged;
        OnFeedChanged(this, EventArgs.Empty);
    }

    protected override void DisconnectHandler(UICollectionView platformView)
    {
        _connected = false;
        VirtualView.Feed.Changed -= OnFeedChanged;
        platformView.WeakDataSource = null;
        platformView.WeakDelegate = null;
        foreach (var reference in _cells)
            if (reference.TryGetTarget(out var cell)) cell.Unbind();
        foreach (var cell in _displayedCells.ToArray()) cell.Unbind();
        _displayedCells.Clear();
        _cells.Clear();
        _source?.Dispose();
        _delegate?.Dispose();
        _decode?.Dispose();
        _decode = null;
        base.DisconnectHandler(platformView);
    }

    void OnFeedChanged(object? sender, EventArgs args)
    {
        if (!_connected) return;
        var state = VirtualView.Feed.State;
        if (_updating) { _pending = state; return; }
        if (ReferenceEquals(_items, state.Items) && _epoch == state.Epoch) return;
        var old = _items;
        var visible = PlatformView.IndexPathsForVisibleItems
            .Where(path => path.Item < old.Count)
            .OrderBy(path => _layout!.Geometry?[checked((int)path.Item)].Y ?? 0).FirstOrDefault();
        var anchorKey = visible is null ? 0 : old[checked((int)visible.Item)].Id;
        var relative = visible is null ? 0 : (_layout!.Geometry?[checked((int)visible.Item)].Y ?? 0) - PlatformView.ContentOffset.Y;
        var reset = _epoch != state.Epoch || old.Count == 0 || state.Items.Count == 0;
        _epoch = state.Epoch;
        if (reset)
        {
            foreach (var reference in _cells)
                if (reference.TryGetTarget(out var cell)) cell.Unbind();
            _items = state.Items;
            _layout!.InvalidateGeometry();
            PlatformView.ReloadData();
            PlatformView.SetContentOffset(CGPoint.Empty, false);
            return;
        }

        var removed = Array.FindIndex(old.ToArray(), item => item.Id == state.Items[0].Id);
        if (removed < 0) removed = old.Count;
        var retained = old.Count - removed;
        var inserted = state.Items.Count - retained;
        if (inserted < 0 || !old.Skip(removed).Select(item => item.Id).SequenceEqual(state.Items.Take(retained).Select(item => item.Id)))
            throw new InvalidOperationException("The prototype supports append/window eviction or an explicit epoch reset.");
        _updating = true;
        UIView.PerformWithoutAnimation(() => PlatformView.PerformBatchUpdates(() =>
        {
            _items = state.Items;
            _layout!.InvalidateGeometry();
            if (removed > 0) PlatformView.DeleteItems(Paths(0, removed));
            if (inserted > 0) PlatformView.InsertItems(Paths(retained, inserted));
        }, _ =>
        {
            if (!_connected) return;
            PlatformView.LayoutIfNeeded();
            _updating = false;
            var index = Array.FindIndex(_items.ToArray(), item => item.Id == anchorKey);
            if (_requestedOffset is { } requested)
            {
                _requestedOffset = null;
                ScrollToOffset(requested);
            }
            else if (index >= 0 && _layout!.Geometry is { } geometry)
                ScrollToOffset(Math.Max(0, geometry[index].Y - relative));
            if (_pending is not null)
            {
                _pending = null;
                OnFeedChanged(this, EventArgs.Empty);
            }
        }));
    }

    static NSIndexPath[] Paths(int first, int count) =>
        Enumerable.Range(first, count).Select(index => NSIndexPath.FromItemSection(index, 0)).ToArray();

    public void ScrollToOffset(double offset)
    {
        if (!_connected) return;
        if (_updating) { _requestedOffset = offset; return; }
        PlatformView.LayoutIfNeeded();
        var maximum = Math.Max(0, PlatformView.ContentSize.Height - PlatformView.Bounds.Height);
        PlatformView.SetContentOffset(new CGPoint(0, Math.Clamp(offset, 0, maximum)), false);
        PlatformView.LayoutIfNeeded();
    }

    public NativeMasonrySnapshot Inspect()
    {
        var viewport = PlatformView.Bounds;
        var cells = PlatformView.VisibleCells.OfType<PhotoCell>().Where(cell => cell.Key > 0)
            .Select(cell => new NativeCellProof(cell.Key, cell.ImageKey, cell.Caption,
                new(cell.Frame.X, cell.Frame.Y, cell.Frame.Width, cell.Frame.Height), cell.Epoch)).ToArray();
        var counters = VirtualView.Counters;
        counters.Realized(cells.Length);
        return new("ios", PlatformView.GetType().FullName!, _layout!.GetType().FullName!, _connected, _updating || _pending is not null,
            _items.Count, cells.Length, counters.Created, counters.Binds, counters.Recycled, counters.PeakRealized,
            counters.Candidates, PlatformView.ContentOffset.Y, new(0, PlatformView.ContentOffset.Y, viewport.Width, viewport.Height),
            counters.DecodedBytes, counters.PeakDecodedBytes, counters.Stale, counters.Errors, cells);
    }

    sealed class MasonryLayout(PhotoMasonryHandler owner) : UICollectionViewLayout
    {
        IReadOnlyList<PhotoItem>? _lastItems;
        public MasonryGeometry? Geometry { get; private set; }
        public void InvalidateGeometry() { _lastItems = null; InvalidateLayout(); }
        public override void PrepareLayout()
        {
            base.PrepareLayout();
            var width = CollectionView?.Bounds.Width ?? 0;
            if (width <= 24) return;
            if (ReferenceEquals(_lastItems, owner._items) && Geometry is not null && Math.Abs(Geometry.Width - width) < 0.5) return;
            Geometry = MasonryGeometry.Create(owner._items, width, Math.Clamp((int)(width / 160), 2, 4));
            _lastItems = owner._items;
        }
        public override CGSize CollectionViewContentSize => new(Geometry?.Width ?? 0, Geometry?.Height ?? 0);
        public override bool ShouldInvalidateLayoutForBoundsChange(CGRect newBounds) =>
            Math.Abs(newBounds.Width - (Geometry?.Width ?? 0)) > 0.5;
        public override UICollectionViewLayoutAttributes LayoutAttributesForItem(NSIndexPath indexPath)
        {
            var geometry = Geometry ?? throw new InvalidOperationException("Masonry geometry is not prepared.");
            var frame = geometry[checked((int)indexPath.Item)];
            var attributes = UICollectionViewLayoutAttributes.CreateForCell(indexPath);
            attributes.Frame = new(frame.X, frame.Y, frame.Width, frame.Height);
            return attributes;
        }
        public override UICollectionViewLayoutAttributes[] LayoutAttributesForElementsInRect(CGRect rect)
        {
            if (Geometry is null) return [];
            var indices = Geometry.Intersecting(new(rect.X, rect.Y, rect.Width, rect.Height), out var visited);
            owner.VirtualView.Counters.Candidates = visited;
            return indices.Select(index => LayoutAttributesForItem(NSIndexPath.FromItemSection(index, 0))).ToArray();
        }
    }

    sealed class PhotoDataSource(PhotoMasonryHandler owner) : UICollectionViewDataSource
    {
        public override nint GetItemsCount(UICollectionView collectionView, nint section) => owner._items.Count;
        public override UICollectionViewCell GetCell(UICollectionView collectionView, NSIndexPath indexPath)
        {
            var cell = (PhotoCell)collectionView.DequeueReusableCell(PhotoCell.Reuse, indexPath);
            owner._cells.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (!owner._cells.Any(reference => reference.TryGetTarget(out var current) && ReferenceEquals(current, cell)))
                owner._cells.Add(new(cell));
            cell.Bind(owner, owner._items[checked((int)indexPath.Item)]);
            return cell;
        }
    }

    sealed class PhotoDelegate(PhotoMasonryHandler owner) : UICollectionViewDelegate
    {
        public override void WillDisplayCell(UICollectionView collectionView, UICollectionViewCell cell, NSIndexPath indexPath)
        {
            if (!owner._connected || indexPath.Item >= owner._items.Count) return;
            var photo = (PhotoCell)cell;
            owner._displayedCells.Add(photo);
            photo.Show(owner._items[checked((int)indexPath.Item)]);
        }

        public override void CellDisplayingEnded(UICollectionView collectionView, UICollectionViewCell cell, NSIndexPath indexPath) =>
            ((PhotoCell)cell).Hide();

        public override void Scrolled(UIScrollView scrollView)
        {
            if (owner._updating && (scrollView.Tracking || scrollView.Dragging || scrollView.Decelerating))
                owner._requestedOffset = scrollView.ContentOffset.Y;
            var state = owner.VirtualView.Feed.State;
            if (owner._connected && state.Items.Count > 0 &&
                scrollView.ContentOffset.Y + scrollView.Bounds.Height > scrollView.ContentSize.Height - 600)
                _ = owner.VirtualView.DemandNextAsync();
        }
    }

    [Register("BespokePhotoCell")]
    public sealed class PhotoCell : UICollectionViewCell
    {
        public const string Reuse = "bespoke-photo";
        readonly UIImageView _image = new() { ContentMode = UIViewContentMode.ScaleAspectFill, ClipsToBounds = true };
        readonly UILabel _caption = new()
        {
            Lines = 2,
            Font = UIFont.SystemFontOfSize(14) ?? throw new InvalidOperationException("The system caption font is unavailable."),
            TextColor = UIColor.White
        };
        PhotoMasonryHandler? _owner;
        UIImage? _bitmap;
        CancellationTokenSource? _binding;
        int _generation;
        long _cost;
        public long Key { get; private set; }
        public long ImageKey { get; private set; }
        public long Epoch { get; private set; }
        public string Caption => _caption.Text ?? "";

        [Export("initWithFrame:")]
        public PhotoCell(CGRect frame) : base(frame)
        {
            ContentView.AddSubview(_image);
            ContentView.AddSubview(_caption);
            ContentView.BackgroundColor = UIColor.FromRGB(38, 58, 82);
            IsAccessibilityElement = true;
        }

        public void Bind(PhotoMasonryHandler owner, PhotoItem item)
        {
            Unbind();
            _owner = owner;
            owner.VirtualView.Counters.Binds++;
            if (Tag == 0) { owner.VirtualView.Counters.Created++; Tag = 1; }
            Key = item.Id;
            Epoch = owner._epoch;
            _caption.Text = item.Caption;
            AccessibilityLabel = item.Caption;
            AccessibilityIdentifier = $"photo-{item.Id}";
        }

        public void Show(PhotoItem item)
        {
            var owner = _owner ?? throw new InvalidOperationException("The displayed cell is not bound.");
            if (_binding is not null) return;
            _binding = new();
            var generation = ++_generation;
            var width = Math.Clamp((int)((owner._layout?.Geometry?.Width ?? 390) /
                (owner._layout?.Geometry?.ColumnCount ?? 2) * UIScreen.MainScreen.Scale), 64, 384);
            _ = LoadImageAsync(owner, item, width, generation, Epoch, _binding.Token);
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            _image.Frame = new(0, 0, ContentView.Bounds.Width, Math.Max(0, ContentView.Bounds.Height - 48));
            _caption.Frame = new(6, Math.Max(0, ContentView.Bounds.Height - 44), Math.Max(0, ContentView.Bounds.Width - 12), 40);
        }

        async Task LoadImageAsync(PhotoMasonryHandler owner, PhotoItem item, int width, int generation, long epoch, CancellationToken token)
        {
            UIImage? prepared = null;
            var view = owner.VirtualView;
            var decode = owner._decode ?? throw new InvalidOperationException("The image decoder is disconnected.");
            try
            {
                var bytes = await view.Thumbnails.GetAsync(item, width, token).ConfigureAwait(false);
                prepared = await decode.RunAsync<UIImage>(async () =>
                {
                    var raw = await Task.Run(() =>
                    {
                        using var data = NSData.FromArray(bytes);
                        return UIImage.LoadFromData(data) ?? throw new InvalidDataException("The fixture thumbnail is not an image.");
                    }, token).ConfigureAwait(false);
                    var preparation = new TaskCompletionSource<UIImage>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (!view.Dispatcher.Dispatch(async () =>
                    {
                        try
                        {
                            var result = await raw.PrepareForDisplayAsync()
                                ?? throw new InvalidDataException("Thumbnail preparation failed.");
                            if (!ReferenceEquals(raw, result)) raw.Dispose();
                            preparation.TrySetResult(result);
                        }
                        catch (Exception exception)
                        {
                            raw.Dispose();
                            preparation.TrySetException(exception);
                        }
                    }))
                    {
                        raw.Dispose();
                        throw new InvalidOperationException("The image dispatcher rejected preparation.");
                    }
                    return await preparation.Task.ConfigureAwait(false);
                }, token).ConfigureAwait(false);
                var image = prepared;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!view.Dispatcher.Dispatch(() =>
                {
                    try
                    {
                        if (!owner._connected || token.IsCancellationRequested || generation != _generation || Key != item.Id ||
                            epoch != owner._epoch || !ReferenceEquals(_owner, owner))
                        {
                            view.Counters.Stale++;
                            image.Dispose();
                        }
                        else
                        {
                            var cost = checked((long)(image.CGImage?.Width ?? 0) * (long)(image.CGImage?.Height ?? 0) * 4);
                            view.Counters.AddDecoded(cost);
                            _cost = cost;
                            _bitmap = image;
                            _image.Image = image;
                            ImageKey = item.Id;
                        }
                        completion.TrySetResult();
                    }
                    catch (Exception exception) { image.Dispose(); completion.TrySetException(exception); }
                }))
                    throw new InvalidOperationException("The image dispatcher rejected publication.");
                prepared = null;
                await completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Interlocked.Increment(ref view.Counters.Errors);
                view.Logger.LogError(exception, "UIKit thumbnail {Key} failed", item.Id);
            }
            finally { prepared?.Dispose(); }
        }

        public void Hide()
        {
            _generation++;
            _binding?.Cancel();
            _binding?.Dispose();
            _binding = null;
            _image.Image = null;
            _bitmap?.Dispose();
            _bitmap = null;
            if (_owner is not null)
            {
                _owner.VirtualView.Counters.RemoveDecoded(_cost);
                _owner._displayedCells.Remove(this);
            }
            _cost = 0;
            ImageKey = 0;
        }

        public void Unbind()
        {
            Hide();
            if (_owner is not null) _owner.VirtualView.Counters.Recycled++;
            _owner = null;
            Key = ImageKey = Epoch = 0;
        }

        public override void PrepareForReuse() { Unbind(); base.PrepareForReuse(); }
    }
}
