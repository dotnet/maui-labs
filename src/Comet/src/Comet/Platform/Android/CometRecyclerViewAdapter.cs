using AndroidX.RecyclerView.Widget;
using Android.Views;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet.Android.Controls
{
	public class CometRecyclerViewAdapter : RecyclerView.Adapter
	{
		public IMauiContext MauiContext{get;set;}
		public IListView ListView { get; set; }

		//TODO: Account for Section
		public override int ItemCount => ListView?.Rows(0) ?? 0;

		public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
		{
			//TODO: Account for Section
			var view = ListView?.ViewFor(0, position);
			var cell = view?.ToPlatform(MauiContext);

			if (holder is CometRecyclerViewHolder rvh && cell is not null)
			{
				Logger.Debug($"OnBindViewHolder");

				var parent = rvh.Parent;
				var density = MauiContext.Context.Resources.DisplayMetrics.Density;

				// Use laid-out dimensions first, fall back to measured dimensions,
				// then to screen dimensions as a last resort.
				var parentWidth = parent.Width > 0 ? parent.Width
					: parent.MeasuredWidth > 0 ? parent.MeasuredWidth
					: MauiContext.Context.Resources.DisplayMetrics.WidthPixels;
				var parentHeight = parent.Height > 0 ? parent.Height
					: parent.MeasuredHeight > 0 ? parent.MeasuredHeight
					: MauiContext.Context.Resources.DisplayMetrics.HeightPixels;

				var scaledSize = new Size(parentWidth / density, parentHeight / density);
				var measuredSize = view.Measure(scaledSize, true);
				view.MeasuredSize = measuredSize;
				view.MeasurementValid = true;

				var itemHeight = (int)(measuredSize.Height * density);
				if (itemHeight <= 0) itemHeight = (int)(48 * density); // fallback minimum height

				rvh.CometView.LayoutParameters = new ViewGroup.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					itemHeight);

				// Add our view to the cell.
				rvh.CometView.CurrentView = view;

				// Disable click/focus on child views so the item click listener fires
				DisableChildClicks(rvh.CometView);
			}
			else
			{
				Logger.Warn("Should never happen.");
			}

		}

		public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
		{
			return new CometRecyclerViewHolder(parent, ListView, MauiContext);
		}

		static void DisableChildClicks(ViewGroup parent)
		{
			for (int i = 0; i < parent.ChildCount; i++)
			{
				var child = parent.GetChildAt(i);
				if (child is not null)
				{
					child.Clickable = false;
					child.Focusable = false;
					if (child is ViewGroup vg)
						DisableChildClicks(vg);
				}
			}
		}
	}
}