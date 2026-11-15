using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

// UseMauiAppLinuxGtk4 registers GtkDispatcher as the application dispatcher and applies
// ConfigureEssentials, whose initializer bridges MainThread and AppInfo from DI.
[Collection("GTK runtime")]
public class MainThreadBridgeTests(ITestOutputHelper output)
{
	[Fact]
	public void Build_RegistersGtkApplicationDispatcherAndAppInfoFallback()
	{
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<TestApplication>().Build();

		Assert.IsType<GtkDispatcher>(app.Services.GetRequiredKeyedService<IDispatcher>(typeof(IApplication)));
		var appInfo = Assert.IsType<GtkAppInfoImplementation>(app.Services.GetRequiredService<IAppInfo>());
		Assert.Same(appInfo, AppInfo.Current);
	}

	[Fact]
	public void Build_MainThreadUsesTheApplicationDispatcher()
	{
		var dispatcher = new RecordingDispatcher();
		var builder = MauiApp.CreateBuilder();
		// Registered first, so the backend's TryAdd keeps it: verifies the bridge is wired
		// by UseMauiAppLinuxGtk4 without needing a native GLib main context.
		builder.Services.AddKeyedSingleton<IDispatcher>(typeof(IApplication), dispatcher);
		using var app = builder.UseMauiAppLinuxGtk4<TestApplication>().Build();

		dispatcher.IsDispatchRequired = false;
		Assert.True(MainThread.IsMainThread);
		dispatcher.IsDispatchRequired = true;
		Assert.False(MainThread.IsMainThread);

		var ran = false;
		MainThread.BeginInvokeOnMainThread(() => ran = true);
		Assert.True(ran);
		Assert.Equal(1, dispatcher.Dispatches);
	}

	[GtkRuntimeFact]
	public void Run_MainThread_ReportsAndMarshalsToTheGtkThread()
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var app = new TestPlatformApplication(output);
				app.Run([]);
				Assert.Null(app.Failure);
				Assert.True(app.Completed, "MainThread.BeginInvokeOnMainThread did not run on the GTK thread.");
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		}) { IsBackground = true };

		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "GTK main loop did not exit within two minutes.");
		Assert.Null(failure);
	}

	sealed class TestPlatformApplication(ITestOutputHelper output) : GtkMauiApplication
	{
		public Exception? Failure { get; private set; }
		public bool Completed { get; private set; }
		protected override string ApplicationId => $"com.maui.mainthreadtest.p{Environment.ProcessId}";
		protected override bool CreateDesktopEntry => false;

		protected override MauiApp CreateMauiApp() =>
			MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<TestApplication>().Build();

		protected override void OnStarted()
		{
			var gtkThread = Environment.CurrentManagedThreadId;
			try
			{
				Assert.True(MainThread.IsMainThread);
				Task.Run(() =>
				{
					var background = MainThread.IsMainThread;
					MainThread.BeginInvokeOnMainThread(() =>
					{
						try
						{
							Assert.False(background);
							Assert.Equal(gtkThread, Environment.CurrentManagedThreadId);
							Assert.True(MainThread.IsMainThread);
							output.WriteLine($"MainThread marshalled to GTK thread {gtkThread}");
							Completed = true;
						}
						catch (Exception ex)
						{
							Failure = ex;
						}
						finally
						{
							Gio.Application.GetDefault()!.Quit();
						}
					});
				});
			}
			catch (Exception ex)
			{
				Failure = ex;
				Gio.Application.GetDefault()!.Quit();
			}
		}
	}

	public sealed class TestApplication : Application
	{
		protected override Window CreateWindow(IActivationState? activationState) => new(new ContentPage());
	}

	sealed class RecordingDispatcher : IDispatcher
	{
		public bool IsDispatchRequired { get; set; }
		public int Dispatches { get; private set; }

		public bool Dispatch(Action action)
		{
			Dispatches++;
			action();
			return true;
		}

		public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);
		public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
	}
}
