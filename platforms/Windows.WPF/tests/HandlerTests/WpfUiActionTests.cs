using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.DevFlow.Agent.WPF;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using DataTemplate = Microsoft.Maui.Controls.DataTemplate;
using Label = Microsoft.Maui.Controls.Label;
using WGrid = System.Windows.Controls.Grid;
using WKeyboard = System.Windows.Input.Keyboard;
using WWindow = System.Windows.Window;

namespace HandlerTests;

[CollectionDefinition("WPF UI actions", DisableParallelization = true)]
public sealed class WpfUiActionCollection;

[Collection("WPF UI actions")]
public class WpfUiActionTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Focus_Entry_CompletesRequestAndSynchronizesNativeFocus(bool password)
	{
		Run(context =>
		{
			var entry = new Entry { IsPassword = password };
			var handler = new EntryHandler();
			handler.SetMauiContext(context);
			handler.SetVirtualView(entry);
			var other = new TextBox();
			var panel = new StackPanel();
			panel.Children.Add(handler.PlatformView);
			panel.Children.Add(other);
			handler.UpdateValue(nameof(IEntry.IsPassword));
			var target = password ? (UIElement)panel.Children.OfType<PasswordBox>().Single() : handler.PlatformView;
			var window = CreateWindow(panel, activate: true);
			try
			{
				window.Show();
				Assert.True(window.Activate(), "Native keyboard-focus tests require foreground access.");
				Layout(window);
				Assert.True(other.Focus());
				Assert.False(entry.IsFocused);
				var focused = 0;
				var unfocused = 0;
				entry.Focused += (_, _) => focused++;
				entry.Unfocused += (_, _) => unfocused++;

				Assert.True(entry.Focus());
				Assert.Same(target, WKeyboard.FocusedElement);
				Assert.True(target.IsKeyboardFocused);
				Assert.True(entry.IsFocused);
				Assert.Equal(1, focused);

				Assert.True(other.Focus());
				Assert.False(entry.IsFocused);
				Assert.Equal(1, unfocused);

				((IElementHandler)handler).DisconnectHandler();
				Assert.True(target.Focus());
				Assert.False(entry.IsFocused);
				Assert.Equal(1, focused);
			}
			finally
			{
				window.Content = null;
				window.Close();
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Focus_EntryEnabledState_FollowsPasswordControlAndTransitions(bool initiallyEnabled)
	{
		Run(context =>
		{
			var entry = new Entry { IsPassword = true, IsEnabled = initiallyEnabled };
			var handler = new EntryHandler();
			handler.SetMauiContext(context);
			handler.SetVirtualView(entry);
			var other = new TextBox();
			var panel = new StackPanel();
			panel.Children.Add(handler.PlatformView);
			panel.Children.Add(other);
			handler.UpdateValue(nameof(IEntry.IsPassword));
			var passwordBox = panel.Children.OfType<PasswordBox>().Single();
			Assert.Equal(initiallyEnabled, handler.PlatformView.IsEnabled);
			Assert.Equal(initiallyEnabled, passwordBox.IsEnabled);
			var window = CreateWindow(panel, activate: true);
			try
			{
				window.Show();
				Assert.True(window.Activate(), "Native keyboard-focus tests require foreground access.");
				Layout(window);
				Assert.True(other.Focus());
				if (initiallyEnabled)
				{
					Assert.True(entry.Focus());
					Assert.Same(passwordBox, WKeyboard.FocusedElement);
					Assert.True(entry.IsFocused);
					Assert.True(other.Focus());
					entry.IsEnabled = false;
				}

				AssertDisabledFocus(passwordBox);
				entry.IsPassword = false;
				Layout(window);
				AssertDisabledFocus(handler.PlatformView);
				entry.IsPassword = true;
				Layout(window);
				Assert.Same(passwordBox, panel.Children.OfType<PasswordBox>().Single());
				AssertDisabledFocus(passwordBox);

				entry.IsEnabled = true;
				Layout(window);
				Assert.True(handler.PlatformView.IsEnabled);
				Assert.True(passwordBox.IsEnabled);
				Assert.True(entry.Focus());
				Assert.Same(passwordBox, WKeyboard.FocusedElement);
				Assert.True(passwordBox.IsKeyboardFocused);
				Assert.True(entry.IsFocused);
				Assert.True(other.Focus());
				Assert.False(entry.IsFocused);

				entry.IsPassword = false;
				Layout(window);
				Assert.True(entry.Focus());
				Assert.Same(handler.PlatformView, WKeyboard.FocusedElement);
				Assert.True(handler.PlatformView.IsKeyboardFocused);
				Assert.True(entry.IsFocused);
				Assert.True(other.Focus());
				entry.IsEnabled = false;
				AssertDisabledFocus(handler.PlatformView);
				entry.IsPassword = true;
				Layout(window);
				AssertDisabledFocus(passwordBox);

				void AssertDisabledFocus(UIElement target)
				{
					Assert.False(handler.PlatformView.IsEnabled);
					Assert.False(passwordBox.IsEnabled);
					var request = new FocusRequest();
					handler.Invoke(nameof(IView.Focus), request);
					Assert.False(request.Result);
					Assert.False(entry.Focus());
					Assert.Same(other, WKeyboard.FocusedElement);
					Assert.False(target.IsFocused);
					Assert.False(target.IsKeyboardFocused);
					Assert.False(entry.IsFocused);
				}
			}
			finally
			{
				window.Content = null;
				window.Close();
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Focus_EntryWithoutAvailableNativeFocus_CompletesWithFalse(bool disabled)
	{
		Run(context =>
		{
			var entry = new Entry { IsEnabled = !disabled };
			var handler = new EntryHandler();
			handler.SetMauiContext(context);
			handler.SetVirtualView(entry);
			try
			{
				// A detached, focusable TextBox can still acquire logical focus.
				if (!disabled)
					handler.PlatformView.Focusable = false;
				Assert.Equal(!disabled, handler.PlatformView.IsEnabled);
				Assert.Equal(disabled, handler.PlatformView.Focusable);

				var request = new FocusRequest();
				handler.Invoke(nameof(IView.Focus), request);
				Assert.False(request.Result);
				Assert.False(entry.IsFocused);
				Assert.False(handler.PlatformView.IsFocused);
				Assert.False(handler.PlatformView.IsKeyboardFocused);
				Assert.False(handler.PlatformView.IsKeyboardFocusWithin);
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Fact]
	public void Scroll_CollectionView_UsesInternalViewerBeforeAncestorAndPreservesVirtualization()
	{
		Run(context =>
		{
			var collection = new CollectionView
			{
				ItemsSource = Enumerable.Range(0, 200)
					.Select(index => new { Text = $"Item {index}", Height = 24d + index % 3 * 12d }).ToArray(),
				ItemTemplate = new DataTemplate(() =>
				{
					var label = new Label();
					label.SetBinding(Label.TextProperty, "Text");
					label.SetBinding(Label.HeightRequestProperty, "Height");
					return label;
				}),
			};
			var handler = new CollectionViewHandler();
			handler.SetMauiContext(context);
			handler.SetVirtualView(collection);
			var root = Assert.IsType<WGrid>(handler.PlatformView);
			root.Height = 200;
			root.Width = 300;
			var list = Assert.IsType<MauiCollectionListBox>(root.Children[0]);
			var panel = new StackPanel();
			panel.Children.Add(root);
			panel.Children.Add(new System.Windows.Controls.Border { Height = 1000 });
			var outer = new ScrollViewer { Content = panel };
			var window = CreateWindow(outer);
			using var service = new ScrollAgent();
			try
			{
				window.Show();
				Layout(window);
				var inner = Assert.Single(Descendants(root).OfType<ScrollViewer>());
				Assert.True(ScrollViewer.GetCanContentScroll(list));
				Assert.True(inner.CanContentScroll);
				Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list));
				Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
				Assert.Single(Descendants(list).OfType<VirtualizingStackPanel>());
				Assert.True(Descendants(list).OfType<ListBoxItem>()
					.Select(row => row.ActualHeight).Distinct().Count() > 1);
				Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1));
				Assert.True(inner.ScrollableHeight > 200);
				Assert.True(outer.ScrollableHeight > 0);
				inner.ScrollToVerticalOffset(200);
				Layout(window);
				// Variable-height virtualization revises estimated offsets; rendered rows measure the scroll distance.
				var originalOffset = inner.VerticalOffset;
				Assert.True(originalOffset > 100d);
				var anchor = Descendants(list).OfType<ListBoxItem>()
					.Where(row => row.TranslatePoint(new System.Windows.Point(), inner).Y >= 0)
					.OrderBy(row => row.TranslatePoint(new System.Windows.Point(), inner).Y).First();
				var anchorIndex = list.ItemContainerGenerator.IndexFromContainer(anchor);
				var originalTop = RowTop();

				Assert.True(CompleteOnDispatcher(service.ScrollAsync(collection, 0, 100)));
				Layout(window);
				var scrolledOffset = inner.VerticalOffset;
				Assert.True(scrolledOffset < originalOffset);
				Assert.Equal(100d, RowTop() - originalTop);
				Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1));
				Assert.Equal(0d, outer.VerticalOffset);

				Assert.True(CompleteOnDispatcher(service.ScrollAsync(collection, 0, -100)));
				Layout(window);
				Assert.True(inner.VerticalOffset > scrolledOffset);
				Assert.Equal(originalTop, RowTop());
				Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1));
				Assert.Equal(0d, outer.VerticalOffset);

				double RowTop() => Assert.IsAssignableFrom<ListBoxItem>(
					list.ItemContainerGenerator.ContainerFromIndex(anchorIndex))
					.TranslatePoint(new System.Windows.Point(), inner).Y;
			}
			finally
			{
				window.Content = null;
				window.Close();
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Scroll_ViewerOrNativeChild_ChangesBothOffsets(bool targetChild)
	{
		Run(context =>
		{
			var label = new Label { Text = "Scrollable content", WidthRequest = 1000, HeightRequest = 1000 };
			var scrollView = new Microsoft.Maui.Controls.ScrollView
			{
				Content = label,
				Orientation = ScrollOrientation.Both,
			};
			var handler = new ScrollViewHandler();
			handler.SetMauiContext(context);
			handler.SetVirtualView(scrollView);
			var window = CreateWindow(handler.PlatformView);
			using var service = new ScrollAgent();
			try
			{
				window.Show();
				Layout(window);
				Assert.True(handler.PlatformView.ScrollableWidth > 100);
				Assert.True(handler.PlatformView.ScrollableHeight > 100);
				Assert.True(CompleteOnDispatcher(service.ScrollAsync(targetChild ? label : scrollView, 60, -80)));
				Layout(window);
				Assert.Equal(60d, handler.PlatformView.HorizontalOffset);
				Assert.Equal(80d, handler.PlatformView.VerticalOffset);
			}
			finally
			{
				window.Content = null;
				window.Close();
				label.Handler?.DisconnectHandler();
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Fact]
	public void Scroll_NonScrollableButtonBesideCollectionView_DoesNotScrollSibling()
	{
		Run(context =>
		{
			var button = new Microsoft.Maui.Controls.Button { Text = "Not scrollable" };
			var collection = new CollectionView
			{
				HeightRequest = 200,
				ItemsSource = Enumerable.Range(0, 200).Select(index => $"Item {index}").ToArray(),
				ItemTemplate = new DataTemplate(() =>
				{
					var label = new Label { HeightRequest = 24 };
					label.SetBinding(Label.TextProperty, ".");
					return label;
				}),
			};
			var grid = new Microsoft.Maui.Controls.Grid
			{
				ColumnDefinitions =
				{
					new Microsoft.Maui.Controls.ColumnDefinition { Width = new Microsoft.Maui.GridLength(80) },
					new Microsoft.Maui.Controls.ColumnDefinition { Width = new Microsoft.Maui.GridLength(300) },
				},
			};
			grid.Add(button, 0, 0);
			grid.Add(collection, 1, 0);
			var handler = new LayoutHandler();
			handler.SetMauiContext(context);
			handler.SetVirtualView(grid);
			var collectionRoot = Assert.IsType<WGrid>(collection.Handler!.PlatformView);
			var list = Assert.IsType<MauiCollectionListBox>(collectionRoot.Children[0]);
			var window = CreateWindow(handler.PlatformView);
			using var service = new ScrollAgent();
			try
			{
				window.Show();
				Layout(window);
				Assert.Same(grid, button.Parent);
				Assert.Same(grid, collection.Parent);
				Assert.True(ScrollViewer.GetCanContentScroll(list));
				var sibling = Assert.Single(Descendants(collectionRoot).OfType<ScrollViewer>());
				Assert.True(sibling.ScrollableHeight > 200);
				sibling.ScrollToVerticalOffset(200);
				Layout(window);
				Assert.Equal(200d, sibling.VerticalOffset);
				var originalX = sibling.HorizontalOffset;
				var originalY = sibling.VerticalOffset;

				Assert.False(CompleteOnDispatcher(service.ScrollAsync(button, 60, 100)));
				Layout(window);
				Assert.Equal(originalX, sibling.HorizontalOffset);
				Assert.Equal(originalY, sibling.VerticalOffset);
			}
			finally
			{
				window.Content = null;
				window.Close();
				button.Handler?.DisconnectHandler();
				collection.Handler?.DisconnectHandler();
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Fact]
	public void Scroll_ElementWithoutViewer_ReturnsFalse()
	{
		Run(context =>
		{
			var label = new Label { Text = "Not scrollable" };
			var handler = new LabelHandler();
			handler.SetMauiContext(context);
			handler.SetVirtualView(label);
			using var service = new ScrollAgent();
			try
			{
				Assert.False(CompleteOnDispatcher(service.ScrollAsync(label, 0, 100)));
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	static void Run(Action<IMauiContext> test) => WpfApplicationHost.Run(_ =>
	{
		DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
		using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Microsoft.Maui.Controls.Application>().Build();
		test(new WPFMauiContext(app.Services));
	});

	static WWindow CreateWindow(object content, bool activate = false) => new()
	{
		Content = content,
		Width = 400,
		Height = 300,
		ShowActivated = activate,
		ShowInTaskbar = false,
	};

	static void Layout(WWindow window)
	{
		window.UpdateLayout();
		window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
		window.UpdateLayout();
	}

	static bool CompleteOnDispatcher(Task<bool> task)
	{
		var bounded = task.WaitAsync(TimeSpan.FromSeconds(10));
		var frame = new DispatcherFrame();
		var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
		_ = bounded.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
			TaskScheduler.Default);
		System.Windows.Threading.Dispatcher.PushFrame(frame);
		return bounded.GetAwaiter().GetResult();
	}

	static IEnumerable<DependencyObject> Descendants(DependencyObject root)
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			yield return child;
			foreach (var descendant in Descendants(child))
				yield return descendant;
		}
	}

	sealed class ScrollAgent : WpfAgentService
	{
		public Task<bool> ScrollAsync(VisualElement element, double x, double y)
			=> TryNativeScroll(element, x, y);
	}
}
