#nullable enable
using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WGrid = System.Windows.Controls.Grid;
using WColor = System.Windows.Media.Color;
using WColumnDefinition = System.Windows.Controls.ColumnDefinition;
using WGridLength = System.Windows.GridLength;
using WGridUnitType = System.Windows.GridUnitType;
using WVisibility = System.Windows.Visibility;

namespace Microsoft.Maui.Handlers.WPF
{
	public class FlyoutContainerView : WGrid
	{
		readonly System.Windows.Controls.ContentControl _flyoutArea;
		readonly System.Windows.Controls.GridSplitter _splitter;
		readonly System.Windows.Controls.ContentControl _detailArea;

		double _flyoutWidth = 240;

		public event EventHandler<bool>? IsPresentedChanged;

		public FlyoutContainerView()
		{
			ColumnDefinitions.Add(new WColumnDefinition { Width = new WGridLength(_flyoutWidth) });
			ColumnDefinitions.Add(new WColumnDefinition { Width = new WGridLength(1, WGridUnitType.Auto) });
			ColumnDefinitions.Add(new WColumnDefinition { Width = new WGridLength(1, WGridUnitType.Star) });

			_flyoutArea = new System.Windows.Controls.ContentControl
			{
				HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
				VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch,
			};
			SetColumn(_flyoutArea, 0);

			_splitter = new System.Windows.Controls.GridSplitter
			{
				Width = 4,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
				Background = new System.Windows.Media.SolidColorBrush(WColor.FromRgb(200, 200, 200)),
			};
			SetColumn(_splitter, 1);

			_detailArea = new System.Windows.Controls.ContentControl
			{
				HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
				VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch,
			};
			SetColumn(_detailArea, 2);

			Children.Add(_flyoutArea);
			Children.Add(_splitter);
			Children.Add(_detailArea);

			_splitter.DragCompleted += OnSplitterDragCompleted;
		}

		void OnSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
		{
			// Detect if user collapsed the flyout by dragging the splitter
			bool isPresented = ColumnDefinitions[0].ActualWidth > 10;
			IsPresentedChanged?.Invoke(this, isPresented);
		}

		public void ShowFlyout(System.Windows.FrameworkElement? view) => _flyoutArea.Content = view;
		public void ShowDetail(System.Windows.FrameworkElement? view) => _detailArea.Content = view;

		public void SetFlyoutVisible(bool visible)
		{
			if (visible)
			{
				ColumnDefinitions[0].Width = new WGridLength(_flyoutWidth);
				_splitter.Visibility = WVisibility.Visible;
			}
			else
			{
				ColumnDefinitions[0].Width = new WGridLength(0);
				_splitter.Visibility = WVisibility.Collapsed;
			}
		}

		public void SetFlyoutWidth(double width)
		{
			_flyoutWidth = width > 0 ? width : 240;
			ColumnDefinitions[0].Width = new WGridLength(_flyoutWidth);
		}
	}

	public partial class FlyoutViewHandler : WPFViewHandler<IFlyoutView, FlyoutContainerView>
	{
		public static readonly IPropertyMapper<IFlyoutView, FlyoutViewHandler> Mapper =
			new PropertyMapper<IFlyoutView, FlyoutViewHandler>(ViewMapper)
			{
				[nameof(IFlyoutView.Flyout)] = MapFlyout,
				[nameof(IFlyoutView.Detail)] = MapDetail,
				[nameof(IFlyoutView.IsPresented)] = MapIsPresented,
				[nameof(IFlyoutView.FlyoutBehavior)] = MapFlyoutBehavior,
				[nameof(IFlyoutView.FlyoutWidth)] = MapFlyoutWidth,
			};

		public FlyoutViewHandler() : base(Mapper) { }

		protected override FlyoutContainerView CreatePlatformView() => new FlyoutContainerView();

		protected override void ConnectHandler(FlyoutContainerView platformView)
		{
			base.ConnectHandler(platformView);
			platformView.IsPresentedChanged += OnIsPresentedChanged;
		}

		protected override void DisconnectHandler(FlyoutContainerView platformView)
		{
			platformView.IsPresentedChanged -= OnIsPresentedChanged;
			base.DisconnectHandler(platformView);
		}

		void OnIsPresentedChanged(object? sender, bool isPresented)
		{
			if (VirtualView is Microsoft.Maui.Controls.FlyoutPage fp && fp.IsPresented != isPresented)
				fp.IsPresented = isPresented;
		}

		public static void MapFlyout(FlyoutViewHandler handler, IFlyoutView view)
		{
			if (handler.MauiContext == null || view.Flyout == null) return;
			var pv = ElementExtensions.ToPlatform((IElement)view.Flyout, handler.MauiContext);
			handler.PlatformView.ShowFlyout(pv as System.Windows.FrameworkElement);
		}

		public static void MapDetail(FlyoutViewHandler handler, IFlyoutView view)
		{
			if (handler.MauiContext == null || view.Detail == null) return;
			var pv = ElementExtensions.ToPlatform((IElement)view.Detail, handler.MauiContext);
			handler.PlatformView.ShowDetail(pv as System.Windows.FrameworkElement);
		}

		public static void MapIsPresented(FlyoutViewHandler handler, IFlyoutView view)
		{
			handler.PlatformView.SetFlyoutVisible(view.IsPresented);
		}

		public static void MapFlyoutBehavior(FlyoutViewHandler handler, IFlyoutView view)
		{
			switch (view.FlyoutBehavior)
			{
				case FlyoutBehavior.Disabled:
					handler.PlatformView.SetFlyoutVisible(false);
					break;
				case FlyoutBehavior.Locked:
					handler.PlatformView.SetFlyoutVisible(true);
					break;
				case FlyoutBehavior.Flyout:
					handler.PlatformView.SetFlyoutVisible(view.IsPresented);
					break;
			}
		}

		public static void MapFlyoutWidth(FlyoutViewHandler handler, IFlyoutView view)
		{
			handler.PlatformView.SetFlyoutWidth(view.FlyoutWidth);
		}
	}
}
