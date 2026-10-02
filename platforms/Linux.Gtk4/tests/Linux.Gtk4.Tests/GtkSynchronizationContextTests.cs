using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class GtkSynchronizationContextTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void Run_AsyncButtonHandler_ResumesOnGtkThreadAndRestoresContext()
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				Assert.Null(SynchronizationContext.Current);
				var app = new TestPlatformApplication(output);
				app.Run([]);
				Assert.Null(SynchronizationContext.Current);
				Assert.Null(app.Failure);
				Assert.True(app.Completed, "The asynchronous button handler did not complete.");
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

	private sealed class TestPlatformApplication(ITestOutputHelper output) : GtkMauiApplication
	{
		public Exception? Failure { get; private set; }
		public bool Completed { get; private set; }
		protected override string ApplicationId => $"com.maui.synchronizationtest.p{Environment.ProcessId}";
		protected override bool CreateDesktopEntry => false;

		protected override MauiApp CreateMauiApp() =>
			MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<TestApplication>().Build();

		protected override void OnStarted()
		{
			var app = (TestApplication)Application;
			var gtkThread = Environment.CurrentManagedThreadId;
			var context = SynchronizationContext.Current;
			output.WriteLine($"Startup: thread={gtkThread}, context={context?.GetType().FullName ?? "null"}");

			var watchdog = GLib.Functions.TimeoutAdd(0, 10000, () =>
			{
				Failure = new TimeoutException("The asynchronous button handler did not complete within 10 seconds.");
				Gio.Application.GetDefault()!.Quit();
				return false;
			});

			app.Button.Clicked += OnClicked;
			GLib.Functions.IdleAdd(0, () =>
			{
				((IButtonController)app.Button).SendClicked();
				return false;
			});

			async void OnClicked(object? sender, EventArgs args)
			{
				try
				{
					Assert.Equal(gtkThread, Environment.CurrentManagedThreadId);
					await Task.Delay(100);
					output.WriteLine($"After await: thread={Environment.CurrentManagedThreadId}, context={SynchronizationContext.Current?.GetType().FullName ?? "null"}");
					Assert.Equal(gtkThread, Environment.CurrentManagedThreadId);
					Assert.NotNull(context);
					Assert.Same(context, SynchronizationContext.Current);
					Assert.False(app.Status.Dispatcher.IsDispatchRequired);
					app.Status.Text = "Done";
					Assert.Equal("Done", Assert.IsType<Gtk.Label>(app.Status.Handler!.PlatformView).GetText());

					await Task.Delay(50);
					Assert.Equal(gtkThread, Environment.CurrentManagedThreadId);
					Assert.Same(context, SynchronizationContext.Current);
					app.Status.Text = "Done again";
					Assert.Equal("Done again", Assert.IsType<Gtk.Label>(app.Status.Handler.PlatformView).GetText());
					Completed = true;
				}
				catch (Exception ex)
				{
					Failure = ex;
				}
				finally
				{
					// The failing baseline resumes on the pool: even cleanup must be marshalled.
					GLib.Functions.IdleAdd(0, () =>
					{
						GLib.Internal.Source.Remove(watchdog);
						app.Button.Clicked -= OnClicked;
						Gio.Application.GetDefault()!.Quit();
						return false;
					});
				}
			}
		}
	}

	public sealed class TestApplication : Application
	{
		public Button Button { get; } = new() { Text = "Await" };
		public Label Status { get; } = new() { Text = "Waiting" };

		protected override Window CreateWindow(IActivationState? activationState) =>
			new(new ContentPage { Content = new VerticalStackLayout { Children = { Button, Status } } });
	}
}
