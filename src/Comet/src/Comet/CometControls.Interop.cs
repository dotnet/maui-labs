using System;
using Microsoft.Maui;

namespace Comet
{
	public static partial class CometControls
	{
		public static NativeHost NativeHost(Func<IMauiContext, object> factory, bool ownsNativeView = true)
			=> new NativeHost(factory, ownsNativeView);

		public static NativeHost NativeHost(object nativeView, bool ownsNativeView = false)
			=> new NativeHost(nativeView, ownsNativeView);
	}
}
