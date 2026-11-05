
namespace Microsoft.Maui.Handlers.WPF
{
	public partial class PageHandler : ContentViewHandler
	{
		public static void MapTitle(PageHandler handler, IContentView page)
		{
			// WindowHandler owns the native title bar; page titles belong to navigation headers.
		}
	}
}
