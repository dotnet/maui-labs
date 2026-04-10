using System;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Comet.MacOS;

namespace Comet.Handlers
{
	public partial class CollectionViewHandler : ViewHandler<IListView, CollectionViewHandler.CollectionViewNSContainer>
	{
		protected override CollectionViewNSContainer CreatePlatformView() => new CollectionViewNSContainer();

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

			try
			{
				var platformView = _mauiItemsView.ToMacOSPlatform(MauiContext);
				PlatformView.SetContent(platformView, _mauiItemsView);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[CollectionViewHandler.MacOS] EmbedMauiItemsView failed: {ex.Message}");
			}
		}

		protected override void DisconnectHandler(CollectionViewNSContainer platformView)
		{
			platformView.ClearContent();
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
			return new Microsoft.Maui.Graphics.Size(
				double.IsInfinity(widthConstraint) ? 400 : widthConstraint,
				double.IsInfinity(heightConstraint) ? 600 : heightConstraint);
		}

		public class CollectionViewNSContainer : NSView
		{
			NSView _contentView;
			IView _virtualView;

			public CollectionViewNSContainer() { WantsLayer = true; }

			public void SetContent(NSView platformView, IView virtualView)
			{
				_contentView?.RemoveFromSuperview();
				_contentView = platformView;
				_virtualView = virtualView;
				if (_contentView is not null)
				{
					_contentView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
					AddSubview(_contentView);
					NeedsLayout = true;
				}
			}

			public void ClearContent()
			{
				_contentView?.RemoveFromSuperview();
				_contentView = null;
				_virtualView = null;
			}

			public override void Layout()
			{
				base.Layout();
				if (_contentView is null || Bounds.Width <= 0 || Bounds.Height <= 0)
					return;

				_virtualView?.Measure(Bounds.Width, Bounds.Height);
				_virtualView?.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, Bounds.Width, Bounds.Height));
				_contentView.Frame = Bounds;
			}
		}
	}
}
