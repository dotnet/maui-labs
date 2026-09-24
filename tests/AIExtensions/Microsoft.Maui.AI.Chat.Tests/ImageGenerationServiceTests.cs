using System.Drawing;
using AIExtensions.Sample.ChatPlayground.Features.Images;
using AIExtensions.Sample.ChatPlayground.Models;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the installed SDK.
public sealed class ImageGenerationServiceTests
{
    [Fact]
    public async Task DescribedGenerator_ExposesMetadataWithoutInitializingProvider()
    {
        var created = 0;
        var provider = new TestImageGenerator((_, _, _) =>
            Task.FromResult(new ImageGenerationResponse([new DataContent(new byte[] { 1, 2 }, "image/png")])));
        using var generator = new DescribedImageGenerator(
            () =>
            {
                created++;
                return provider;
            },
            new ImageGeneratorDescriptor("test", "Test image", "On device", true, false));

        Assert.Equal("test", generator.GetService<ImageGeneratorDescriptor>()?.Id);
        Assert.Same(generator, generator.GetService<IImageGenerator>());
        Assert.Equal(0, created);

        var images = await new ImageGenerationService().GenerateAsync(generator, "a robot", null);
        Assert.Equal(new byte[] { 1, 2 }, Assert.Single(images).Bytes);
        Assert.Equal(1, created);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void DescribedGenerator_InvalidDescriptor_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DescribedImageGenerator(
            () => new TestImageGenerator((_, _, _) =>
                Task.FromResult(new ImageGenerationResponse([]))),
            new ImageGeneratorDescriptor("", "Test", "On device", true, false)));
    }

    [Fact]
    public async Task GenerateAsync_WithOriginalAndOptions_PassesThemToGenerator()
    {
        var original = new ImageAttachment("input.png", "image/png", [3, 4]);
        var options = ImageGenerationService.CreateOptions("2", new Size(1024, 1024), "image/jpeg")!;
        var imageUri = new Uri("https://example.test/image.png");
        var generator = new TestImageGenerator((request, receivedOptions, _) =>
        {
            Assert.Equal("edit this image", request.Prompt);
            var input = Assert.IsType<DataContent>(Assert.Single(request.OriginalImages!));
            Assert.Equal(original.Bytes, input.Data.ToArray());
            Assert.Equal("image/png", input.MediaType);
            Assert.Same(options, receivedOptions);
            return Task.FromResult(new ImageGenerationResponse(
                [new DataContent(new byte[] { 5, 6 }, "image/png"), new UriContent(imageUri, "image/png")]));
        });

        var images = await new ImageGenerationService().GenerateAsync(
            generator, " edit this image ", original, options);

        Assert.Equal(2, images.Count);
        Assert.Equal(new byte[] { 5, 6 }, images[0].Bytes);
        Assert.Equal("image/png", images[0].MediaType);
        Assert.Equal(imageUri, images[1].Uri);
        Assert.Equal(1, generator.CallCount);
    }

    [Fact]
    public void CreateOptions_UsesDefaultsOrExplicitSettings()
    {
        Assert.Null(ImageGenerationService.CreateOptions("", null, "Provider default"));
        var options = ImageGenerationService.CreateOptions("2", new Size(1536, 1024), "image/webp");
        Assert.Equal(2, options?.Count);
        Assert.Equal(new Size(1536, 1024), options?.ImageSize);
        Assert.Equal("image/webp", options?.MediaType);
        Assert.Null(options?.ResponseFormat);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1.5")]
    [InlineData("abc")]
    public void CreateOptions_InvalidCount_Throws(string count) =>
        Assert.Throws<ArgumentException>(() => ImageGenerationService.CreateOptions(
            count, null, "Provider default"));

    [Fact]
    public void CreateOptions_InvalidSizeOrMediaType_Throws()
    {
        Assert.Throws<ArgumentException>(() => ImageGenerationService.CreateOptions(
            "", new Size(0, 1024), "Provider default"));
        Assert.Throws<ArgumentException>(() => ImageGenerationService.CreateOptions(
            "", null, "image/bmp"));
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
    }

    private sealed class TestImageGenerator(
        Func<ImageGenerationRequest, ImageGenerationOptions?, CancellationToken, Task<ImageGenerationResponse>> generate)
        : IImageGenerator
    {
        public int CallCount { get; private set; }

        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request, ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return generate(request, options, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
#pragma warning restore MEAI001
