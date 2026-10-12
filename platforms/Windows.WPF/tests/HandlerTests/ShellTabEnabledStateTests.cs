using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers.WPF;
using Xunit.Abstractions;
using WTabControl = System.Windows.Controls.TabControl;
using WTabItem = System.Windows.Controls.TabItem;

namespace HandlerTests;

public partial class ShellTabNavigationTests
{
	readonly ITestOutputHelper _output;

	public ShellTabNavigationTests(ITestOutputHelper output) => _output = output;

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void NativeTabEnabled_InitialState_ProjectsToNativePeerAndSelection(bool useTemplate, bool enabled)
	{
		RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
		{
			var target = shell.CurrentItem.Items[1];
			var tab = Assert.IsType<WTabItem>(tabs.Items[1]);
			var peer = EnabledPeer(tab, tabs);
			var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
			var navigating = 0;
			var navigated = 0;
			shell.Navigating += (_, _) => navigating++;
			shell.Navigated += (_, _) => navigated++;
			var name = $"initial-{enabled}";

			Snapshot("before-select");
			AssertEnabledFixtureSetup(shell, handler, tabs, first);
			Assert.Equal(enabled, target.IsEnabled);
			var selectionFailure = Xunit.Record.Exception(selection.Select);
			DrainDispatcher();
			Snapshot("after-select", selectionFailure);

			AssertOffscreenSelectionDidNotActivate(tabs);
			AssertNativeEnabled(tab, peer, enabled);
			if (enabled)
			{
				Assert.Null(selectionFailure);
				Assert.Same(target, shell.CurrentItem.CurrentItem);
				Assert.Same(tab, tabs.SelectedItem);
				Assert.EndsWith("/two", shell.CurrentState.Location.OriginalString);
				Assert.Equal(1, navigating);
				Assert.Equal(1, navigated);
				AssertDisplayed(handler, second, "PAGE TWO");
				Assert.Equal(1, created());
			}
			else
			{
				Assert.IsType<ElementNotEnabledException>(selectionFailure);
				Assert.Same(shell.CurrentItem.Items[0], shell.CurrentItem.CurrentItem);
				Assert.Same(tabs.Items[0], tabs.SelectedItem);
				Assert.EndsWith("/one", shell.CurrentState.Location.OriginalString);
				Assert.Equal(0, navigating);
				Assert.Equal(0, navigated);
				AssertDisplayed(handler, first, "PAGE ONE");
				Assert.Equal(0, created());
			}

			void Snapshot(string stage, Exception? failure = null) =>
				RecordEnabled(useTemplate, name, stage, shell, handler, tabs, navigating, navigated, created(), failure);
		}, beforeAttach: shell => shell.CurrentItem.Items[1].IsEnabled = enabled);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NativeTabEnabled_RuntimeChanges_PreserveIdentityAndNavigation(bool useTemplate)
	{
		RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
		{
			var originalTabs = tabs.Items.Cast<WTabItem>().ToArray();
			var target = shell.CurrentItem.Items[1];
			var tab = originalTabs[1];
			var peer = EnabledPeer(tab, tabs);
			var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
			var navigating = 0;
			var navigated = 0;
			shell.Navigating += (_, _) => navigating++;
			shell.Navigated += (_, _) => navigated++;

			Snapshot("enabled-control");
			AssertEnabledFixtureSetup(shell, handler, tabs, first);
			AssertNativeEnabled(tab, peer, true);
			target.IsEnabled = false;
			DrainDispatcher();
			Snapshot("disabled-before-select");
			AssertOffscreenSelectionDidNotActivate(tabs);
			var selectionFailure = Xunit.Record.Exception(selection.Select);
			DrainDispatcher();
			Snapshot("disabled-after-select", selectionFailure);

			AssertOffscreenSelectionDidNotActivate(tabs);
			AssertNativeEnabled(tab, peer, false);
			Assert.IsType<ElementNotEnabledException>(selectionFailure);
			Assert.Equal(originalTabs, tabs.Items.Cast<WTabItem>().ToArray());
			Assert.Same(originalTabs[0], tabs.SelectedItem);
			Assert.EndsWith("/one", shell.CurrentState.Location.OriginalString);
			Assert.Equal(0, navigating);
			Assert.Equal(0, navigated);
			AssertDisplayed(handler, first, "PAGE ONE");
			Assert.Equal(0, created());

			target.IsEnabled = true;
			DrainDispatcher();
			Snapshot("reenabled-before-select");
			AssertNativeEnabled(tab, peer, true);
			Assert.Equal(originalTabs, tabs.Items.Cast<WTabItem>().ToArray());
			AssertOffscreenSelectionDidNotActivate(tabs);
			selection.Select();
			DrainDispatcher();
			Snapshot("reenabled-after-select");
			AssertOffscreenSelectionDidNotActivate(tabs);
			Assert.Same(target, shell.CurrentItem.CurrentItem);
			Assert.Same(tab, tabs.SelectedItem);
			Assert.EndsWith("/two", shell.CurrentState.Location.OriginalString);
			Assert.Equal(1, navigating);
			Assert.Equal(1, navigated);
			AssertDisplayed(handler, second, "PAGE TWO");
			Assert.Equal(1, created());

			target.IsEnabled = false;
			DrainDispatcher();
			Snapshot("selected-section-disabled");
			AssertNativeEnabled(tab, peer, false);
			Assert.Equal(originalTabs, tabs.Items.Cast<WTabItem>().ToArray());
			Assert.Same(target, shell.CurrentItem.CurrentItem);
			Assert.Same(tab, tabs.SelectedItem);
			Assert.Equal(1, navigating);
			Assert.Equal(1, navigated);
			Assert.EndsWith("/two", shell.CurrentState.Location.OriginalString);
			AssertDisplayed(handler, second, "PAGE TWO");
			AssertOffscreenSelectionDidNotActivate(tabs);

			void Snapshot(string stage, Exception? failure = null) =>
				RecordEnabled(useTemplate, "runtime", stage, shell, handler, tabs, navigating, navigated, created(), failure);
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NativeTabEnabled_RuntimeChanges_PreserveCurrentItemMapperCustomization(bool useTemplate)
	{
		var originalMapper = ShellHandler.Mapper;
		var mapper = new PropertyMapper<Shell, ShellHandler>(originalMapper);
		var calls = 0;
		mapper.AppendToMapping(nameof(Shell.CurrentItem), (handler, _) =>
		{
			foreach (var tab in EnabledTabs(handler).Items.Cast<WTabItem>())
			{
				tab.Header = new System.Windows.Controls.TextBox { Text = $"Custom {((ShellSection)tab.Tag).Title}" };
				System.Windows.Automation.AutomationProperties.SetHelpText(tab, "Enabled-state mapper sentinel");
			}
			calls++;
		});
		try
		{
			ShellHandler.Mapper = mapper;
			RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
			{
				var navigating = 0;
				var navigated = 0;
				shell.Navigating += (_, _) => navigating++;
				shell.Navigated += (_, _) => navigated++;
				handler.UpdateValue(nameof(Shell.CurrentItem));
				DrainDispatcher();
				var originalTabs = tabs.Items.Cast<WTabItem>().ToArray();
				var headers = originalTabs.Select(tab => tab.Header).ToArray();
				var originalCalls = calls;
				var target = shell.CurrentItem.Items[1];
				var peer = EnabledPeer(originalTabs[1], tabs);

				Snapshot("enabled-control");
				AssertEnabledFixtureSetup(shell, handler, tabs, first);
				AssertNativeEnabled(originalTabs[1], peer, true);
				AssertCustomization();
				target.IsEnabled = false;
				DrainDispatcher();
				Snapshot("disabled");
				AssertNativeEnabled(originalTabs[1], peer, false);
				AssertCustomization();
				target.IsEnabled = true;
				DrainDispatcher();
				Snapshot("reenabled");
				AssertNativeEnabled(originalTabs[1], peer, true);
				AssertCustomization();

				void Snapshot(string stage) =>
					RecordEnabled(useTemplate, "mapper", stage, shell, handler, tabs, navigating, navigated, created(),
						extra: new { mapperCalls = calls, initialMapperCalls = originalCalls });

				void AssertCustomization()
				{
					Assert.Equal(originalTabs, tabs.Items.Cast<WTabItem>().ToArray());
					for (var i = 0; i < originalTabs.Length; i++)
					{
						Assert.Same(headers[i], originalTabs[i].Header);
						Assert.Equal($"Custom {((ShellSection)originalTabs[i].Tag).Title}",
							Assert.IsType<System.Windows.Controls.TextBox>(originalTabs[i].Header).Text);
						Assert.Equal("Enabled-state mapper sentinel", System.Windows.Automation.AutomationProperties.GetHelpText(originalTabs[i]));
					}
					Assert.Equal(originalCalls, calls);
					Assert.Equal(0, navigating);
					Assert.Equal(0, navigated);
					Assert.Equal(0, created());
					Assert.Same(originalTabs[0], tabs.SelectedItem);
					Assert.EndsWith("/one", shell.CurrentState.Location.OriginalString);
					AssertDisplayed(handler, first, "PAGE ONE");
					AssertOffscreenSelectionDidNotActivate(tabs);
				}
			});
		}
		finally
		{
			ShellHandler.Mapper = originalMapper;
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NativeTabEnabled_Subscriptions_FollowCollectionAndHandlerLifecycle(bool useTemplate)
	{
		RunTest(useTemplate, (shell, handler, tabs, first, second, created) =>
		{
			var window = System.Windows.Window.GetWindow(tabs);
			var original = shell.CurrentItem.Items.ToArray();
			var target = original[1];
			var navigating = 0;
			var navigated = 0;
			shell.Navigating += (_, _) => navigating++;
			shell.Navigated += (_, _) => navigated++;
			Snapshot("enabled-control", shell);
			AssertEnabledFixtureSetup(shell, handler, tabs, first);
			target.IsEnabled = false;
			DrainDispatcher();
			Snapshot("disabled", shell);
			AssertNativeEnabled((WTabItem)tabs.Items[1], EnabledPeer((WTabItem)tabs.Items[1], tabs), false);
			AssertSubscriptions(original, 1);

			handler.SetVirtualView(shell);
			handler.SetVirtualView(shell);
			DrainDispatcher();
			Snapshot("repeat-attach", shell);
			AssertSubscriptions(original, 1);

			var removed = EnabledSection("removed");
			shell.CurrentItem.Items.Add(removed);
			DrainDispatcher();
			var retired = Assert.Single(tabs.Items.Cast<WTabItem>(), tab => ReferenceEquals(tab.Tag, removed));
			AssertSubscriptions(new[] { removed }, 1);
			shell.CurrentItem.Items.Remove(removed);
			DrainDispatcher();
			var currentTabs = tabs.Items.Cast<WTabItem>().ToArray();
			var oldNavigation = (navigating, navigated);
			removed.IsEnabled = false;
			DrainDispatcher();
			Snapshot("removed-section-mutated", shell, new { retiredNativeEnabled = retired.IsEnabled, callbacks = EnabledCallbackCount(removed, handler) });
			AssertSubscriptions(new[] { removed }, 0);
			Assert.True(retired.IsEnabled);
			Assert.Equal(currentTabs, tabs.Items.Cast<WTabItem>().ToArray());
			Assert.Equal(oldNavigation, (navigating, navigated));

			var inactive = new TabBar { Items = { EnabledSection("inactive-one"), EnabledSection("inactive-two") } };
			shell.Items.Add(inactive);
			DrainDispatcher();
			currentTabs = tabs.Items.Cast<WTabItem>().ToArray();
			oldNavigation = (navigating, navigated);
			inactive.Items[1].IsEnabled = false;
			DrainDispatcher();
			Snapshot("inactive-section-mutated", shell);
			AssertSubscriptions(inactive.Items, 1);
			Assert.Equal(currentTabs, tabs.Items.Cast<WTabItem>().ToArray());
			Assert.Equal(oldNavigation, (navigating, navigated));
			AssertNativeEnabled(currentTabs[0], EnabledPeer(currentTabs[0], tabs), true);

			var originalItem = shell.CurrentItem;
			shell.CurrentItem = inactive;
			DrainDispatcher();
			Snapshot("inactive-root-selected", shell);
			AssertNativeEnabled((WTabItem)tabs.Items[1], EnabledPeer((WTabItem)tabs.Items[1], tabs), false);
			shell.CurrentItem = originalItem;
			shell.Items.Remove(inactive);
			DrainDispatcher();
			AssertSubscriptions(inactive.Items, 0);

			originalItem.Items.Clear();
			originalItem.Items.Add(EnabledSection("reset-one"));
			originalItem.Items.Add(EnabledSection("reset-two"));
			DrainDispatcher();
			oldNavigation = (navigating, navigated);
			original[0].IsEnabled = false;
			target.IsEnabled = true;
			DrainDispatcher();
			Snapshot("reset-retired-sections-mutated", shell);
			AssertSubscriptions(original, 0);
			AssertSubscriptions(originalItem.Items, 1);
			Assert.Equal(oldNavigation, (navigating, navigated));
			foreach (var tab in tabs.Items.Cast<WTabItem>())
				AssertNativeEnabled(tab, EnabledPeer(tab, tabs), true);

			var replacement = new Shell
			{
				Items = { new TabBar { Items = { EnabledSection("replacement-one"), EnabledSection("replacement-two") } } },
			};
			if (useTemplate)
				replacement.ItemTemplate = new DataTemplate(() => new Label { Text = "replacement template" });
			_ = new Window(replacement);
			replacement.Navigating += (_, _) => navigating++;
			replacement.Navigated += (_, _) => navigated++;
			handler.SetVirtualView(replacement);
			DrainDispatcher();
			oldNavigation = (navigating, navigated);
			originalItem.Items[1].IsEnabled = false;
			DrainDispatcher();
			Snapshot("rebound-old-shell-mutated", replacement);
			AssertSubscriptions(originalItem.Items, 0);
			AssertSubscriptions(replacement.CurrentItem.Items, 1);
			Assert.Equal(oldNavigation, (navigating, navigated));
			var replacementTabs = EnabledTabs(handler);
			var replacementTab = (WTabItem)replacementTabs.Items[1];
			AssertNativeEnabled(replacementTab, EnabledPeer(replacementTab, replacementTabs), true);

			((IElementHandler)handler).DisconnectHandler();
			replacement.CurrentItem.Items[1].IsEnabled = false;
			DrainDispatcher();
			RecordEnabled(useTemplate, "lifecycle", "disconnected-source-mutated", replacement, handler, replacementTabs,
				navigating, navigated, created(), extra: new { detachedNativeEnabled = replacementTab.IsEnabled });
			AssertSubscriptions(replacement.CurrentItem.Items, 0);
			Assert.True(replacementTab.IsEnabled);
			Assert.Equal(oldNavigation, (navigating, navigated));

			handler.SetVirtualView(replacement);
			window.Content = handler.PlatformView;
			handler.SetVirtualView(replacement);
			DrainDispatcher();
			Snapshot("reconnected", replacement);
			AssertSubscriptions(replacement.CurrentItem.Items, 1);
			replacementTabs = EnabledTabs(handler);
			replacementTab = (WTabItem)replacementTabs.Items[1];
			AssertNativeEnabled(replacementTab, EnabledPeer(replacementTab, replacementTabs), false);
			replacement.CurrentItem.Items[1].IsEnabled = true;
			DrainDispatcher();
			Snapshot("reconnected-source-enabled", replacement);
			AssertNativeEnabled(replacementTab, EnabledPeer(replacementTab, replacementTabs), true);
			AssertOffscreenSelectionDidNotActivate(replacementTabs);
			((IElementHandler)handler).DisconnectHandler();
			AssertSubscriptions(replacement.CurrentItem.Items, 0);

			void Snapshot(string stage, Shell observed, object? extra = null) =>
				RecordEnabled(useTemplate, "lifecycle", stage, observed, handler, EnabledTabs(handler), navigating, navigated, created(), extra: extra);

			void AssertSubscriptions(IEnumerable<ShellSection> sections, int expected)
			{
				foreach (var section in sections)
					Assert.Equal(expected, EnabledCallbackCount(section, handler));
			}
		});
	}

	static ShellSection EnabledSection(string route) => new()
	{
		Title = route,
		Items = { new ShellContent { Route = route, Content = CreatePage(route) } },
	};

	static WTabControl EnabledTabs(ShellHandler handler) =>
		Assert.Single(Descendants(handler.PlatformView).OfType<WTabControl>());

	static TabItemAutomationPeer EnabledPeer(WTabItem tab, WTabControl tabs)
	{
		Assert.Same(tabs, System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(tab));
		return new TabItemAutomationPeer(tab, new TabControlAutomationPeer(tabs));
	}

	static void AssertNativeEnabled(WTabItem tab, TabItemAutomationPeer peer, bool enabled)
	{
		if (enabled)
		{
			Assert.True(tab.IsEnabled, "Enabled ShellSection must project native TabItem.IsEnabled=true.");
			Assert.True(peer.IsEnabled(), "Enabled ShellSection must report UIA IsEnabled=true.");
		}
		else
		{
			Assert.False(tab.IsEnabled, "Disabled ShellSection must project native TabItem.IsEnabled=false.");
			Assert.False(peer.IsEnabled(), "Disabled ShellSection must report UIA IsEnabled=false.");
		}
	}

	static void AssertEnabledFixtureSetup(Shell shell, ShellHandler handler, WTabControl tabs, ContentPage first)
	{
		Assert.Equal(2, ((IShellItemController)shell.CurrentItem).GetItems().Count);
		Assert.Equal(2, tabs.Items.Count);
		Assert.True(tabs.IsVisible);
		Assert.True(tabs.ActualWidth > 0 && tabs.ActualHeight > 0);
		Assert.Same(shell.CurrentItem.Items[0], shell.CurrentItem.CurrentItem);
		Assert.Same(tabs.Items[0], tabs.SelectedItem);
		Assert.EndsWith("/one", shell.CurrentState.Location.OriginalString);
		AssertDisplayed(handler, first, "PAGE ONE");
		AssertOffscreenSelectionDidNotActivate(tabs);
	}

	static void AssertOffscreenSelectionDidNotActivate(WTabControl tabs)
	{
		Assert.False(System.Windows.Window.GetWindow(tabs).IsActive, "Enabled-state fixtures must not activate their owned window.");
		Assert.False(tabs.IsKeyboardFocusWithin, "UIA selection must start and remain outside keyboard focus.");
	}

	static int EnabledCallbackCount(ShellSection section, ShellHandler handler)
	{
		var field = typeof(BindableObject).GetField(nameof(BindableObject.PropertyChanged), BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		var callbacks = (field.GetValue(section) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
		return callbacks.Count(callback => ReferenceEquals(callback.Target, handler) &&
			callback.Method.DeclaringType == typeof(ShellHandler) && callback.Method.Name == "OnSectionIsEnabledChanged");
	}

	void RecordEnabled(bool useTemplate, string test, string stage, Shell shell, ShellHandler handler, WTabControl tabs,
		int navigating, int navigated, int created, Exception? failure = null, object? extra = null)
	{
		var name = $"enabled-{test}-{stage}";
		var root = System.Windows.Window.GetWindow(tabs)?.Content as Visual ?? tabs;
		var snapshot = new
		{
			test,
			stage,
			useTemplate,
			windowActive = System.Windows.Window.GetWindow(tabs)?.IsActive,
			keyboardFocusWithin = tabs.IsKeyboardFocusWithin,
			controllerCount = ((IShellItemController)shell.CurrentItem).GetItems().Count,
			nativeCount = tabs.Items.Count,
			stripVisible = tabs.IsVisible,
			nativeWidth = tabs.ActualWidth,
			nativeHeight = tabs.ActualHeight,
			modelSection = shell.CurrentItem.CurrentItem?.CurrentItem?.Route,
			route = shell.CurrentState.Location.OriginalString,
			page = ((shell.CurrentPage as ContentPage)?.Content as Label)?.Text,
			visibleNativeLabels = VisualDescendants(root).OfType<System.Windows.Controls.TextBlock>()
				.Where(label => label.IsVisible).Select(label => label.Text).ToArray(),
			navigating,
			navigated,
			lazyPagesCreated = created,
			selectionException = failure?.GetType().FullName,
			runtimeModules = new
			{
				handlerLocation = typeof(ShellHandler).Assembly.Location,
				handlerMvid = typeof(ShellHandler).Module.ModuleVersionId,
				testLocation = typeof(ShellTabNavigationTests).Assembly.Location,
				testMvid = typeof(ShellTabNavigationTests).Module.ModuleVersionId,
			},
			tabs = tabs.Items.Cast<WTabItem>().Select(tab => new
			{
				section = ((ShellSection)tab.Tag).CurrentItem?.Route,
				modelEnabled = ((ShellSection)tab.Tag).IsEnabled,
				nativeEnabled = tab.IsEnabled,
				uiaEnabled = EnabledPeer(tab, tabs).IsEnabled(),
				selected = tab.IsSelected,
				enabledCallbacks = EnabledCallbackCount((ShellSection)tab.Tag, handler),
				header = tab.Header is System.Windows.Controls.TextBox textBox ? textBox.Text : tab.Header?.ToString(),
				helpText = System.Windows.Automation.AutomationProperties.GetHelpText(tab),
			}).ToArray(),
			extra,
		};
		var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
		_output.WriteLine(json);
		if (Environment.GetEnvironmentVariable("SHELL_TAB_RESULTS") is { Length: > 0 } output)
		{
			Directory.CreateDirectory(output);
			File.WriteAllText(Path.Combine(output, $"{(useTemplate ? "d7t" : "d7")}-{name}.details.json"), json);
			var bitmap = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32);
			bitmap.Render(root);
			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(bitmap));
			using var stream = File.Create(Path.Combine(output, $"{(useTemplate ? "d7t" : "d7")}-{name}.png"));
			encoder.Save(stream);
		}
	}
}
