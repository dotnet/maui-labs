using AIExtensions.Sample.ChatPlayground;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class DocumentMediaTypesTests
{
    [Theory]
    [InlineData("scan.png", "image/png")]
    [InlineData("photo.JPEG", "image/jpeg")]
    [InlineData("page.heic", "image/heic")]
    [InlineData("archive.tiff", "image/tiff")]
    [InlineData("report.PDF", "application/pdf")]
    [InlineData("notes.txt", null)]
    public void FromFileName_UsesSupportedDocumentExtensions(string fileName, string? expected) =>
        Assert.Equal(expected, DocumentMediaTypes.FromFileName(fileName));
}
