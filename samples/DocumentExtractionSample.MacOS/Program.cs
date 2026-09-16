using AppKit;
using Foundation;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace DocumentExtractionSample;

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
