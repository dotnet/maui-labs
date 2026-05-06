using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	internal interface INativeHostHandler
	{
		object GetNativeView();
		void SyncNativeView();
		Size MeasureNativeView(Size availableSize);
	}

	public partial class NativeHostHandler
	{
		public static IPropertyMapper<NativeHost, NativeHostHandler> Mapper =
			new PropertyMapper<NativeHost, NativeHostHandler>(ViewHandler.ViewMapper);
	}
}
