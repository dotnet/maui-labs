using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AView = global::Android.Views.View;
using AFrameLayout = global::Android.Widget.FrameLayout;
using AViewGroup = global::Android.Views.ViewGroup;
using AMeasureSpecMode = global::Android.Views.MeasureSpecMode;

namespace Comet.Handlers
{
public partial class CollectionViewHandler : ViewHandler<IListView, AFrameLayout>
{
AView _hostedPlatformView;

protected override AFrameLayout CreatePlatformView() => new AFrameLayout(Context);

public static void MapListViewProperty(IElementHandler handler, IListView virtualView)
{
var cvHandler = (CollectionViewHandler)handler;
cvHandler._currentListViewRef = new WeakReference<IListView>(virtualView);

if (cvHandler._mauiItemsView is Microsoft.Maui.Controls.CollectionView existingCv
&& !IsCarouselView(virtualView))
{
UpdateCollectionView(existingCv, virtualView);
return;
}

if (IsCarouselView(virtualView))
{
var carousel = new Microsoft.Maui.Controls.CarouselView();
ConfigureMauiCarouselView(carousel, virtualView);
RefreshItemsSource(carousel, virtualView);
cvHandler._mauiItemsView = carousel;
}
else
{
var cv = new Microsoft.Maui.Controls.CollectionView();
cvHandler.InitCollectionView(cv);
MapCometItemsLayout(cv, virtualView);
MapCometInfiniteScroll(cv, virtualView);
UpdateCollectionView(cv, virtualView);
cvHandler._mauiItemsView = cv;
}
cvHandler.EmbedMauiItemsView();
}

#nullable enable
public static void MapReloadData(CollectionViewHandler handler, IListView virtualView, object? value)
#nullable restore
{
if (handler._mauiItemsView is not null)
RefreshItemsSource(handler._mauiItemsView, virtualView);
}

void EmbedMauiItemsView()
{
if (_mauiItemsView is null || MauiContext is null)
return;

if (_hostedPlatformView is not null)
PlatformView.RemoveView(_hostedPlatformView);

try
{
_hostedPlatformView = _mauiItemsView.ToPlatform(MauiContext);
}
catch (Exception ex)
{
Console.WriteLine($"[CollectionViewHandler] EmbedMauiItemsView failed: {ex.Message}");
return;
}

if (_hostedPlatformView is not null)
{
PlatformView.AddView(_hostedPlatformView,
new AFrameLayout.LayoutParams(
AViewGroup.LayoutParams.MatchParent,
AViewGroup.LayoutParams.MatchParent));
PlatformView.RequestLayout();
}
}

protected override void DisconnectHandler(AFrameLayout platformView)
{
if (_hostedPlatformView is not null)
{
platformView.RemoveView(_hostedPlatformView);
_hostedPlatformView = null;
}
if (_mauiItemsView?.Handler is IElementHandler hostedHandler)
{
hostedHandler.DisconnectHandler();
if (hostedHandler is IDisposable disposable)
disposable.Dispose();
}
_mauiItemsView = null;
base.DisconnectHandler(platformView);
}

public override Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
{
var w = double.IsInfinity(widthConstraint) ? 400 : widthConstraint;
var h = double.IsInfinity(heightConstraint) ? 800 : heightConstraint;

// Measure the platform FrameLayout so its children (MauiRecyclerView) get sized
if (PlatformView is not null && Context is not null)
{
var density = Context.Resources.DisplayMetrics.Density;
PlatformView.Measure(
AView.MeasureSpec.MakeMeasureSpec((int)(w * density), AMeasureSpecMode.Exactly),
AView.MeasureSpec.MakeMeasureSpec((int)(h * density), AMeasureSpecMode.Exactly));
}

return new Microsoft.Maui.Graphics.Size(w, h);
}

public override void PlatformArrange(Microsoft.Maui.Graphics.Rect frame)
{
base.PlatformArrange(frame);

// Ensure the FrameLayout and its MauiRecyclerView child get proper layout
if (PlatformView is not null && Context is not null)
{
var density = Context.Resources.DisplayMetrics.Density;
var widthPx = (int)(frame.Width * density);
var heightPx = (int)(frame.Height * density);
PlatformView.Measure(
AView.MeasureSpec.MakeMeasureSpec(widthPx, AMeasureSpecMode.Exactly),
AView.MeasureSpec.MakeMeasureSpec(heightPx, AMeasureSpecMode.Exactly));
PlatformView.Layout(0, 0, widthPx, heightPx);
}
}
}
}
