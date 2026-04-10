using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A pull-to-refresh container that wraps content and provides refresh behavior.
	/// </summary>
	public partial class RefreshView : View, IEnumerable, IContainerView, IContentView
	{
		IEnumerator IEnumerable.GetEnumerator() => new[] { Content }.GetEnumerator();
		public View Content { get; set; }

		object IContentView.Content => Content;
		IView IContentView.PresentedContent => Content;
		Thickness IPadding.Padding => this.GetPadding();

		public virtual void Add(View view)
		{
			if (view is null) return;
			view.Parent = this;
			view.Navigation = Parent?.Navigation;
			Content = view;
			TypeHashCode = view.GetContentTypeHashCode();
		}

		protected override void OnParentChange(View parent)
		{
			base.OnParentChange(parent);
			if (Content is not null) Content.Parent = this;
		}

		internal override void ContextPropertyChanged(string property, object value, bool cascades)
		{
			base.ContextPropertyChanged(property, value, cascades);
			Content?.ContextPropertyChanged(property, value, cascades);
		}

		protected override void Dispose(bool disposing)
		{
			Content?.Dispose();
			Content = null;
			base.Dispose(disposing);
		}

		public override void LayoutSubviews(Rect frame)
		{
			this.Frame = frame;
			Content?.LayoutSubviews(frame);
		}

		public override Size GetDesiredSize(Size availableSize)
		{
			if (Content is not null)
			{
				var margin = Content.GetMargin();
				availableSize.Width -= margin.HorizontalThickness;
				availableSize.Height -= margin.VerticalThickness;
				MeasuredSize = Content.Measure(availableSize, true);
				return MeasuredSize;
			}
			return base.GetDesiredSize(availableSize);
		}

		internal override void Reload(bool isHotReload)
		{
			Content?.Reload(isHotReload);
			base.Reload(isHotReload);
		}

		public IReadOnlyList<View> GetChildren() => new[] { Content };

		Size ICrossPlatformLayout.CrossPlatformMeasure(double widthConstraint, double heightConstraint) =>
			this.Measure(widthConstraint, heightConstraint);

		Size ICrossPlatformLayout.CrossPlatformArrange(Rect bounds)
		{
			if (!this.MeasurementValid) Measure(bounds.Width, bounds.Height);
			this.LayoutSubviews(bounds);
			return this.MeasuredSize;
		}

		Size IContentView.CrossPlatformMeasure(double widthConstraint, double heightConstraint) =>
			this.Measure(widthConstraint, heightConstraint);

		Size IContentView.CrossPlatformArrange(Rect bounds) =>
			((ICrossPlatformLayout)this).CrossPlatformArrange(bounds);
	}
}
