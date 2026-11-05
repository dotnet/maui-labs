using System.IO;
using System.Windows.Threading;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using Dispatcher = System.Windows.Threading.Dispatcher;

namespace HandlerTests;

[CollectionDefinition("Shell selection runtime", DisableParallelization = true)]
public sealed class ShellSelectionRuntimeCollection;

[Collection("Shell selection runtime")]
public class ShellSectionSwitchingTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SwitchSection_ShowsLazyPageAndCompletesNavigation(bool useRoute)
	{
		RunTest((shell, handler, first, second, created) =>
		{
			var navigated = 0;
			shell.Navigated += (_, _) => navigated++;
			Assert.Equal(0, created());
			AssertDisplayed(handler, first);

			if (useRoute)
				CompleteNavigation(shell.GoToAsync("//two"));
			else
				shell.CurrentItem.CurrentItem = shell.CurrentItem.Items[1];
			DrainDispatcher();

			Assert.Equal(1, created());
			Assert.Same(second, shell.CurrentPage);
			AssertDisplayed(handler, second);
			Assert.True(navigated > 0);

			shell.CurrentItem.CurrentItem = shell.CurrentItem.Items[0];
			DrainDispatcher();
			AssertDisplayed(handler, first);
			shell.CurrentItem.CurrentItem = shell.CurrentItem.Items[1];
			DrainDispatcher();
			AssertDisplayed(handler, second);
			Assert.Equal(1, created());
		});
	}

	[Fact]
	public void HideOrRemoveSelectedSection_ShowsRemainingSection()
	{
		RunTest((shell, handler, first, second, created) =>
		{
			var item = shell.CurrentItem;
			var selected = item.Items[1];
			item.CurrentItem = selected;
			DrainDispatcher();
			AssertDisplayed(handler, second);
			selected.IsVisible = false;
			DrainDispatcher();
			AssertDisplayed(handler, first);
			selected.IsVisible = true;
			item.CurrentItem = selected;
			DrainDispatcher();
			AssertDisplayed(handler, second);
			item.Items.Remove(selected);
			DrainDispatcher();
			AssertDisplayed(handler, first);
			Assert.Equal(1, created());
		});
	}

	[Fact]
	public void SwitchContentWithinSection_ShowsLazyPage()
	{
		RunTest((shell, handler, first, second, created) =>
		{
			var section = shell.CurrentItem.CurrentItem;
			var page = new ContentPage { Content = new Label { Text = "Third page" } };
			var content = new ShellContent { ContentTemplate = new DataTemplate(() => page) };
			section.Items.Add(content);
			section.CurrentItem = content;
			DrainDispatcher();

			Assert.Same(page, shell.CurrentPage);
			AssertDisplayed(handler, page);
			Assert.Equal(0, created());
			Assert.Null(((IShellContentController)shell.CurrentItem.Items[1].CurrentItem).Page);
		});
	}

	[Fact]
	public void SwitchItem_ObservesNewSelectionAndIgnoresOldItem()
	{
		RunTest((shell, handler, first, second, created) =>
		{
			var oldItem = shell.CurrentItem;
			var page = new ContentPage { Content = new Label { Text = "Other item" } };
			var other = new FlyoutItem { Items = { new ShellContent { Content = page } } };
			shell.Items.Add(other);
			shell.CurrentItem = other;
			DrainDispatcher();
			oldItem.CurrentItem = oldItem.Items[1];
			DrainDispatcher();
			Assert.Equal(0, created());
			AssertDisplayed(handler, page);

			shell.CurrentItem = oldItem;
			DrainDispatcher();
			AssertDisplayed(handler, second);
			oldItem.CurrentItem = oldItem.Items[0];
			DrainDispatcher();
			AssertDisplayed(handler, first);
		});
	}

	[Fact]
	public void Disconnect_WithQueuedSelection_DoesNotCreatePage()
	{
		RunTest((shell, handler, first, second, created) =>
		{
			shell.CurrentItem.CurrentItem = shell.CurrentItem.Items[1];
			((IElementHandler)handler).DisconnectHandler();
			DrainDispatcher();
			Assert.Equal(0, created());
		});
	}

	[Fact]
	public void Rebind_ObservesReplacementAndIgnoresPreviousShell()
	{
		RunTest((shell, handler, first, second, created) =>
		{
			var replacementPage = new ContentPage { Content = new Label { Text = "Replacement" } };
			var replacementTarget = new ContentPage { Content = new Label { Text = "Replacement target" } };
			var replacement = new Shell
			{
				Items =
				{
					new TabBar
					{
						Items =
						{
							new ShellContent { Content = replacementPage },
							new ShellContent { ContentTemplate = new DataTemplate(() => replacementTarget) },
						},
					},
				},
			};
			var window = new Window(replacement);
			shell.CurrentItem.CurrentItem = shell.CurrentItem.Items[1];
			handler.SetVirtualView(replacement);
			shell.CurrentItem.CurrentItem = shell.CurrentItem.Items[1];
			DrainDispatcher();
			Assert.Equal(0, created());
			AssertDisplayed(handler, replacementPage);
			replacement.CurrentItem.CurrentItem = replacement.CurrentItem.Items[1];
			DrainDispatcher();
			AssertDisplayed(handler, replacementTarget);
			((IElementHandler)handler).DisconnectHandler();
			shell.CurrentItem = new FlyoutItem { Items = { new ShellContent { Content = first } } };
			shell.FlyoutIsPresented = !shell.FlyoutIsPresented;
			DrainDispatcher();
		});
	}

	[Fact]
	public void Disconnect_DetachesSelectionNotifications()
	{
		RunTest((shell, handler, first, second, created) =>
		{
			((IElementHandler)handler).DisconnectHandler();
			shell.CurrentItem.CurrentItem = shell.CurrentItem.Items[1];
			DrainDispatcher();
			Assert.Equal(0, created());
		});
	}

	static void RunTest(Action<Shell, ShellHandler, ContentPage, ContentPage, Func<int>> test)
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
				var first = new ContentPage { BackgroundColor = Microsoft.Maui.Graphics.Colors.White, Content = new Label { Text = "First page", TextColor = Microsoft.Maui.Graphics.Colors.Black } };
				var second = new ContentPage { BackgroundColor = Microsoft.Maui.Graphics.Colors.White, Content = new Label { Text = "Second page", TextColor = Microsoft.Maui.Graphics.Colors.Black } };
				var count = 0;
				var shell = new Shell
				{
					Items =
					{
						new TabBar
						{
							Items =
							{
								new ShellContent { Route = "one", ContentTemplate = new DataTemplate(() => first) },
								new ShellContent { Route = "two", ContentTemplate = new DataTemplate(() => { count++; return second; }) },
							},
						},
					},
				};
				var window = new Window(shell);
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
						ShowActivated = false,
						ShowInTaskbar = false,
					};
					nativeWindow.Show();
					DrainDispatcher();
					test(shell, handler, first, second, () => count);
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
		Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Shell test timed out.");
		if (failure != null)
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
	}

	static void CompleteNavigation(Task navigation)
	{
		var dispatcher = Dispatcher.CurrentDispatcher;
		var frame = new DispatcherFrame();
		var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
		timer.Tick += (_, _) => frame.Continue = false;
		timer.Start();
		_ = navigation.ContinueWith(_ => dispatcher.BeginInvoke(
			new Action(() => frame.Continue = false)), TaskScheduler.Default);
		if (!navigation.IsCompleted)
			Dispatcher.PushFrame(frame);
		timer.Stop();
		Assert.True(navigation.IsCompleted, "Shell navigation did not complete.");
		navigation.GetAwaiter().GetResult();
	}

	static void DrainDispatcher() =>
		Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

	static void AssertDisplayed(ShellHandler handler, Page page)
	{
		Assert.NotNull(page.Handler?.PlatformView);
		Assert.Contains(Descendants(handler.PlatformView),
			element => element is System.Windows.Controls.ContentControl host &&
				ReferenceEquals(host.Content, page.Handler.PlatformView));
		Assert.True(handler.PlatformView.IsVisible);
		Assert.True(handler.PlatformView.ActualWidth > 0);

		if (Environment.GetEnvironmentVariable("SHELL_SECTION_RESULTS") is { Length: > 0 } output)
		{
			Directory.CreateDirectory(output);
			var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 600, 96, 96,
				System.Windows.Media.PixelFormats.Pbgra32);
			bitmap.Render(handler.PlatformView);
			var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
			encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
			using var stream = File.Create(Path.Combine(output, $"{Guid.NewGuid():N}.png"));
			encoder.Save(stream);
		}
	}

	static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
	{
		foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root)
			.OfType<System.Windows.DependencyObject>())
		{
			yield return child;
			foreach (var descendant in Descendants(child))
				yield return descendant;
		}
	}
}
