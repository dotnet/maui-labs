#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	public class ContentPanel : Panel
	{
		FrameworkElement? _content;

		internal FrameworkElement? Content
		{
			get => _content;
			set
			{
				_content = value;
				AddContent(_content);
			}
		}

		internal Func<double, double, Microsoft.Maui.Graphics.Size>? CrossPlatformMeasure { get; set; }
		internal Func<Graphics.Rect, Microsoft.Maui.Graphics.Size>? CrossPlatformArrange { get; set; }

		protected override global::System.Windows.Size MeasureOverride(global::System.Windows.Size availableSize)
		{
			if (CrossPlatformMeasure == null || (availableSize.Width * availableSize.Height == 0))
			{
				return base.MeasureOverride(availableSize);
			}

			var measure = CrossPlatformMeasure(availableSize.Width, availableSize.Height);

			// WPF requires all children to be measured during MeasureOverride.
			var resultSize = new Size(measure.Width, measure.Height);
			foreach (System.Windows.UIElement child in Children)
			{
				child.Measure(resultSize);
			}

			return resultSize;
		}

		protected override global::System.Windows.Size ArrangeOverride(global::System.Windows.Size finalSize)
		{
			if (CrossPlatformArrange == null)
			{
				return base.ArrangeOverride(finalSize);
			}

			var width = finalSize.Width;
			var height = finalSize.Height;

			var actual = CrossPlatformArrange(new Graphics.Rect(0, 0, width, height));

			return new global::System.Windows.Size(Math.Max(0, actual.Width), Math.Max(0, actual.Height));
		}

		public ContentPanel()
		{
		}

		void AddContent(FrameworkElement? content)
		{
			if (content == null)
				return;

			if (!Children.Contains(_content))
				Children.Add(_content);
		}
	}
}