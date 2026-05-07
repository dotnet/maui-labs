using AppKit;

namespace MauiMacOSApp;

public class MainClass
{
	static void Main(string[] args)
	{
		NSApplication.Init();
		NSApplication.SharedApplication.Delegate = new MauiMacOSAppDelegate();
		NSApplication.Main(args);
	}
}
