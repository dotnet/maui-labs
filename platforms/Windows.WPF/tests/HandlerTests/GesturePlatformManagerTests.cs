#if !NET10_0
using System.Windows;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;

namespace HandlerTests;

[Collection("Layout handlers")]
public class GesturePlatformManagerTests
{
	[Fact]
	public void MouseLeave_AfterViewLeavesWindow_ReportsUnknownRelativePositionInsteadOfThrowing()
	{
		WpfApplicationHost.Run(_ =>
		{
			DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
			using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Microsoft.Maui.Controls.Application>().Build();
			var context = new WPFMauiContext(app.Services);

			var target = new Label();
			var other = new Label();
			other.ToHandler(context);
			var exits = new List<PointerEventArgs>();
			var pointer = new PointerGestureRecognizer();
			pointer.PointerExited += (_, args) => exits.Add(args);
			target.GestureRecognizers.Add(pointer);
			var handler = target.ToHandler(context);
			var platformView = Assert.IsAssignableFrom<FrameworkElement>(handler.PlatformView);
			Assert.Null(PresentationSource.FromVisual(platformView));

			using var manager = new WPFGesturePlatformManagerFactory().CreateGesturePlatformManager(handler);
			platformView.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });

			var exit = Assert.Single(exits);
			Assert.NotNull(exit.GetPosition(null));
			Assert.Null(exit.GetPosition(other));
		});
	}
}
#endif
