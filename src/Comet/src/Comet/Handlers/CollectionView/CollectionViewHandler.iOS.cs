using System;
using CoreGraphics;
using UIKit;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Comet.Handlers
{
	public partial class CollectionViewHandler : ViewHandler<IListView, CollectionViewHandler.CollectionViewContainer>
	{
		protected override CollectionViewContainer CreatePlatformView() => new CollectionViewContainer();

		public static void MapListViewProperty(IElementHandler handler, IListView virtualView)
		{
			var cvHandler = (CollectionViewHandler)handler;
			cvHandler._currentListViewRef = new WeakReference<IListView>(virtualView);

			// Reuse existing MAUI CollectionView — only update changed properties.
			// This preserves scroll position and selection state across body rebuilds.
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
				var platformView = _mauiItemsView.ToPlatform(MauiContext);
				PlatformView.SetContent(platformView, _mauiItemsView);

				// MAUI's ItemsViewController forces ContentInsetAdjustmentBehavior = Never,
				// which prevents the collection view from adjusting for the navigation bar.
				// Override to Automatic so content starts below the nav bar and scrolls
				// behind it with the standard iOS translucent glass effect.
				SetAutomaticContentInsets(platformView);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[CollectionViewHandler] EmbedMauiItemsView failed: {ex.Message}");
			}
		}

		static void SetAutomaticContentInsets(UIView view)
		{
			if (view is UIKit.UIScrollView scrollView)
			{
				scrollView.ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Automatic;
				return;
			}
			foreach (var subview in view.Subviews)
				SetAutomaticContentInsets(subview);
		}

		protected override void DisconnectHandler(CollectionViewContainer platformView)
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
			if (_mauiItemsView is not null)
			{
				var platformView = _mauiItemsView.Handler?.PlatformView as UIView;
				if (platformView is not null)
				{
					var fitting = platformView.SizeThatFits(new CoreGraphics.CGSize(
						double.IsInfinity(widthConstraint) ? double.MaxValue : widthConstraint,
						double.IsInfinity(heightConstraint) ? double.MaxValue : heightConstraint));
					var w = double.IsInfinity(widthConstraint) ? fitting.Width : widthConstraint;
					var h = double.IsInfinity(heightConstraint) ? fitting.Height : heightConstraint;
					if (w > 0 && h > 0)
						return new Microsoft.Maui.Graphics.Size(w, h);
				}
			}
			return new Microsoft.Maui.Graphics.Size(
				double.IsInfinity(widthConstraint) ? 400 : widthConstraint,
				double.IsInfinity(heightConstraint) ? 600 : heightConstraint);
		}

		public class CollectionViewContainer : UIView
		{
			UIView _contentView;
			IView _virtualView;

			public CollectionViewContainer() { ClipsToBounds = true; }

			public void SetContent(UIView platformView, IView virtualView)
			{
				_contentView?.RemoveFromSuperview();
				_contentView = platformView;
				_virtualView = virtualView;
				if (_contentView is not null)
				{
					_contentView.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
					AddSubview(_contentView);
					SetNeedsLayout();
				}
			}

			public void ClearContent()
			{
				_contentView?.RemoveFromSuperview();
				_contentView = null;
				_virtualView = null;
			}

			public override void LayoutSubviews()
			{
				base.LayoutSubviews();
				if (_contentView is null || Bounds.Width <= 0 || Bounds.Height <= 0)
					return;

				_virtualView?.Measure(Bounds.Width, Bounds.Height);
				_virtualView?.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, Bounds.Width, Bounds.Height));
				_contentView.Frame = Bounds;
			}

			public override CGSize SizeThatFits(CGSize size) => size;
		}
	}
}
