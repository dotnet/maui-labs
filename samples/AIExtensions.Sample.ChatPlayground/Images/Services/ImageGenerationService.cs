using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

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
            : new ImageGenerationRequest(prompt.Trim(), [new DataContent(original.Bytes, original.MediaType)]);
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
}
