using System;
using Android.App;
using Android.Runtime;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Go.CompanionApp;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
	{
		// CRITICAL: Must be set before the runtime initializes MetadataUpdater.
		Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "Debug");
	}

	protected override MauiApp CreateMauiApp() => GoApp.CreateMauiApp();
}
