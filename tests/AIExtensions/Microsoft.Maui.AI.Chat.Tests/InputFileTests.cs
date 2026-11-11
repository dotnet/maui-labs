using AIExtensions.Sample.ChatPlayground;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class InputFileTests
{
    [Theory]
    [InlineData("photo.JPG", null, "image/jpeg")]
    [InlineData("scan.PDF", "application/octet-stream", "application/pdf")]
    [InlineData("photo.bin", "IMAGE/PNG; charset=binary", "image/png")]
    [InlineData("animation.gif", null, "image/gif")]
    [InlineData("photo.webp", null, "image/webp")]
    [InlineData("scan.heic", null, "image/heic")]
    [InlineData("scan.tif", null, "image/tiff")]
    public async Task ReadAsync_ResolvesFileTypesAndBorrowsTheInput(string name, string? contentType, string expected)
    {
        using var source = new MemoryStream([1, 2, 3]);
        source.Position = 1;
        var file = await InputFile.ReadAsync(source, name, contentType);
        Assert.Equal(name, file.FileName);
        Assert.Equal(expected, file.MediaType);
        Assert.Equal(new byte[] { 2, 3 }, file.Bytes);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task ReadAsync_RejectsUnsupportedFilesBeforeReading()
    {
        using var source = new MemoryStream([1]);
        await Assert.ThrowsAsync<NotSupportedException>(() => InputFile.ReadAsync(source, "file.exe"));
        Assert.Equal(0, source.Position);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task ReadAsync_CancellationLeavesCallerInputOpen()
    {
        using var source = new MemoryStream([1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InputFile.ReadAsync(source, "photo.png", cancellationToken: cancellation.Token));
        Assert.Equal(0, source.Position);
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(20 * 1024 * 1024)]
    [InlineData(20 * 1024 * 1024 + 1)]
    public async Task ReadAsync_EnforcesTheExactSharedFileLimit(int length)
    {
        using var source = new MemoryStream(new byte[length], writable: false);
        if (length == 20 * 1024 * 1024)
            Assert.Equal(length, (await InputFile.ReadAsync(source, "scan.pdf")).Bytes.Length);
        else
            Assert.Contains("20 MB", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InputFile.ReadAsync(source, "scan.pdf"))).Message);
        Assert.True(source.CanRead);
    }
}
