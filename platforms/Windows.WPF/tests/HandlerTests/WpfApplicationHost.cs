using WApplication = System.Windows.Application;

namespace HandlerTests;

internal static class WpfApplicationHost
{
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
	static readonly Lazy<Task<WApplication>> Application = new(StartApplication);
	static bool _timedOut;

	public static void Run(Action<WApplication> test)
	{
		if (Volatile.Read(ref _timedOut))
			throw new InvalidOperationException("The shared WPF dispatcher timed out in a previous test.");
		var application = Application.Value.WaitAsync(Timeout).GetAwaiter().GetResult();
		var operation = application.Dispatcher.InvokeAsync(() =>
		{
			var previousMainWindow = application.MainWindow;
			var previousApplication = Microsoft.Maui.Controls.Application.Current;
			try
			{
				test(application);
			}
			finally
			{
				application.MainWindow = previousMainWindow;
				Microsoft.Maui.Controls.Application.Current = previousApplication;
			}
		});
		try
		{
			operation.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
		}
		catch (TimeoutException) when (!operation.Task.IsCompleted)
		{
			Volatile.Write(ref _timedOut, true);
			operation.Abort();
			throw;
		}
	}

	static Task<WApplication> StartApplication()
	{
		var started = new TaskCompletionSource<WApplication>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() =>
		{
			WApplication application;
			try
			{
				application = new WApplication { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
			}
			catch (Exception exception)
			{
				started.SetException(exception);
				return;
			}
			application.Dispatcher.BeginInvoke(new Action(() => started.SetResult(application)));
			// WPF shutdown permanently disables Window.Show for this process, not just this dispatcher.
			application.Run();
		}) { IsBackground = true, Name = "WPF test application" };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return started.Task;
	}
}
