using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using WGrid = System.Windows.Controls.Grid;
using Dispatcher = System.Windows.Threading.Dispatcher;
using Label = Microsoft.Maui.Controls.Label;

namespace HandlerTests;

[Collection("Shell handlers")]
public class ShellItemsHandlerTests
{
	[Fact]
	public void RuntimeWindow_DynamicHierarchy_RendersAddRemoveReplaceAndReset()
	{
		Run((shell, handler) =>
		{
			shell.FlyoutBehavior = FlyoutBehavior.Locked;
			var window = new System.Windows.Window
			{
				Title = "Shell dynamic items regression",
				Content = handler.PlatformView,
				Width = 800,
				Height = 600,
				Left = -20000,
				Top = -20000,
				ShowActivated = false,
				ShowInTaskbar = false,
			};
			try
			{
				window.Show();
				var first = Item("First");
				var second = Item("Second");
				shell.Items.Add(first);
				shell.Items.Add(second);
				Render("01-flyout-add", first, second);
				AssertCurrentPage(shell, handler);

				shell.Items.Remove(second);
				Render("02-flyout-remove", first);
				AssertCurrentPage(shell, handler);

				var replacement = Item("Replacement");
				shell.Items[0] = replacement;
				Render("03-flyout-replace", replacement);
				AssertCurrentPage(shell, handler);

				shell.Items.Clear();
				DrainDispatcher();
				Assert.Null(ContentHost(handler).Content);
				Render("04-reset");

				var a = Section("A");
				var b = Section("B");
				var tabs = new TabBar { Items = { a, b } };
				shell.Items.Add(tabs);
				Render("05-tabbar-add");
				AssertTabs(handler, a, b);
				AssertCurrentPage(shell, handler);

				var c = Section("C");
				tabs.Items[1] = c;
				Render("06-tab-replace");
				AssertTabs(handler, a, c);

				tabs.Items.Remove(c);
				Render("07-tab-remove");
				Assert.Equal(System.Windows.Visibility.Collapsed, Tabs(handler).Visibility);

				tabs.Items.Clear();
				Render("08-tabs-reset");
				Assert.Null(ContentHost(handler).Content);
			}
			finally
			{
				window.Close();
			}

			void Render(string name, params ShellItem[] items)
			{
				DrainDispatcher();
				window.UpdateLayout();
				AssertFlyout(handler, items);
				Assert.True(handler.PlatformView.ActualWidth > 0);
				Assert.True(handler.PlatformView.ActualHeight > 0);
				var directory = Environment.GetEnvironmentVariable("SHELL_ITEMS_EVIDENCE_DIRECTORY");
				if (string.IsNullOrEmpty(directory))
					return;
				System.IO.Directory.CreateDirectory(directory);
				var image = new System.Windows.Media.Imaging.RenderTargetBitmap(
					(int)handler.PlatformView.ActualWidth, (int)handler.PlatformView.ActualHeight,
					96, 96, System.Windows.Media.PixelFormats.Pbgra32);
				image.Render(handler.PlatformView);
				var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
				encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
				using var file = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png"));
				encoder.Save(file);
			}
		});
	}

	[Fact]
	public void Items_AddRemoveReplaceReorderReset_UpdatesNativeFlyoutAndPage()
	{
		Run((shell, handler) =>
		{
			var first = Item("First");
			shell.Items.Add(first);
			DrainDispatcher();
			AssertFlyout(handler, first);
			AssertCurrentPage(shell, handler);

			var second = Item("Second");
			shell.Items.Add(second);
			DrainDispatcher();
			AssertFlyout(handler, first, second);

			shell.Items.Remove(second);
			shell.Items.Insert(0, second);
			DrainDispatcher();
			AssertFlyout(handler, second, first);

			var replacement = Item("Replacement");
			shell.Items[0] = replacement;
			DrainDispatcher();
			AssertFlyout(handler, replacement, first);

			shell.Items.Remove(first);
			DrainDispatcher();
			AssertFlyout(handler, replacement);
			AssertCurrentPage(shell, handler);

			shell.Items.Clear();
			DrainDispatcher();
			AssertFlyout(handler);
			Assert.Null(ContentHost(handler).Content);
			Assert.Equal(System.Windows.Visibility.Collapsed, Tabs(handler).Visibility);
		});
	}

	[Fact]
	public void Items_ClearThenAddTabBar_ReplacesFlyoutWithTabsWithoutCreatingInactivePages()
	{
		Run((shell, handler) =>
		{
			shell.Items.Add(Item("Old A"));
			shell.Items.Add(Item("Old B"));
			DrainDispatcher();
			var inactiveCreated = 0;
			var first = Section("A");
			var second = new Tab
			{
				Title = "B",
				Items = { new ShellContent { ContentTemplate = new DataTemplate(() =>
				{
					inactiveCreated++;
					return new ContentPage();
				}) } },
			};
			shell.Items.Clear();
			shell.Items.Add(new TabBar { Items = { first, second } });
			DrainDispatcher();

			AssertFlyout(handler);
			AssertTabs(handler, first, second);
			AssertCurrentPage(shell, handler);
			Assert.Equal(0, inactiveCreated);
		});
	}

