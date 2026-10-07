using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("Shell navigation")]
public class ShellNavigationRuntimeTests
{
	[GtkRuntimeFact]
	public void SectionNavigation_UpdatesTheMappedNotebookPage()
	{
		var app = new NavigationTestHost();
		app.Run([]);
		Assert.Null(app.Failure);
		Assert.True(app.Completed);
	}

	sealed class NavigationTestHost : GtkMauiApplication
	{
		public Exception? Failure { get; private set; }
		public bool Completed { get; private set; }
		protected override string ApplicationId => "com.maui.tests.shellnavigation";
		protected override bool CreateDesktopEntry => false;
		protected override MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<TestApplication>().Build();

		protected override void OnStarted()
		{
			GLib.Functions.IdleAdd(0, () =>
			{
				RunAssertions();
				return false;
			});
		}

		async void RunAssertions()
		{
			var previousContext = SynchronizationContext.Current;
			SynchronizationContext.SetSynchronizationContext(new GtkTestSynchronizationContext());
			var window = ((TestApplication)Application).Windows[0];
			var shell = (Shell)window.Page!;
			try
			{
				var section = shell.CurrentItem.CurrentItem;
				var root = (Page)section.CurrentItem.Content;
				await AssertDisplayed(shell, root, "root");

				await shell.GoToAsync("//workspace/home/main/detail", false);
				var routed = section.Stack[^1];
				Assert.IsType<DetailPage>(routed);
				await AssertDisplayed(shell, routed, "absolute-route");

				var pushed = new ContentPage { Content = new Label { Text = "PushAsync detail" } };
				await section.Navigation.PushAsync(pushed, false);
				await AssertDisplayed(shell, pushed, "push");

				await section.Navigation.PopAsync(false);
				await AssertDisplayed(shell, routed, "pop");

				await section.Navigation.PushAsync(new ContentPage(), false);
				await section.Navigation.PopToRootAsync(false);
				await AssertDisplayed(shell, root, "pop-to-root");

				await shell.GoToAsync("detail", false);
				await shell.GoToAsync("..", false);
				await AssertDisplayed(shell, root, "relative-back");

				var first = new ContentPage { Content = new Label { Text = "First detail" } };
				var second = new ContentPage { Content = new Label { Text = "Reentrant detail" } };
				Task? nestedPush = null;
				first.HandlerChanged += (_, _) =>
				{
					if (first.Handler != null)
						nestedPush = ((NavigationTestSection)section).PushDirect(second);
				};
				await ((NavigationTestSection)section).PushDirect(first);
				Assert.NotNull(nestedPush);
				await nestedPush;
				await AssertDisplayed(shell, second, "same-section-reentrant-push");
				await section.Navigation.PopToRootAsync(false);
				await AssertDisplayed(shell, root, "reentrant-pop-to-root");

				var originalItem = shell.CurrentItem;
				var otherRoot = new ContentPage { Content = new Label { Text = "Other item" } };
				var otherItem = new ShellItem { Route = "other" };
				var otherSection = new ShellSection();
				otherSection.Items.Add(new ShellContent { Content = otherRoot });
				otherItem.Items.Add(otherSection);
				shell.Items.Add(otherItem);
				var redirect = new ContentPage { Content = new Label { Text = "Redirected detail" } };
				redirect.HandlerChanged += (_, _) =>
				{
					if (redirect.Handler != null)
						shell.CurrentItem = otherItem;
				};
				await section.Navigation.PushAsync(redirect, false);
				shell.CurrentItem = originalItem;
				await AssertDisplayed(shell, redirect, "handler-redirect-return");
				await section.Navigation.PopToRootAsync(false);
				await AssertDisplayed(shell, root, "redirect-pop-to-root");

				var replacement = new Shell();
				var replacementRoot = new ContentPage { Content = new Label { Text = "Replacement Shell" } };
				replacement.Items.Add(new ShellContent { Content = replacementRoot });
				var handler = shell.Handler!;
				handler.SetVirtualView(replacement);
				await AssertDisplayed(replacement, replacementRoot, "handler-rebind");
				handler.DisconnectHandler();
				Completed = true;
			}
			catch (Exception ex)
			{
				Failure = ex;
				Console.WriteLine(ex);
			}
			finally
			{
				Routing.UnRegisterRoute("detail");
				SynchronizationContext.SetSynchronizationContext(previousContext);
				((Gtk.Window)window.Handler!.PlatformView!).Close();
			}
		}

		static async Task AssertDisplayed(Shell shell, Page page, string phase)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (DateTime.UtcNow < deadline)
			{
				if (page.Handler?.PlatformView is Gtk.Widget widget && widget.GetMapped() && widget.GetWidth() > 0 && widget.GetHeight() > 0)
				{
					var root = (Gtk.Box)shell.Handler!.PlatformView!;
					var paned = Assert.IsType<Gtk.Paned>(root.GetFirstChild());
					var notebook = Assert.IsType<Gtk.Notebook>(paned.GetEndChild());
					var selected = notebook.GetNthPage(notebook.GetCurrentPage());
					Assert.True(selected == widget || selected == widget.GetParent(), $"Wrong notebook child at {phase}");
					Console.WriteLine($"GTK-NAV {phase}: mapped={widget.GetMapped()} width={widget.GetWidth()} height={widget.GetHeight()} route={shell.CurrentState.Location}");
					return;
				}

				var frame = new TaskCompletionSource();
				GLib.Functions.TimeoutAdd(0, 16, () => { frame.SetResult(); return false; });
				await frame.Task;
			}
			Assert.Fail($"Page was not mapped with positive allocation at {phase}; route={shell.CurrentState.Location}");
		}
	}

	sealed class GtkTestSynchronizationContext : SynchronizationContext
	{
		public override void Post(SendOrPostCallback callback, object? state) =>
			GLib.Functions.IdleAdd(0, () => { callback(state); return false; });
	}

	public sealed class TestApplication : Application
	{
		protected override Window CreateWindow(IActivationState? activationState)
		{
			Routing.RegisterRoute("detail", typeof(DetailPage));
			var root = new ContentPage { Content = new Label { Text = "Shell navigation root" } };
			var section = new NavigationTestSection { Route = "home", Title = "Home" };
			section.Items.Add(new ShellContent { Route = "main", Content = root });
			var item = new ShellItem { Route = "workspace" };
			item.Items.Add(section);
			var shell = new Shell();
			shell.Items.Add(item);
			return new Window(shell);
		}
	}

	public sealed class DetailPage : ContentPage
	{
		public DetailPage() => Content = new Label { Text = "Routed detail page" };
	}

	sealed class NavigationTestSection : ShellSection
	{
		public Task PushDirect(Page page) => OnPushAsync(page, false);
	}
}
