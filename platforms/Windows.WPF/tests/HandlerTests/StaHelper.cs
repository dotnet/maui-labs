using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HandlerTests;

/// <summary>
/// Helper to run WPF UI tests on the STA thread.
/// WPF controls must be created on the dispatcher thread.
/// </summary>
public static class StaHelper
{
	public static void RunOnSta(Action action)
	{
		Exception? exception = null;
		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				exception = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join(TimeSpan.FromSeconds(10));
		if (exception != null)
			throw new AggregateException("STA thread failed", exception);
	}
}
