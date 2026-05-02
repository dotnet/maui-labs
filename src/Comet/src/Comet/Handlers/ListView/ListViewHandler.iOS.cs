using System;
using Foundation;
using UIKit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui;
using Comet.iOS;


namespace Comet.Handlers
{
	public partial class ListViewHandler : ViewHandler<IListView, CUITableView>
	{
		

		public static void MapListViewProperty(IElementHandler viewHandler, IListView virtualView)
		{
			var PlatformView = (CUITableView)viewHandler.PlatformView;
			PlatformView.ListView = virtualView;
			PlatformView.SizeToFit();
		}

#nullable enable
		public static void MapReloadData(ListViewHandler viewHandler, IListView virtualView, object? value)
#nullable restore
		{
			var PlatformView = (CUITableView)viewHandler.PlatformView;
			PlatformView?.ReloadData();
		}

		protected override CUITableView CreatePlatformView() =>  new CUITableView(MauiContext);
	}
}
