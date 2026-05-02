using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;

namespace Comet
{
	public abstract class AbstractLayout : ContainerView, ILayout
	{
		ILayoutManager layout;
		protected abstract ILayoutManager CreateLayoutManager();
		public ILayoutManager LayoutManager => layout ??= CreateLayoutManager();
		public ILayoutHandler LayoutHandler => ViewHandler as ILayoutHandler;

		Thickness IPadding.Padding => GetDefaultPadding();

		bool ILayout.ClipsToBounds { get; }

		protected override void OnAdded(View view)
		{
			LayoutHandler?.Add(view);
			InvalidateMeasurement();
		}

		protected override void OnClear(List<View> views)
		{
			views?.ForEach(x => LayoutHandler?.Remove(x));
			InvalidateMeasurement();
		}

		protected override void OnRemoved(View view)
		{
			LayoutHandler?.Remove(view);
			InvalidateMeasurement();
		}

		protected override void OnInsert(int index, View item) {
			LayoutHandler.Add(item);
			InvalidateMeasurement();
		}

		// Override to prevent the base View.LayoutSubviews cascade which gives
		// every child the parent's full frame. Instead, route through
		// CrossPlatformArrange so the LayoutManager positions children correctly.
		public override void LayoutSubviews(Rect frame)
		{
			this.SetFrameFromPlatformView(frame);
			CrossPlatformArrange(frame);
		}

		// Default layout padding for Comet layouts (matches the legacy Style.LayoutPadding
		// default of 6pt). Spacing tokens from the theme system will supersede this once
		// layout-level token wiring is implemented.
		protected virtual Thickness GetDefaultPadding() => new Thickness(6);

		Size lastMeasureSize;
		public override Size GetDesiredSize(Size availableSize)
		{
			if (MeasurementValid && availableSize == lastMeasureSize)
				return MeasuredSize;
			lastMeasureSize = availableSize;
			var frameConstraints = this.GetFrameConstraints();

			var layoutVerticalSizing = ((IView)this).VerticalLayoutAlignment;
			var layoutHorizontalSizing = ((IView)this).HorizontalLayoutAlignment;


			double widthConstraint = frameConstraints?.Width > 0 ? frameConstraints.Width.Value : availableSize.Width;
			double heightConstraint = frameConstraints?.Height > 0 ? frameConstraints.Height.Value : availableSize.Height;

			//Lets adjust for padding

			var padding = this.GetPadding(GetDefaultPadding());
			if (!double.IsInfinity(widthConstraint))
				widthConstraint -= padding.HorizontalThickness;
			if (!double.IsInfinity(heightConstraint))
				heightConstraint -= padding.VerticalThickness;


			var measured = LayoutManager.Measure(widthConstraint, heightConstraint);


			if (frameConstraints?.Height > 0 && frameConstraints?.Width > 0)
			{
				measured = new Size(frameConstraints.Width.Value, frameConstraints.Height.Value);
			}
			else
			{
				if (layoutVerticalSizing == LayoutAlignment.Fill && !double.IsInfinity(heightConstraint))
					measured.Height = heightConstraint;
				if (layoutHorizontalSizing == LayoutAlignment.Fill && !double.IsInfinity(widthConstraint))
					measured.Width = widthConstraint;

				if (!double.IsInfinity(measured.Width))
					measured.Width += padding.HorizontalThickness;
				if (!double.IsInfinity(measured.Height))
					measured.Height += padding.VerticalThickness;

				// Apply individual frame constraints — Frame size is the
				// final size (including padding), matching View.GetDesiredSize.
				if (frameConstraints?.Width > 0)
					measured.Width = frameConstraints.Width.Value;
				if (frameConstraints?.Height > 0)
					measured.Height = frameConstraints.Height.Value;
			}

			var margin = this.GetMargin();
			if (!double.IsInfinity(measured.Width))
				measured.Width += margin.HorizontalThickness;
			if (!double.IsInfinity(measured.Height))
				measured.Height += margin.VerticalThickness;
			return MeasuredSize = measured;
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			//LayoutManager?.Invalidate();
		}

		/// <summary>
		/// When a handler is transferred during DiffUpdate (UpdateFromOldView),
		/// the LayoutHandler's SetVirtualView is called with the new layout.
		/// MAUI's LayoutHandler may not fully re-sync its platform children
		/// in SetVirtualView (it only adds new children, doesn't remove stale
		/// ones). Force a full rebuild by clearing all platform children first,
		/// then re-adding from the current virtual child list.
		/// </summary>
		/// <summary>
		/// Placeholder for post-handler-transfer sync. The actual platform
		/// child synchronization is done by CometHostHandler.Reload after the
		/// entire diff completes, to avoid re-entrant view manipulation during
		/// the recursive diff pass.
		/// </summary>
		protected override void OnHandlerChange()
		{
			base.OnHandlerChange();
		}

		Rect lastRect;
		public virtual Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) => GetDesiredSize(new Size(widthConstraint,heightConstraint));
		public virtual Size CrossPlatformArrange(Rect bounds) {
			if(bounds != lastRect)
			{
				Measure(bounds.Width,bounds.Height);
			}
			lastRect = bounds;
			var padding = this.GetPadding(GetDefaultPadding()); ;
			var b = bounds.ApplyPadding(padding);
			LayoutManager?.ArrangeChildren(b);
			return this.MeasuredSize;
		}
	}
}