	[Fact]
	public void Sections_AddRemoveReplaceReorderReset_UpdatesNativeTabs()
	{
		Run((shell, handler) =>
		{
			var item = Item("Item");
			shell.Items.Add(item);
			DrainDispatcher();
			var first = item.Items[0];
			var second = Section("Second");
			item.Items.Add(second);
			DrainDispatcher();
			AssertTabs(handler, first, second);

			item.Items.Remove(second);
			item.Items.Insert(0, second);
			DrainDispatcher();
			AssertTabs(handler, second, first);

			var replacement = Section("Replacement");
			item.Items[0] = replacement;
			DrainDispatcher();
			AssertTabs(handler, replacement, first);

			item.Items.Remove(first);
			DrainDispatcher();
			Assert.Equal(System.Windows.Visibility.Collapsed, Tabs(handler).Visibility);
			AssertCurrentPage(shell, handler);

			item.Items.Clear();
			DrainDispatcher();
			Assert.Empty(Tabs(handler).Items);
			Assert.Null(ContentHost(handler).Content);

			item.Items.Add(Section("After reset"));
			DrainDispatcher();
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void Contents_ReplaceRemoveResetAdd_UpdatesVisiblePage()
	{
		Run((shell, handler) =>
		{
			var item = Item("Item");
			shell.Items.Add(item);
			DrainDispatcher();
			var section = item.Items[0];
			var replacement = Content("Replacement");
			section.Items[0] = replacement;
			DrainDispatcher();
			Assert.Same(((IShellContentController)replacement).Page, shell.CurrentPage);
			AssertCurrentPage(shell, handler);

			var next = Content("Next");
			section.Items.Add(next);
			section.Items.Remove(replacement);
			DrainDispatcher();
			Assert.Same(((IShellContentController)next).Page, shell.CurrentPage);
			AssertCurrentPage(shell, handler);

			section.Items.Clear();
			DrainDispatcher();
			Assert.Null(ContentHost(handler).Content);
			section.Items.Add(Content("After reset"));
			DrainDispatcher();
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void DetachedCollections_AndDisconnectedHandler_DoNotRebuildNativeItems()
	{
		Run((shell, handler) =>
		{
			var removed = Item("Removed");
			shell.Items.Add(removed);
			shell.Items.Add(Item("Retained"));
			DrainDispatcher();
			var removedSection = removed.Items[0];
			shell.Items.Remove(removed);
			DrainDispatcher();
			var button = Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>());
			removed.Items.Add(Section("Detached"));
			removedSection.Items.Add(Content("Detached content"));
			DrainDispatcher();
			Assert.Same(button, Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>()));

			var flyout = Flyout(handler);
			shell.Items[0].Items.Add(Section("Pending"));
			((IElementHandler)handler).DisconnectHandler();
			shell.Items.Add(Item("Disconnected"));
			DrainDispatcher();
			Assert.Same(button, Assert.Single(flyout.Children.Cast<System.Windows.Controls.Button>()));
		});
	}

	[Fact]
	public void SetVirtualView_RebindsCollectionsAndCancelsOldPendingRefresh()
	{
		Run((oldShell, handler) =>
		{
			oldShell.Items.Add(Item("Old"));
			var shell = new Shell { Items = { Item("New") } };
			handler.SetVirtualView(shell);
			DrainDispatcher();
			AssertFlyout(handler, shell.Items[0]);
			var button = Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>());
			oldShell.Items.Add(Item("Detached"));
			DrainDispatcher();
			Assert.Same(button, Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>()));
			shell.Items.Add(Item("Added"));
			DrainDispatcher();
			AssertFlyout(handler, shell.Items.ToArray());
			AssertCurrentPage(shell, handler);
		});
	}

	static ShellItem Item(string title) => new FlyoutItem { Title = title, Items = { Section(title) } };
	static ShellSection Section(string title) => new Tab { Title = title, Items = { Content(title) } };
	static ShellContent Content(string title) => new()
	{
		Title = title,
		ContentTemplate = new DataTemplate(() => new ContentPage
		{
			Title = title,
			BackgroundColor = Microsoft.Maui.Graphics.Colors.White,
			Content = new Label { Text = title },
		}),
	};

	static WGrid MainGrid(ShellHandler handler) => (WGrid)handler.PlatformView.Children[1];
	static TabControl Tabs(ShellHandler handler) => MainGrid(handler).Children.OfType<TabControl>().Single();
	static ContentControl ContentHost(ShellHandler handler) => MainGrid(handler).Children.OfType<ContentControl>().Single();
	static System.Windows.Controls.StackPanel Flyout(ShellHandler handler)
		=> (System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)
			((WGrid)handler.PlatformView.Children[0]).Children[1]).Content;

	static void AssertFlyout(ShellHandler handler, params ShellItem[] items)
		=> Assert.Equal(items, Flyout(handler).Children.Cast<System.Windows.Controls.Button>().Select(button => button.Tag));

	static void AssertTabs(ShellHandler handler, params ShellSection[] sections)
	{
		Assert.Equal(sections, Tabs(handler).Items.Cast<TabItem>().Select(tab => tab.Tag));
		Assert.Equal(System.Windows.Visibility.Visible, Tabs(handler).Visibility);
	}

	static void AssertCurrentPage(Shell shell, ShellHandler handler)
	{
		Assert.NotNull(shell.CurrentPage);
		Assert.NotNull(shell.CurrentPage.Handler?.PlatformView);
		Assert.Same(shell.CurrentPage.Handler.PlatformView, ContentHost(handler).Content);
	}

	static void DrainDispatcher()
	{
		var frame = new DispatcherFrame();
		Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
			new Action(() => frame.Continue = false));
		Dispatcher.PushFrame(frame);
	}

	static void Run(Action<Shell, ShellHandler> test)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				_ = Dispatcher.CurrentDispatcher;
				DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
				using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>().Build();
				var handler = new ShellHandler();
				handler.SetMauiContext(new WPFMauiContext(app.Services));
				var shell = new Shell();
				try
				{
					handler.SetVirtualView(shell);
					DrainDispatcher();
					test(shell, handler);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			}
			catch (Exception exception)
			{
				failure = exception;
			}
			finally
			{
				Dispatcher.CurrentDispatcher.InvokeShutdown();
			}
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The STA test did not finish within the timeout.");
		if (failure != null)
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
