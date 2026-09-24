using System.Globalization;
using AIExtensions.Sample.ChatPlayground.Models;
using Microsoft.Extensions.AI;
using Size = System.Drawing.Size;

namespace AIExtensions.Sample.ChatPlayground.Features.Images;

#pragma warning disable MEAI001 // IImageGenerator and its request options are experimental in the installed SDK.
public sealed record GeneratedImage(byte[]? Bytes, Uri? Uri, string MediaType);

/// <summary>Runs real image generation or edits without depending on MAUI controls.</summary>
public sealed class ImageGenerationService
{
    public async Task<IReadOnlyList<GeneratedImage>> GenerateAsync(
        IImageGenerator generator, string prompt, ImageAttachment? original,
        ImageGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var request = original is null
            ? new ImageGenerationRequest(prompt.Trim())
            : new ImageGenerationRequest(prompt.Trim(),
                [new DataContent(original.Bytes, original.MediaType)]);
        var response = await generator.GenerateAsync(request, options, cancellationToken).ConfigureAwait(false);
        var images = new List<GeneratedImage>();
        foreach (var content in response.Contents)
        {
            switch (content)
            {
                case DataContent data when data.HasTopLevelMediaType("image") && !data.Data.IsEmpty:
                    images.Add(new GeneratedImage(data.Data.ToArray(), null, data.MediaType));
                    break;
                case UriContent uri when uri.HasTopLevelMediaType("image") &&
                    uri.Uri.IsAbsoluteUri && uri.Uri.Scheme is "https" or "http":
                    images.Add(new GeneratedImage(null, uri.Uri, uri.MediaType));
                    break;
                case DataContent or UriContent:
                    throw new InvalidDataException("The image generator returned empty or unsupported image content.");
            }
        }
        if (images.Count == 0)
            throw new InvalidDataException("The image generator returned no images.");
        return images;
    }

    public static ImageGenerationOptions? CreateOptions(string count, Size? size, string mediaType)
    {
        int? parsedCount = null;
        if (!string.IsNullOrWhiteSpace(count))
        {
            if (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                throw new ArgumentException("Image count must be a positive integer or blank for the provider default.", nameof(count));
            parsedCount = value;
        }
        if (size is { Width: <= 0 } or { Height: <= 0 })
            throw new ArgumentException("Image width and height must be positive.", nameof(size));
        if (mediaType is not ("Provider default" or "image/png" or "image/jpeg" or "image/webp"))
            throw new ArgumentException("Choose a supported image format.", nameof(mediaType));

        if (parsedCount is null && size is null && mediaType == "Provider default")
            return null;

        return new ImageGenerationOptions
        {
            Count = parsedCount,
            ImageSize = size,
            MediaType = mediaType == "Provider default" ? null : mediaType,
        };
    }
}
#pragma warning restore MEAI001
