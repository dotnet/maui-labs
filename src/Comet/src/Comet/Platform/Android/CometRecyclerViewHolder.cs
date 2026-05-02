using System;
using AndroidX.RecyclerView.Widget;
using Android.Views;
using Android.Widget;
using Microsoft.Maui;
namespace Comet.Android.Controls
{
	public class CometRecyclerViewHolder : RecyclerView.ViewHolder, global::Android.Views.View.IOnClickListener
	{
		private readonly IListView listView;
		public IMauiContext MauiContext {get;set;}
		public ViewGroup Parent { get; }
		public CometView CometView => (CometView)ItemView;

		public CometRecyclerViewHolder(
			ViewGroup parent,
			IListView listView, IMauiContext mauiContext) : base(new CometView(mauiContext))
		{
			MauiContext  = mauiContext;
			Parent = parent;
			this.listView = listView;

			ItemView.Clickable = true;
			ItemView.SetOnClickListener(this);
		}

		public void OnClick(global::Android.Views.View v) => listView?.OnSelected(0, BindingAdapterPosition);
	}
}