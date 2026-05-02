
namespace Microsoft.Maui.Handlers.WPF
{
	public partial class PageHandler : ContentViewHandler
	{
		public static void MapTitle(PageHandler handler, IContentView page)
		{
			if (page is ITitledElement titled && !string.IsNullOrEmpty(titled.Title))
			{
				var wpfWindow = System.Windows.Application.Current?.MainWindow;
				if (wpfWindow != null)
					wpfWindow.Title = titled.Title;
			}
		}
	}
}
