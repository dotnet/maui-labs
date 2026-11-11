using System.Windows.Media;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace HandlerTests;

[Collection("Layout handlers")]
public class ScreenshotCaptureTests
{
	[Fact]
	public void CaptureAsync_BackgroundCaller_ResolvesMainWindowOnDispatcher()
	{
		WpfApplicationHost.Run(application =>
		{
			var window = new System.Windows.Window
			{
				Width = 120,
				Height = 100,
				ShowActivated = false,
				ShowInTaskbar = false,
				Left = -10000,
				Content = new System.Windows.Controls.Border { Background = Brushes.Red },
			};
			try
			{
				window.Show();
				application.MainWindow = window;
				var capture = Task.Run(() => new WPFScreenshot().CaptureAsync());
				var frame = new System.Windows.Threading.DispatcherFrame();
				_ = capture.ContinueWith(_ => application.Dispatcher.BeginInvoke(
					new Action(() => frame.Continue = false)), TaskScheduler.Default);
				System.Windows.Threading.Dispatcher.PushFrame(frame);
				var result = capture.GetAwaiter().GetResult();
				var dpi = VisualTreeHelper.GetDpi(window);
				Assert.Equal((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), result.Width);
				Assert.Equal((int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), result.Height);
			}
			finally
			{
				window.Close();
			}
		});
	}
}
