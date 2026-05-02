using AppKit;

namespace CometControlsGallery
{
	public static class MainClass
	{
		static void Main(string[] args)
		{
			NSApplication.Init();
			NSApplication.SharedApplication.Delegate = new MauiMacOSApp();
			NSApplication.Main(args);
		}
	}
}
