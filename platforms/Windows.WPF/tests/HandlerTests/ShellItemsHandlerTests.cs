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
	[Theory]
	[InlineData(nameof(Shell.BackgroundColor), false)]
	[InlineData(nameof(Shell.FlyoutBackgroundColor), false)]
	[InlineData(nameof(Shell.FlyoutBackground), false)]
	[InlineData(nameof(Shell.BackgroundColor), true)]
	public void BackgroundAppendToMapping_ItemsOnlyRefresh_PreservesNativeCustomization(string key, bool mutateOnceInCallback)
	{
		var originalMapper = ShellHandler.Mapper;
		var mapper = new PropertyMapper<Shell, ShellHandler>(originalMapper);
		string[] backgroundKeys = [nameof(Shell.BackgroundColor), nameof(Shell.FlyoutBackgroundColor), nameof(Shell.FlyoutBackground)];
		var replayedKeys = new List<string>();
		var unrelatedCalls = 0;
		System.Windows.Media.Brush? customBrush = null;
		Action? mutateItems = null;
		foreach (var backgroundKey in backgroundKeys)
		{
			mapper.AppendToMapping(backgroundKey, (handler, _) =>
			{
				replayedKeys.Add(backgroundKey);
				if (backgroundKey != key)
					return;
				customBrush ??= new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Magenta);
				if (key == nameof(Shell.BackgroundColor))
					handler.PlatformView.SetBarBackground(customBrush);
				else
					handler.PlatformView.SetFlyoutBackground(customBrush);
				var mutation = mutateItems;
				mutateItems = null;
				mutation?.Invoke();
			});
		}
		mapper.AppendToMapping(nameof(Shell.FlyoutWidth), (_, _) => unrelatedCalls++);
		try
		{
			ShellHandler.Mapper = mapper;
			Run((shell, handler) =>
			{
				var item = Item("Initial");
				shell.Items.Add(item);
				shell.BackgroundColor = Microsoft.Maui.Graphics.Colors.Red;
				shell.FlyoutBackgroundColor = Microsoft.Maui.Graphics.Colors.Blue;
				if (key == nameof(Shell.FlyoutBackground))
					shell.FlyoutBackground = new Microsoft.Maui.Controls.SolidColorBrush(Microsoft.Maui.Graphics.Colors.Green);
				DrainDispatcher();

				// Establish a valid customization without relying on initial mapper enumeration order.
				handler.UpdateValue(key);
				DrainDispatcher();
				Assert.NotNull(customBrush);
				Assert.Same(customBrush, NativeBackground(handler, key));
				Assert.Contains(key, replayedKeys);
				var background = shell.BackgroundColor;
				var flyoutColor = shell.FlyoutBackgroundColor;
				var flyoutBrush = shell.FlyoutBackground;
				var changedBackgrounds = new List<string>();
				System.ComponentModel.PropertyChangedEventHandler onChanged = (_, args) =>
				{
					if (args.PropertyName is string name && backgroundKeys.Contains(name))
						changedBackgrounds.Add(name);
				};
				shell.PropertyChanged += onChanged;
				try
				{
					replayedKeys.Clear();
					var previousUnrelatedCalls = unrelatedCalls;
					if (mutateOnceInCallback)
						mutateItems = () => item.Items.Add(Section("Added by callback"));
					item.Items.Add(Section("Added by test"));
					DrainDispatcher();

					Assert.Same(background, shell.BackgroundColor);
					Assert.Same(flyoutColor, shell.FlyoutBackgroundColor);
					Assert.Same(flyoutBrush, shell.FlyoutBackground);
					Assert.Empty(changedBackgrounds);
					Assert.Equal(previousUnrelatedCalls, unrelatedCalls);
					Assert.Same(customBrush, NativeBackground(handler, key));
					Assert.Equal(mutateOnceInCallback ? backgroundKeys.Concat(backgroundKeys) : backgroundKeys, replayedKeys);
					Assert.Null(mutateItems);
					Assert.Equal(mutateOnceInCallback ? 3 : 2, item.Items.Count);
					AssertTabs(handler, item.Items.ToArray());
					AssertCurrentPage(shell, handler);
				}
				finally
				{
					shell.PropertyChanged -= onChanged;
				}
			});
		}
		finally
		{
			ShellHandler.Mapper = originalMapper;
		}
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	public void BackgroundMappings_ItemsOnlyRefresh_PreservesUncustomizedColorsAndPrecedence(bool themeDefaults, bool useFlyoutBrush)
	{
		Run((shell, handler) =>
		{
			var item = Item("Initial");
			shell.Items.Add(item);
			if (!themeDefaults)
			{
				shell.BackgroundColor = Microsoft.Maui.Graphics.Colors.Red;
				shell.FlyoutBackgroundColor = Microsoft.Maui.Graphics.Colors.Blue;
			}
			if (useFlyoutBrush)
				shell.FlyoutBackground = new Microsoft.Maui.Controls.SolidColorBrush(Microsoft.Maui.Graphics.Colors.Green);
			if (themeDefaults)
				shell.FlyoutBackground = null;
			DrainDispatcher();
			handler.UpdateValue(nameof(Shell.BackgroundColor));
			handler.UpdateValue(nameof(Shell.FlyoutBackgroundColor));
			handler.UpdateValue(nameof(Shell.FlyoutBackground));
			var toolbarColor = Assert.IsType<System.Windows.Media.SolidColorBrush>(NativeBackground(handler, nameof(Shell.BackgroundColor))).Color;
			var flyoutColor = Assert.IsType<System.Windows.Media.SolidColorBrush>(NativeBackground(handler, nameof(Shell.FlyoutBackground))).Color;
			if (themeDefaults)
			{
				Assert.Null(shell.BackgroundColor);
				Assert.Null(shell.FlyoutBackgroundColor);
				Assert.Null(shell.FlyoutBackground);
			}
			else
			{
				Assert.Equal(System.Windows.Media.Colors.Red, toolbarColor);
				Assert.Equal(useFlyoutBrush ? System.Windows.Media.Colors.Green : System.Windows.Media.Colors.Blue, flyoutColor);
			}

			item.Items.Add(Section("Added"));
			DrainDispatcher();
			Assert.Equal(toolbarColor, Assert.IsType<System.Windows.Media.SolidColorBrush>(NativeBackground(handler, nameof(Shell.BackgroundColor))).Color);
			Assert.Equal(flyoutColor, Assert.IsType<System.Windows.Media.SolidColorBrush>(NativeBackground(handler, nameof(Shell.FlyoutBackground))).Color);
			AssertTabs(handler, item.Items.ToArray());
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void RuntimeWindow_ItemsAppendToMapping_CustomizesDeferredCollectionRefresh()
	{
		var originalMapper = ShellHandler.Mapper;
		var mapper = new PropertyMapper<Shell, ShellHandler>(originalMapper);
		var calls = 0;
		mapper.AppendToMapping(nameof(Shell.Items), (handler, _) =>
		{
			calls++;
			foreach (var tab in Tabs(handler).Items.Cast<TabItem>())
				tab.Header = $"Custom {Assert.IsAssignableFrom<ShellSection>(tab.Tag).Title}";
		});
		try
		{
			ShellHandler.Mapper = mapper;
			Run((shell, handler) =>
			{
				var window = new System.Windows.Window
				{
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
					var first = Section("First");
					var second = Section("Second");
					var item = new TabBar { Items = { first, second } };
					shell.Items.Add(item);
					DrainDispatcher();
					var previousCalls = calls;

					var added = Section("Added");
					item.Items.Add(added);
					AssertCustomized("09-mapper-add", first, second, added);

					var replacement = Section("Replacement");
					item.Items[2] = replacement;
					AssertCustomized("10-mapper-replace", first, second, replacement);

					item.Items.Remove(replacement);
					AssertCustomized("11-mapper-remove", first, second);

					item.Items.Clear();
					item.Items.Add(first);
					item.Items.Add(second);
					AssertCustomized("12-mapper-reset-add", first, second);

					void AssertCustomized(string name, params ShellSection[] sections)
					{
						DrainDispatcher();
						Assert.True(calls > previousCalls, "Deferred collection refresh bypassed the Items mapper customization.");
						previousCalls = calls;
						AssertTabs(handler, sections);
						Assert.Equal(sections.Select(section => $"Custom {section.Title}"),
							Tabs(handler).Items.Cast<TabItem>().Select(tab => Assert.IsType<string>(tab.Header)));
						AssertCurrentPage(shell, handler);
						window.UpdateLayout();
						Assert.True(handler.PlatformView.ActualWidth > 0);
						Assert.True(handler.PlatformView.ActualHeight > 0);
						SaveRuntimeEvidence(handler, name);
					}
				}
				finally
				{
					window.Close();
				}
			});
		}
		finally
		{
			ShellHandler.Mapper = originalMapper;
		}
	}

	[Fact]
	public void RootSubscriptions_AttachRebindAndReconnect_RegisterEachCallbackOnce()
	{
		Run((original, handler) =>
		{
			original.Items.Add(Item("Original"));
			DrainDispatcher();
			AssertRootSubscriptions(original, handler, connected: true);
			handler.SetVirtualView(original);
			handler.SetVirtualView(original);
			AssertRootSubscriptions(original, handler, connected: true);

			var replacement = new Shell { Items = { Item("Replacement") } };
			handler.SetVirtualView(replacement);
			DrainDispatcher();
			AssertRootSubscriptions(original, handler, connected: false);
			AssertRootSubscriptions(replacement, handler, connected: true);
			AssertCurrentPage(replacement, handler);

			((IElementHandler)handler).DisconnectHandler();
			AssertRootSubscriptions(original, handler, connected: false);
			AssertRootSubscriptions(replacement, handler, connected: false);

			handler.SetVirtualView(replacement);
			handler.SetVirtualView(replacement);
			DrainDispatcher();
			AssertRootSubscriptions(replacement, handler, connected: true);
			replacement.Items.Add(Item("After reconnect"));
			DrainDispatcher();
			AssertFlyout(handler, replacement.Items.ToArray());
			AssertCurrentPage(replacement, handler);

			((IElementHandler)handler).DisconnectHandler();
			AssertRootSubscriptions(replacement, handler, connected: false);
		});
	}

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
				SaveRuntimeEvidence(handler, name);
			}
		});
	}

	[Fact]
	public void DynamicSections_SelectionChanges_KeepTabsAndPageSynchronized()
	{
		Run((shell, handler) =>
		{
			var item = Item("Initial");
			item.Items.Add(Section("Second"));
			shell.Items.Add(item);
			DrainDispatcher();

			var inserted = Section("Inserted");
			item.Items.Add(inserted);
			item.CurrentItem = inserted;
			DrainDispatcher();
			AssertTabs(handler, item.Items.ToArray());
			Assert.Same(inserted, Assert.IsType<TabItem>(Tabs(handler).SelectedItem).Tag);
			AssertCurrentPage(shell, handler);

			var content = Content("Inserted content");
			inserted.Items.Add(content);
			inserted.CurrentItem = content;
			DrainDispatcher();
			Assert.Same(((IShellContentController)content).Page, shell.CurrentPage);
			AssertCurrentPage(shell, handler);

			item.Items.Remove(inserted);
			DrainDispatcher();
			AssertTabs(handler, item.Items.ToArray());
			Assert.Same(item.CurrentItem, Assert.IsType<TabItem>(Tabs(handler).SelectedItem).Tag);
			AssertCurrentPage(shell, handler);

			var tab = Tabs(handler).SelectedItem;
			inserted.CurrentItem = inserted.Items[0];
			inserted.Items.Add(Content("Detached"));
			DrainDispatcher();
			Assert.Same(tab, Tabs(handler).SelectedItem);
		});
	}

	[Fact]
	public void InactiveSections_Changed_KeepActiveTabsSelectionAndPage()
	{
		Run((shell, handler) =>
		{
			var active = Item("Active");
			var selected = Section("Selected");
			active.Items.Add(selected);
			active.CurrentItem = selected;
			var inactive = Item("Inactive");
			inactive.Items.Add(Section("Other"));
			shell.Items.Add(active);
			shell.Items.Add(inactive);
			DrainDispatcher();
			var page = shell.CurrentPage;

			inactive.Items.Add(Section("Added while inactive"));
			DrainDispatcher();

			AssertFlyout(handler, active, inactive);
			AssertTabs(handler, active.Items.ToArray());
			Assert.Same(selected, Assert.IsType<TabItem>(Tabs(handler).SelectedItem).Tag);
			Assert.Same(page, shell.CurrentPage);
			AssertCurrentPage(shell, handler);
			Assert.All(inactive.Items, section =>
				Assert.Null(((IShellContentController)section.Items[0]).Page));

			shell.CurrentItem = inactive;
			DrainDispatcher();
			AssertTabs(handler, inactive.Items.ToArray());
			Assert.Same(inactive.CurrentItem, Assert.IsType<TabItem>(Tabs(handler).SelectedItem).Tag);
			AssertCurrentPage(shell, handler);
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
	static System.Windows.Media.Brush NativeBackground(ShellHandler handler, string key)
		=> key == nameof(Shell.BackgroundColor)
			? MainGrid(handler).Children.OfType<DockPanel>().Single().Background
			: ((WGrid)handler.PlatformView.Children[0]).Background;
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

	static void AssertRootSubscriptions(Shell shell, ShellHandler handler, bool connected)
	{
		AssertCallbacks(typeof(Shell), nameof(Shell.Navigated), "OnShellNavigated");
		AssertCallbacks(typeof(Shell), nameof(Shell.Navigating), "OnShellNavigating");
		AssertCallbacks(typeof(BindableObject), nameof(Shell.PropertyChanged),
			"OnSelectionShellPropertyChanged", "OnShellPropertyChanged");

		void AssertCallbacks(Type declaringType, string eventName, params string[] expected)
		{
			// Count registrations, not coalesced renders, so duplicate subscriptions cannot hide.
			var field = declaringType.GetField(eventName,
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.NotNull(field);
			var callbacks = (field.GetValue(shell) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
			var owned = callbacks.Where(callback => ReferenceEquals(callback.Target, handler) &&
				callback.Method.DeclaringType == typeof(ShellHandler))
				.Select(callback => callback.Method.Name).OrderBy(name => name).ToArray();
			Assert.Equal(connected ? expected.OrderBy(name => name).ToArray() : Array.Empty<string>(), owned);
		}
	}

	static void SaveRuntimeEvidence(ShellHandler handler, string name)
	{
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
