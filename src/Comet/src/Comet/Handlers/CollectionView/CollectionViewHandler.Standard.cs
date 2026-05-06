using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	public partial class CollectionViewHandler : ViewHandler<IListView, object>
	{
		public static void MapListViewProperty(IElementHandler handler, IListView virtualView)
		{
		}

		public static void MapReloadData(CollectionViewHandler handler, IListView virtualView, object? value)
		{
		}

		protected override object CreatePlatformView() => throw new NotImplementedException();
	}
}
