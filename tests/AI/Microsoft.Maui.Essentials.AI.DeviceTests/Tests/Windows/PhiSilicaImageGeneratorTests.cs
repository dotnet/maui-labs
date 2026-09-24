#if WINDOWS
using Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class PhiSilicaImageGeneratorTests
{
	[Fact]
	public async Task GenerateAsync_CancelledBeforeSetup_DoesNotStartModel()
	{
		using var generator = new PhiSilicaImageGenerator();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

#pragma warning disable MEAI001 // The image-generation request API is experimental.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			generator.GenerateAsync(
				new ImageGenerationRequest("A blue sailboat on a lake."),
				cancellationToken: cancellation.Token));
#pragma warning restore MEAI001
	}
}
#endif
