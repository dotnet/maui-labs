using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>Covers <see cref="TextMessageContent"/> and <see cref="MediaMessageContent"/>.</summary>
public class MessageContentTests
{
    [Fact]
    public void Text_DefaultsToEmptyWithGeneratedId()
    {
        var content = new TextMessageContent();

        Assert.Equal(string.Empty, content.Text);
        Assert.True(content.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(content.Id));
        Assert.Equal(ChatContentPresentation.Bubble, content.Presentation);
    }

    [Fact]
    public void Presentation_ChangeRaisesContentChanged()
    {
        var content = new TextMessageContent("hello");
        var changes = 0;
        content.ContentChanged += (_, _) => changes++;

        content.Presentation = ChatContentPresentation.Bare;

        Assert.Equal(ChatContentPresentation.Bare, content.Presentation);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Text_ExplicitId_IsKept()
    {
        var content = new TextMessageContent("hi", "c-1");

        Assert.Equal("c-1", content.Id);
        Assert.Equal("hi", content.Text);
    }

    [Fact]
    public void Text_BlankId_IsReplacedWithGeneratedId()
    {
        var content = new TextMessageContent("hi", "   ");

        Assert.False(string.IsNullOrWhiteSpace(content.Id));
        Assert.NotEqual("   ", content.Id);
    }

    [Fact]
    public void Text_NullValue_CoercesToEmpty()
    {
        var content = new TextMessageContent("hi");

        content.Text = null!;

        Assert.Equal(string.Empty, content.Text);
    }

    [Fact]
    public void Text_Set_RaisesContentChangedAndPropertyChanged()
    {
        var content = new TextMessageContent();
        using var properties = new PropertyRecorder(content);
        var contentChanges = 0;
        content.ContentChanged += (_, _) => contentChanges++;

        content.Text = "hello";

        Assert.Equal(1, contentChanges);
        Assert.Contains(nameof(TextMessageContent.Text), properties.Names);
    }

    [Fact]
    public void Text_SetSameValue_DoesNotRaise()
    {
        var content = new TextMessageContent("hello");
        var contentChanges = 0;
        content.ContentChanged += (_, _) => contentChanges++;

        content.Text = "hello";

        Assert.Equal(0, contentChanges);
    }

    [Fact]
    public void Append_GrowsTextInPlaceAndRaisesOncePerChunk()
    {
        var content = new TextMessageContent("He");
        var contentChanges = 0;
        content.ContentChanged += (_, _) => contentChanges++;

        content.Append("llo");
        content.Append(", world");

        Assert.Equal("Hello, world", content.Text);
        Assert.Equal(2, contentChanges);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Append_WithNothing_IsIgnored(string? chunk)
    {
        var content = new TextMessageContent("Hi");
        var contentChanges = 0;
        content.ContentChanged += (_, _) => contentChanges++;

        content.Append(chunk);

        Assert.Equal("Hi", content.Text);
        Assert.Equal(0, contentChanges);
    }

    [Fact]
    public void StructuredText_ReplacesFallbackAndDocumentWithOneContentSignal()
    {
        var first = new object();
        var second = new object();
        var content = new StructuredTextMessageContent<object>(
            "first",
            first);
        using var properties = new PropertyRecorder(content);
        var contentChanges = 0;
        content.ContentChanged += (_, _) => contentChanges++;

        content.Replace("second", second);

        Assert.Equal("second", content.Text);
        Assert.Same(second, content.Document);
        Assert.Equal(1, contentChanges);
        Assert.Contains(nameof(content.Document), properties.Names);
        Assert.Contains(nameof(content.Text), properties.Names);
    }

    [Fact]
    public void ContentChanged_Unsubscribed_StopsRaising()
    {
        var content = new TextMessageContent();
        var changes = 0;
        void Handler(object? sender, EventArgs e) => changes++;

        content.ContentChanged += Handler;
        content.Append("a");
        content.ContentChanged -= Handler;
        content.Append("b");

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Media_FromUri_ExposesUriAndNoData()
    {
        var uri = new Uri("https://example.test/cat.png");
        var media = new MediaMessageContent(uri, "image/png");

        Assert.Equal(uri, media.Uri);
        Assert.False(media.HasData);
        Assert.Equal(0, media.ByteCount);
        Assert.True(media.IsImage);
    }

    [Fact]
    public void Media_FromBytes_ExposesReusableBuffer()
    {
        var media = new MediaMessageContent(new byte[] { 1, 2, 3 }, "application/pdf");

        Assert.Null(media.Uri);
        Assert.True(media.HasData);
        Assert.Equal(3, media.ByteCount);
        Assert.False(media.IsImage);

        // The buffer is readable more than once, which is what cell recycling needs.
        Assert.Equal(media.Data.ToArray(), media.Data.ToArray());
    }

    [Fact]
    public void Media_WithEmptyBuffer_Throws() =>
        Assert.Throws<ArgumentException>(() => new MediaMessageContent(ReadOnlyMemory<byte>.Empty, "image/png"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Media_WithBlankMediaType_Throws(string? mediaType)
    {
        Assert.ThrowsAny<ArgumentException>(() => new MediaMessageContent(new byte[] { 1 }, mediaType!));
        Assert.ThrowsAny<ArgumentException>(() =>
            new MediaMessageContent(new Uri("https://example.test/a.png"), mediaType!));
    }

    [Fact]
    public void Media_WithNullUri_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new MediaMessageContent((Uri)null!, "image/png"));

    [Fact]
    public void Media_IsImage_IsCaseInsensitive()
    {
        var media = new MediaMessageContent(new byte[] { 1 }, "IMAGE/PNG");

        Assert.True(media.IsImage);
    }

    [Fact]
    public void Media_DisplayName_PrefersAltTextThenFileName()
    {
        var media = new MediaMessageContent(new byte[] { 1 }, "image/png");
        Assert.Equal("image/png", media.DisplayName);

        media.FileName = "cat.png";
        Assert.Equal("cat.png", media.DisplayName);

        media.AltText = "A cat";
        Assert.Equal("A cat", media.DisplayName);
    }

    [Fact]
    public void Media_MetadataChanges_RaiseContentChanged()
    {
        var media = ChatFactory.Image();
        var changes = 0;
        media.ContentChanged += (_, _) => changes++;

        media.FileName = "other.png";
        media.AltText = "Something";

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Media_Dimensions_DefaultToZeroAndAreSettable()
    {
        var media = ChatFactory.Image();
        Assert.Equal(0, media.PixelWidth);
        Assert.Equal(0, media.PixelHeight);

        media.PixelWidth = 640;
        media.PixelHeight = 480;

        Assert.Equal(640, media.PixelWidth);
        Assert.Equal(480, media.PixelHeight);
    }
}
