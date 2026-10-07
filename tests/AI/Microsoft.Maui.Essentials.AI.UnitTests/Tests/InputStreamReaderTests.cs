using Xunit;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class InputStreamReaderTests
{
    [Fact]
    public async Task ReadBytesAsync_ExactLimitPreservesBytesAndInputOwnership()
    {
        using var source = new MemoryStream([1, 2, 3]);
        var bytes = await InputStreamReader.ReadBytesAsync(source, 3, "Too large.", CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task ReadBytesAsync_ExceedingLimitThrowsFeatureSpecificError()
    {
        using var source = new MemoryStream([1, 2, 3, 4]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InputStreamReader.ReadBytesAsync(source, 3, "Documents must be 20 MB or smaller.", CancellationToken.None));
        Assert.Equal("Documents must be 20 MB or smaller.", error.Message);
    }

    [Fact]
    public async Task ReadBytesAsync_CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var source = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InputStreamReader.ReadBytesAsync(source, 3, "Too large.", cancellation.Token));
    }
}
