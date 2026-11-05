using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;

namespace Essentials.Tests;

public sealed class ScreenshotTests
{
	[Fact]
	public async Task Dispatcher_MarshalsCaptureWhenCallerDoesNotOwnGtkContext()
	{
		var callerThread = Environment.CurrentManagedThreadId;
		var captureThread = callerThread;
		var dispatcher = new GtkUiDispatcher(
			() => false,
			callback => _ = Task.Run(() => callback()));

		var result = await dispatcher.InvokeAsync(() =>
		{
			captureThread = Environment.CurrentManagedThreadId;
			return 42;
		});

		Assert.Equal(42, result);
		Assert.NotEqual(callerThread, captureThread);
	}

	[Fact]
	public async Task Dispatcher_ExecutesInlineForGtkOwner()
	{
		var callerThread = Environment.CurrentManagedThreadId;
		var dispatcher = new GtkUiDispatcher(
			() => true,
			_ => throw new InvalidOperationException("Owner dispatch must not schedule."));

		var captureThread = await dispatcher.InvokeAsync(
			() => Environment.CurrentManagedThreadId);

		Assert.Equal(callerThread, captureThread);
	}
}
