using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using WTabControl = System.Windows.Controls.TabControl;
using WTabItem = System.Windows.Controls.TabItem;
using Dispatcher = System.Windows.Threading.Dispatcher;

namespace HandlerTests;

[CollectionDefinition("Shell tab runtime", DisableParallelization = true)]
public sealed class ShellTabRuntimeCollection;

[Collection("Shell tab runtime")]
public class ShellTabNavigationTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NativeTabSelection_WithOrWithoutItemTemplate_NavigatesAndDisplaysPage(bool useTemplate)
	{
		RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
		{
			var navigated = new List<ShellNavigatedEventArgs>();
			shell.Navigated += (_, e) => navigated.Add(e);
			AssertDisplayed(handler, first, "PAGE ONE");
			Assert.Equal(0, created());
			Record(useTemplate, "initial", shell, handler, tabs, navigated.Count);

			if (tabs.Items.Count == 2)
				Select((WTabItem)tabs.Items[1]);
			DrainDispatcher();
			Record(useTemplate, "selected-two", shell, handler, tabs, navigated.Count);

			Assert.Equal(2, tabs.Items.Count);
			Assert.True(tabs.IsVisible);
			Assert.Same(shell.CurrentItem.Items[1], shell.CurrentItem.CurrentItem);
			Assert.EndsWith("/two", shell.CurrentState.Location.OriginalString);
			Assert.Single(navigated);
			Assert.Equal(ShellNavigationSource.ShellSectionChanged, navigated[0].Source);
			Assert.Same(second, shell.CurrentPage);
			AssertDisplayed(handler, second, "PAGE TWO");
			Assert.Equal(1, created());

			Select((WTabItem)tabs.Items[0]);
			DrainDispatcher();
			AssertDisplayed(handler, first, "PAGE ONE");
			Assert.EndsWith("/one", shell.CurrentState.Location.OriginalString);
			Assert.Equal(2, navigated.Count);
			Select((WTabItem)tabs.Items[1]);
			DrainDispatcher();
			AssertDisplayed(handler, second, "PAGE TWO");
			Assert.Equal(1, created());
			Assert.Equal(3, navigated.Count);
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NativeTabSelection_WhenCancelled_RestoresNativeSelection(bool useTemplate)
	{
		RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
		{
			var navigating = 0;
			var navigated = 0;
			shell.Navigating += (_, e) =>
			{
				navigating++;
				Assert.True(e.CanCancel);
				Assert.Equal(ShellNavigationSource.ShellSectionChanged, e.Source);
				e.Cancel();
			};
			shell.Navigated += (_, _) => navigated++;

			Select((WTabItem)tabs.Items[1]);
			DrainDispatcher();

			Assert.Equal(1, navigating);
			Assert.Equal(0, navigated);
			Assert.Same(shell.CurrentItem.Items[0], ((WTabItem)tabs.SelectedItem).Tag);
			Assert.EndsWith("/one", shell.CurrentState.Location.OriginalString);
			AssertDisplayed(handler, first, "PAGE ONE");
			Assert.Equal(0, created());
		});
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void NativeTabSelection_WhenDeferred_WaitsForShellDecision(bool useTemplate, bool cancel)
	{
		RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
		{
			ShellNavigatingEventArgs? proposal = null;
			ShellNavigatingDeferral? deferral = null;
			var navigated = 0;
			shell.Navigating += (_, e) =>
			{
				proposal = e;
				deferral = e.GetDeferral();
			};
			shell.Navigated += (_, _) => navigated++;
			Select((WTabItem)tabs.Items[1]);
			DrainDispatcher();
			Assert.NotNull(deferral);
			Assert.Equal(0, navigated);
			Assert.Same(shell.CurrentItem.Items[0], ((WTabItem)tabs.SelectedItem).Tag);
			AssertDisplayed(handler, first, "PAGE ONE");
			Assert.Equal(0, created());

			if (cancel)
				proposal!.Cancel();
			deferral.Complete();
			DrainDispatcher();

			Assert.Equal(cancel ? 0 : 1, navigated);
			Assert.Equal(cancel ? 0 : 1, created());
			AssertDisplayed(handler, cancel ? first : second, cancel ? "PAGE ONE" : "PAGE TWO");
			Assert.Same(shell.CurrentItem.CurrentItem, ((WTabItem)tabs.SelectedItem).Tag);
			Assert.EndsWith(cancel ? "/one" : "/two", shell.CurrentState.Location.OriginalString);
		});
	}

	[Fact]
	public void NativeTabSelection_WithRenderedFlyoutItemTemplate_NavigatesAndDisplaysPage()
	{
		RunTest(true, (shell, handler, tabs, first, second, created) =>
		{
			Assert.Contains(VisualDescendants(handler.PlatformView).OfType<System.Windows.Controls.TextBlock>(),
				label => label.Text == "templated item" && label.IsVisible && label.ActualWidth > 0);
			var navigated = 0;
			shell.Navigated += (_, _) => navigated++;
			Select((WTabItem)tabs.Items[1]);
			DrainDispatcher();
			Assert.Equal(2, tabs.Items.Count);
			Assert.True(tabs.IsVisible);
			Assert.Same(shell.CurrentItem.Items[1], ((WTabItem)tabs.SelectedItem).Tag);
			Assert.EndsWith("/two", shell.CurrentState.Location.OriginalString);
			Assert.Equal(1, navigated);
			Assert.Equal(1, created());
			AssertDisplayed(handler, second, "PAGE TWO");
		}, useFlyoutItem: true);
	}

	[Fact]
	public void UpdateTabs_WithSecondSectionSelected_DoesNotNavigateBackDuringRebuild()
	{
		RunTest(true, (shell, handler, tabs, first, second, created) =>
		{
			shell.CurrentItem.CurrentItem = shell.CurrentItem.Items[1];
			DrainDispatcher();
			AssertDisplayed(handler, second, "PAGE TWO");
			var navigating = 0;
			var navigated = 0;
			shell.Navigating += (_, _) => navigating++;
			shell.Navigated += (_, _) => navigated++;

			handler.UpdateValue(nameof(Shell.CurrentItem));
			handler.UpdateValue(nameof(Shell.Items));
			DrainDispatcher();

			Assert.Equal(0, navigating);
			Assert.Equal(0, navigated);
			Assert.Same(shell.CurrentItem.Items[1], ((WTabItem)tabs.SelectedItem).Tag);
			AssertDisplayed(handler, second, "PAGE TWO");
			Assert.Equal(1, created());
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NativeTabSelection_WithNoVisibleContent_KeepsCurrentPage(bool emptySection)
	{
		RunTest(true, (shell, handler, tabs, first, second, created) =>
		{
			var item = shell.CurrentItem;
			var original = item.CurrentItem;
			var target = item.Items[1];
			if (emptySection)
			{
				target = new ShellSection { Title = "Empty", Route = "empty" };
				item.Items.Add(target);
			}
			else
			{
				target.CurrentItem.IsVisible = false;
			}
			DrainDispatcher();
			Assert.True(target.IsVisible);
			Assert.Contains(target, item.Items);
			Assert.DoesNotContain(target, ((IShellItemController)item).GetItems());
			var navigating = 0;
			var navigated = 0;
			shell.Navigating += (_, _) => navigating++;
			shell.Navigated += (_, _) => navigated++;
			var tab = tabs.Items.OfType<WTabItem>().FirstOrDefault(tab => ReferenceEquals(tab.Tag, target));
			if (tab is null)
			{
				// Exercise the acceptance guard even if stale/customized native UI proposes a hidden section.
				tab = new WTabItem { Header = target.Title, Tag = target };
				tabs.Items.Add(tab);
			}

			Select(tab);
			DrainDispatcher();

			Assert.Same(original, item.CurrentItem);
			Assert.NotSame(tab, tabs.SelectedItem);
			if (tabs.SelectedItem is WTabItem selected)
				Assert.Same(original, selected.Tag);
			Assert.Equal(0, navigating);
			Assert.Equal(0, navigated);
			Assert.EndsWith("/one", shell.CurrentState.Location.OriginalString);
			AssertDisplayed(handler, first, "PAGE ONE");
			Assert.Equal(0, created());
		});
	}

	[Fact]
	public void NativeTabSelection_AfterDisconnect_DoesNotNavigate()
	{
		RunTest(false, (shell, handler, tabs, first, second, created) =>
		{
			var navigated = 0;
			shell.Navigated += (_, _) => navigated++;
			((IElementHandler)handler).DisconnectHandler();
			Select((WTabItem)tabs.Items[1]);
			DrainDispatcher();
			Assert.Equal(0, navigated);
			Assert.EndsWith("/one", shell.CurrentState.Location.OriginalString);
			Assert.Equal(0, created());
		});
	}

	[Theory]
	[InlineData(false, "section")]
	[InlineData(true, "section")]
	[InlineData(false, "content")]
	[InlineData(true, "content")]
	[InlineData(false, "empty")]
	[InlineData(true, "empty")]
	public void TabPopulation_WhenVisibleSectionsChange_MatchesControllerWithoutRefresh(bool useTemplate, string change)
	{
		RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
		{
			var item = shell.CurrentItem;
			var target = item.Items[1];
			var content = target.Items[0];
			item.Items.Add(new ShellContent
			{
				Title = "Three",
				Route = "three",
				ContentTemplate = new DataTemplate(() => CreatePage("PAGE THREE")),
			});
			DrainDispatcher();
			Assert.Equal(3, tabs.Items.Count);
			var navigated = 0;
			shell.Navigated += (_, _) => navigated++;

			switch (change)
			{
				case "section": target.IsVisible = false; break;
				case "content": content.IsVisible = false; break;
				case "empty": target.Items.Clear(); break;
			}
			DrainDispatcher();
			Record(useTemplate, $"filtered-{change}", shell, handler, tabs, navigated);

			var visible = ((IShellItemController)item).GetItems();
			Assert.DoesNotContain(target, visible);
			Assert.Equal(2, visible.Count);
			Assert.Equal(visible.ToArray(), tabs.Items.OfType<WTabItem>().Select(tab => tab.Tag).Cast<ShellSection>().ToArray());
			Assert.True(tabs.IsVisible);
			AssertDisplayed(handler, first, "PAGE ONE");
			Assert.Equal(0, navigated);
			Assert.Equal(0, created());

			switch (change)
			{
				case "section": target.IsVisible = true; break;
				case "content": content.IsVisible = true; break;
				case "empty": target.Items.Add(content); break;
			}
			DrainDispatcher();
			Assert.Equal(3, tabs.Items.Count);
			Assert.Same(target, ((WTabItem)tabs.Items[1]).Tag);
			Select((WTabItem)tabs.Items[1]);
			DrainDispatcher();
			Assert.Equal(1, navigated);
			Assert.EndsWith("/two", shell.CurrentState.Location.OriginalString);
			AssertDisplayed(handler, second, "PAGE TWO");
			Assert.Equal(1, created());
			Record(useTemplate, $"restored-{change}", shell, handler, tabs, navigated);
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NativeTabSelection_WhenKeyboardFocused_PreservesNativeFocus(bool useTemplate)
	{
		RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
		{
			var originalTabs = tabs.Items.OfType<WTabItem>().ToArray();
			var window = System.Windows.Window.GetWindow(tabs);
			Assert.True(window.IsActive);
			Assert.Same(originalTabs[0], System.Windows.Input.Keyboard.Focus(originalTabs[0]));
			DrainDispatcher();
			Assert.True(originalTabs[0].IsKeyboardFocused);
			Record(useTemplate, "keyboard-before", shell, handler, tabs, 0, originalTabs[0]);
			var navigated = 0;
			shell.Navigated += (_, _) => navigated++;

			Assert.Same(originalTabs[1], System.Windows.Input.Keyboard.Focus(originalTabs[1]));
			DrainDispatcher();
			Record(useTemplate, "keyboard-selected-two", shell, handler, tabs, navigated, originalTabs[1]);

			Assert.Same(originalTabs[1], System.Windows.Input.Keyboard.FocusedElement);
			Assert.True(originalTabs[1].IsKeyboardFocused);
			Assert.Equal(originalTabs, tabs.Items.OfType<WTabItem>().ToArray());
			Assert.Same(originalTabs[1], tabs.SelectedItem);
			Assert.EndsWith("/two", shell.CurrentState.Location.OriginalString);
			Assert.Equal(1, navigated);
			AssertDisplayed(handler, second, "PAGE TWO");
			Assert.Equal(1, created());
		}, activateWindow: true);
	}

	[Fact]
	public void NativeTabSelection_AfterRebind_NavigatesOnlyReplacementShell()
	{
		RunTest(true, (shell, handler, tabs, first, second, created) =>
		{
			var replacementFirst = CreatePage("REPLACEMENT ONE");
			var replacementSecond = CreatePage("REPLACEMENT TWO");
			var replacement = new Shell
			{
				Items =
				{
					new TabBar
					{
						Items =
						{
							new ShellContent { Route = "replacement-one", Content = replacementFirst },
							new ShellContent { Route = "replacement-two", ContentTemplate = new DataTemplate(() => replacementSecond) },
						},
					},
				},
			};
			_ = new Window(replacement);
			handler.SetVirtualView(replacement);
			DrainDispatcher();
			var oldNavigated = 0;
			var newNavigated = 0;
			shell.Navigated += (_, _) => oldNavigated++;
			replacement.Navigated += (_, _) => newNavigated++;
			Select((WTabItem)tabs.Items[1]);
			DrainDispatcher();

			Assert.Equal(0, oldNavigated);
			Assert.Equal(1, newNavigated);
			Assert.Equal(0, created());
			Assert.EndsWith("/replacement-two", replacement.CurrentState.Location.OriginalString);
			AssertDisplayed(handler, replacementSecond, "REPLACEMENT TWO");
		});
	}

	static void Select(WTabItem tab)
	{
		var owner = Assert.IsType<WTabControl>(System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(tab));
		var peer = new TabItemAutomationPeer(tab, new TabControlAutomationPeer(owner));
		var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
		selection.Select();
	}

	static void RunTest(bool useTemplate,
		Action<Shell, ShellHandler, WTabControl, ContentPage, ContentPage, Func<int>> test,
		bool useFlyoutItem = false, bool activateWindow = false)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			var previousApplication = Application.Current;
			try
			{
				SynchronizationContext.SetSynchronizationContext(
					new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
				DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
				using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>().Build();
				var first = CreatePage("PAGE ONE");
				var second = CreatePage("PAGE TWO");
				var count = 0;
				ShellItem item = useFlyoutItem ? new FlyoutItem() : new TabBar();
				item.Items.Add(new ShellContent { Title = "One", Route = "one", ContentTemplate = new DataTemplate(() => first) });
				item.Items.Add(new ShellContent { Title = "Two", Route = "two", ContentTemplate = new DataTemplate(() => { count++; return second; }) });
				var shell = new Shell
				{
					Items = { item },
					FlyoutBehavior = useFlyoutItem ? FlyoutBehavior.Locked : FlyoutBehavior.Flyout,
				};
				if (useTemplate)
					shell.ItemTemplate = new DataTemplate(() => new Label { Text = "templated item", Padding = 8 });
				_ = new Window(shell);
				var handler = new ShellHandler();
				handler.SetMauiContext(new WPFMauiContext(app.Services));
				System.Windows.Window? nativeWindow = null;
				try
				{
					handler.SetVirtualView(shell);
					nativeWindow = new System.Windows.Window
					{
						Content = handler.PlatformView,
						Width = 800,
						Height = 600,
						Left = -10000,
						Top = -10000,
						ShowActivated = activateWindow,
						ShowInTaskbar = false,
					};
					nativeWindow.Show();
					DrainDispatcher();
					var tabs = Assert.Single(Descendants(handler.PlatformView).OfType<WTabControl>());
					test(shell, handler, tabs, first, second, () => count);
				}
				finally
				{
					nativeWindow?.Close();
					((IElementHandler)handler).DisconnectHandler();
				}
			}
			catch (Exception ex)
			{
				failure = ex;
			}
			finally
			{
				Application.Current = previousApplication;
			}
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Native Shell tab test timed out.");
		if (failure != null)
			ExceptionDispatchInfo.Capture(failure).Throw();
	}

	static ContentPage CreatePage(string text) => new()
	{
		BackgroundColor = Microsoft.Maui.Graphics.Colors.White,
		Content = new Label { Text = text, TextColor = Microsoft.Maui.Graphics.Colors.Black, FontSize = 32 },
	};

	static void DrainDispatcher() =>
		Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

	static void AssertDisplayed(ShellHandler handler, ContentPage page, string text)
	{
		var nativePage = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(page.Handler?.PlatformView);
		Assert.True(nativePage.IsVisible);
		Assert.True(nativePage.ActualWidth > 0 && nativePage.ActualHeight > 0);
		Assert.Contains(Descendants(handler.PlatformView),
			element => element is System.Windows.Controls.ContentControl host && ReferenceEquals(host.Content, nativePage));
		var label = Assert.Single(VisualDescendants(nativePage).OfType<System.Windows.Controls.TextBlock>(),
			label => label.Text == text);
		Assert.True(label.IsVisible);
		Assert.True(label.ActualWidth > 0 && label.ActualHeight > 0);
	}

	static void Record(bool useTemplate, string state, Shell shell, ShellHandler handler, WTabControl tabs, int navigated,
		WTabItem? expectedFocusedTab = null)
	{
		if (Environment.GetEnvironmentVariable("SHELL_TAB_RESULTS") is not { Length: > 0 } output)
			return;
		Directory.CreateDirectory(output);
		var name = $"{(useTemplate ? "d7t" : "d7")}-{state}";
		File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
		{
			tabs = tabs.Items.Count,
			tabStripVisible = tabs.IsVisible,
			selectedTab = (tabs.SelectedItem as WTabItem)?.Header,
			route = shell.CurrentState.Location.OriginalString,
			navigated,
			keyboardFocusedTab = (System.Windows.Input.Keyboard.FocusedElement as WTabItem)?.Header,
			selectedTabHasKeyboardFocus = (tabs.SelectedItem as WTabItem)?.IsKeyboardFocused,
			originalTabHasKeyboardFocus = expectedFocusedTab?.IsKeyboardFocused,
			originalTabRetainsKeyboardFocus = expectedFocusedTab is null ? (bool?)null
				: ReferenceEquals(expectedFocusedTab, System.Windows.Input.Keyboard.FocusedElement),
			visibleLabels = VisualDescendants(handler.PlatformView).OfType<System.Windows.Controls.TextBlock>()
				.Where(label => label.IsVisible).Select(label => label.Text).ToArray(),
		}, new JsonSerializerOptions { WriteIndented = true }));
		var bitmap = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32);
		bitmap.Render(handler.PlatformView);
		var encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(bitmap));
		using var stream = File.Create(Path.Combine(output, name + ".png"));
		encoder.Save(stream);
	}

	static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
	{
		foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
		{
			yield return child;
			foreach (var descendant in Descendants(child))
				yield return descendant;
		}
	}

	static IEnumerable<System.Windows.DependencyObject> VisualDescendants(System.Windows.DependencyObject root)
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			yield return child;
			foreach (var descendant in VisualDescendants(child))
				yield return descendant;
		}
	}
}
