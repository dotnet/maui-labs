using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Android.App;
using Android.Runtime;

namespace CometAllTheLists
{
	[Application]
	public class MainApplication : MauiApplication
	{
		public MainApplication(IntPtr handle, JniHandleOwnership ownership)
			: base(handle, ownership)
		{
		}

		protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
	}
}
