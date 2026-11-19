using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Exposes metadata for a registered image generator.</summary>
public sealed class DescribedImageGenerator(IImageGenerator inner, ImageGeneratorDescriptor descriptor)
    : IImageGenerator
{
    public Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        inner.GenerateAsync(request, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType == typeof(ImageGeneratorDescriptor))
            return descriptor;
        if (serviceKey is null && serviceType.IsInstanceOfType(this))
            return this;
        return inner.GetService(serviceType, serviceKey);
    }

    public void Dispose() => inner.Dispose();
}
