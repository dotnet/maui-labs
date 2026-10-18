#if WINDOWS
using Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the pinned SDK.

public class WindowsAIImageGeneratorTests : ImageGeneratorTestsBase
{
	protected override IImageGenerator CreateImageGenerator() => new WindowsAIImageGenerator();

	// Windows Generate* is synchronous; a background-posted UI callback must run during warm inference.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GenerateAsync_WarmUiCaller_CanDispatchCancellation()
	{
		using var generator = new WindowsAIImageGenerator();
		using var timeout = new CancellationTokenSource(ImageGeneratorTestData.RequestTimeout);
		await generator.GenerateAsync(
			new ImageGenerationRequest(ImageGeneratorTestData.Prompt), cancellationToken: timeout.Token);

		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
			Task<ImageGenerationResponse>? pending = null;
			var heartbeat = Task.Run(async () =>
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250));
				var watch = System.Diagnostics.Stopwatch.StartNew();
				return await MainThread.InvokeOnMainThreadAsync(() =>
				{
					var observation = (Latency: watch.Elapsed, WasPending: pending is { IsCompleted: false });
					cancellation.Cancel();
					return observation;
				});
			});
			pending = generator.GenerateAsync(
				new ImageGenerationRequest(ImageGeneratorTestData.Prompt), cancellationToken: cancellation.Token);

			// Observe both tasks before assertions/disposal, even if the UI was blocked until inference finished.
			var error = await Record.ExceptionAsync(() => pending);
			var observation = await heartbeat;
			Assert.True(observation.WasPending, "The UI callback did not run while generation was pending.");
			Assert.True(observation.Latency < TimeSpan.FromSeconds(1),
				$"The background-posted callback waited {observation.Latency} for the UI thread.");
			Assert.IsAssignableFrom<OperationCanceledException>(error);
		});
	}
}

#pragma warning restore MEAI001
#endif
