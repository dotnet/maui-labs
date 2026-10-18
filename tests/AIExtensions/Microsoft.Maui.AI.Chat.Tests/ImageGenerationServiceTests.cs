using System.Drawing;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the installed SDK.
public sealed class ImageGenerationServiceTests
{
    [Fact]
    public async Task DescribedGenerator_ExposesMetadataAndDelegatesToProvider()
    {
        var provider = new TestImageGenerator((_, _, _) =>
            Task.FromResult(new ImageGenerationResponse([new DataContent(new byte[] { 1, 2 }, "image/png")])));
        using var generator = new DescribedImageGenerator(
            provider,
            new ImageGeneratorDescriptor("test", "Test image", "On device", true));

        Assert.Equal("test", generator.GetService<ImageGeneratorDescriptor>()?.Id);
        Assert.Same(generator, generator.GetService<IImageGenerator>());

        var images = await new ImageGenerationService().GenerateAsync(generator, "a robot", null);
        Assert.Equal(new byte[] { 1, 2 }, Assert.Single(images).Bytes);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_WithOriginalAndOptions_PassesThemToGenerator()
    {
        using var cancellation = new CancellationTokenSource();
        var original = new ImageAttachment("input.png", "image/png", [3, 4]);
        var settings = CreateSettings();
        settings.SelectedSize = settings.ImageSizes.Single(size => size.Size == new Size(1024, 1024));
        settings.SelectedMediaType = "image/jpeg";
        var options = settings.CreateOptions()!;
        var imageUri = new Uri("https://example.test/image.png");
        var generator = new TestImageGenerator((request, receivedOptions, token) =>
        {
            Assert.Equal("edit this image", request.Prompt);
            var input = Assert.IsType<DataContent>(Assert.Single(request.OriginalImages!));
            Assert.Equal(original.Bytes, input.Data.ToArray());
            Assert.Equal("image/png", input.MediaType);
            Assert.Same(options, receivedOptions);
            Assert.Equal(cancellation.Token, token);
            return Task.FromResult(new ImageGenerationResponse(
                [new DataContent(new byte[] { 5, 6 }, "image/png"), new UriContent(imageUri, "image/png")]));
        });

        var images = await new ImageGenerationService().GenerateAsync(
            generator, " edit this image ", original, options, cancellation.Token);

        Assert.Equal(2, images.Count);
        Assert.Equal(new byte[] { 5, 6 }, images[0].Bytes);
        Assert.Equal("image/png", images[0].MediaType);
        Assert.Equal(imageUri, images[1].Uri);
        Assert.Equal(1, generator.CallCount);
        Assert.Equal(0, generator.DisposeCount);
    }

    [Fact]
    public async Task GenerateAsync_NormalizesDataAndUrisWithoutTakingOwnershipOfGenerator()
    {
        byte[] providerBytes = [1, 2, 3];
        var uri = new Uri("https://example.test/generated.png");
        var generator = new TestImageGenerator((request, options, _) =>
        {
            Assert.Equal("a robot", request.Prompt);
            Assert.True(request.OriginalImages is null || !request.OriginalImages.Any());
            Assert.Null(options);
            return Task.FromResult(new ImageGenerationResponse(
                [new TextContent("Revised prompt"), new DataContent(providerBytes, "image/png"),
                 new UriContent(uri, "image/png")]));
        });
        var service = new ImageGenerationService();

        var images = await service.GenerateAsync(generator, " a robot ", null);
        providerBytes[0] = 99;
        await service.GenerateAsync(generator, "a robot", null);

        Assert.Equal(2, images.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, images[0].Bytes);
        Assert.NotSame(providerBytes, images[0].Bytes);
        Assert.Null(images[0].Uri);
        Assert.Equal("image/png", images[0].MediaType);
        Assert.Null(images[1].Bytes);
        Assert.Equal(uri, images[1].Uri);
        Assert.Equal("image/png", images[1].MediaType);
        Assert.Equal(2, generator.CallCount);
        Assert.Equal(0, generator.DisposeCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task GenerateAsync_InvalidPrompt_DoesNotCallOrDisposeGenerator(string? prompt)
    {
        var generator = new TestImageGenerator((_, _, _) =>
            throw new InvalidOperationException("The provider should not be invoked."));

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new ImageGenerationService().GenerateAsync(generator, prompt!, null));

        Assert.Equal(0, generator.CallCount);
        Assert.Equal(0, generator.DisposeCount);
    }

    [Fact]
    public async Task GenerateAsync_NullGenerator_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new ImageGenerationService().GenerateAsync(null!, "a robot", null));
    }

