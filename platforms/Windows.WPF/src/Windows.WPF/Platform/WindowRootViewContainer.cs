using System;
using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
using Window = System.Windows.Window;
using Point = System.Windows.Point;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	internal class WindowRootViewContainer : Panel
	{
		FrameworkElement? _topPage;
		protected override Size MeasureOverride(Size availableSize)
		{
			var width = availableSize.Width;
			var height = availableSize.Height;
			var window = Window.GetWindow(this);

			if (double.IsInfinity(width))
				width = window?.ActualWidth ?? 800;

			if (double.IsInfinity(height))
				height = window?.ActualHeight ?? 600;

			var size = new Size(width, height);

			foreach (var child in Children)
			{
				if (child is UIElement fe)
					fe.Measure(size);
			}

			return size;
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			foreach (var child in Children)
			{
				if (child is UIElement fe)
					fe.Arrange(new Rect(new Point(0, 0), finalSize));
			}

			return finalSize;
		}

		internal void AddPage(FrameworkElement pageView)
		{
			if (!Children.Contains(pageView))
			{
				int indexOFTopPage = 0;
				if (_topPage != null)
					indexOFTopPage = Children.IndexOf(_topPage) + 1;

				Children.Insert(indexOFTopPage, pageView);
				_topPage = pageView;
			}
		}

		internal void RemovePage(FrameworkElement pageView)
		{
			int indexOFTopPage = -1;
			if (_topPage != null)
				indexOFTopPage = Children.IndexOf(_topPage) - 1;

			Children.Remove(pageView);

			if (indexOFTopPage >= 0)
				_topPage = (FrameworkElement)Children[indexOFTopPage];
			else
				_topPage = null;
		}

		internal void AddOverlay(FrameworkElement overlayView)
		{
			if (!Children.Contains(overlayView))
				Children.Add(overlayView);
		}

		internal void RemoveOverlay(FrameworkElement overlayView)
		{
			Children.Remove(overlayView);
		}
	}
}