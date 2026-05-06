using Microsoft.Maui.Platforms.MacOS.Platform;
using Microsoft.Maui.Hosting;
using AppKit;
using Foundation;

namespace DevFlow.Sample;

[Register("Program")]
public class Program : MacOSMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public static void Main(string[] args)
	{
		NSApplication.Init();
		NSApplication.SharedApplication.Delegate = new Program();
		NSApplication.Main(args);
	}
}
