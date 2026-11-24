#if NET11_0_OR_GREATER
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public sealed class ModalNavigationRuntimeTests
{
	[GtkRuntimeFact]
	public async Task PublicModalPlatform_PushesAndPopsInlineContent()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>().Build();
		var context = new GtkMauiContext(app.Services);
		var rootPage = new ContentPage { Content = new Label { Text = "root" } };
		var mauiWindow = new Window(rootPage);
		var handler = mauiWindow.ToHandler(context);
		var gtkWindow = Assert.IsType<Gtk.Window>(handler.PlatformView, exactMatch: false);
		var container = Assert.IsType<WindowRootViewContainer>(gtkWindow.GetChild(), exactMatch: false);
		var modal = new ContentPage { Content = new Label { Text = "modal" } };
		GtkPage.SetModalPresentationStyle(modal, GtkModalPresentationStyle.Inline);
		var host = new TestModalHost(mauiWindow, context, rootPage, modal);
		using var platform = new GtkModalNavigationPlatform(host);

		await platform.PushModalAsync(modal, animated: false);
		Assert.Same(modal.Handler?.PlatformView, container.GetLastChild());

		await platform.PopModalAsync(modal, animated: false);
		Assert.NotSame(modal.Handler?.PlatformView, container.GetLastChild());

		gtkWindow.SetChild(null);
		gtkWindow.Close();
		handler.DisconnectHandler();
	}

	sealed class TestModalHost(
		Window window,
		IMauiContext context,
		Page rootPage,
		Page modal) : IModalNavigationHost
	{
		readonly IReadOnlyList<Page> _stack = new[] { modal };

		public Window Window { get; } = window;
		public IMauiContext MauiContext { get; } = context;
		public IReadOnlyList<Page> PlatformModalStack => _stack;
		public Page? CurrentPage => modal;
		public Page CurrentPlatformPage => rootPage;
		public bool IsWindowReady => true;
		public bool IsBatchPopping => false;
		public bool IsBatchPushing => false;
		public void RequestSync()
		{
		}
	}
}
#endif