    [Fact]
    public async Task GenerateAsync_ProviderFailure_PropagatesWithoutDisposingGenerator()
    {
        var expected = new NotSupportedException("The configured provider does not support this operation.");
        var generator = new TestImageGenerator((_, _, _) =>
            Task.FromException<ImageGenerationResponse>(expected));

        var actual = await Assert.ThrowsAsync<NotSupportedException>(() =>
            new ImageGenerationService().GenerateAsync(generator, "a robot", null));

        Assert.Same(expected, actual);
        Assert.Equal(1, generator.CallCount);
        Assert.Equal(0, generator.DisposeCount);
    }

    [Fact]
    public async Task GenerateAsync_InvalidOutputAfterValidImage_DoesNotReturnPartialSuccess()
    {
        var generator = new TestImageGenerator((_, _, _) =>
            Task.FromResult(new ImageGenerationResponse(
                [new DataContent(new byte[] { 1 }, "image/png"),
                 new UriContent(new Uri("https://example.test/not-an-image"), "text/plain")])));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImageGenerationService().GenerateAsync(generator, "a robot", null));

        Assert.Equal(0, generator.DisposeCount);
    }

    [Fact]
    public void ImageSettings_CreateOptions_UsesDefaultsOrExplicitSettings()
    {
        var settings = CreateSettings();
        Assert.Null(settings.CreateOptions());
        settings.SelectedSize = settings.ImageSizes.Single(size => size.Size == new Size(1536, 1024));
        settings.SelectedMediaType = "image/webp";

        var options = settings.CreateOptions();

        Assert.Null(options?.Count);
        Assert.Equal(new Size(1536, 1024), options?.ImageSize);
        Assert.Equal("image/webp", options?.MediaType);
        Assert.Null(options?.ResponseFormat);
    }

    [Fact]
    public void ImageSettings_InvalidSizeOrMediaType_Throws()
    {
        var settings = CreateSettings();
        settings.SelectedSize = new ImageSizeOption("Invalid size", new Size(0, 1024));
        Assert.Throws<ArgumentException>(settings.CreateOptions);
        settings.SelectedSize = settings.ImageSizes[0];
        settings.SelectedMediaType = "image/bmp";
        Assert.Throws<ArgumentException>(settings.CreateOptions);
    }

    [Fact]
    public void ImageSettings_RejectsDuplicateGeneratorIds()
    {
        var provider = new TestImageGenerator((_, _, _) =>
            Task.FromResult(new ImageGenerationResponse([])));
        Assert.Throws<ArgumentException>(() => new ImageSettingsViewModel([
            new DescribedImageGenerator(provider,
                new ImageGeneratorDescriptor("same", "First", "First provider", true)),
            new DescribedImageGenerator(provider,
                new ImageGeneratorDescriptor("same", "Second", "Second provider", false)),
        ]));
    }

    [Fact]
    public async Task GenerateAsync_EmptyOrInvalidOutputs_AreReported()
    {
        var service = new ImageGenerationService();
        foreach (var content in new AIContent?[]
        {
            null,
            new TextContent("no image"),
            new DataContent(Array.Empty<byte>(), "image/png"),
            new DataContent(new byte[] { 1 }, "text/plain"),
            new UriContent(new Uri("file:///tmp/image.png"), "image/png"),
        })
        {
            var response = new ImageGenerationResponse(content is null ? [] : [content]);
            var generator = new TestImageGenerator((_, _, _) => Task.FromResult(response));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.GenerateAsync(generator, "a robot", null));
        }
    }

    [Fact]
    public async Task GenerateAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var generator = new TestImageGenerator((_, _, token) =>
            Task.FromCanceled<ImageGenerationResponse>(token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ImageGenerationService().GenerateAsync(
                generator, "a robot", null, cancellationToken: cancellation.Token));

        Assert.Equal(1, generator.CallCount);
        Assert.Equal(0, generator.DisposeCount);
    }

    private static ImageSettingsViewModel CreateSettings() =>
        new([new DescribedImageGenerator(
            new TestImageGenerator((_, _, _) =>
                Task.FromResult(new ImageGenerationResponse([]))),
            new ImageGeneratorDescriptor("test", "Test generator", "Test description", true))]);

    private sealed class TestImageGenerator(
        Func<ImageGenerationRequest, ImageGenerationOptions?, CancellationToken, Task<ImageGenerationResponse>> generate)
        : IImageGenerator
    {
        public int CallCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request, ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return generate(request, options, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => DisposeCount++;
    }
}
#pragma warning restore MEAI001
