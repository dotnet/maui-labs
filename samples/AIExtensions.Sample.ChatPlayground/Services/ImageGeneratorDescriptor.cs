using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Services;

#pragma warning disable MEAI001 // IImageGenerator is experimental in the installed SDK.
public sealed record ImageGeneratorDescriptor(
    string Id, string DisplayName, string Description, bool SupportsEdits);

/// <summary>Exposes image-provider metadata without creating the provider until first use.</summary>
public sealed class DescribedImageGenerator : IImageGenerator
{
    private readonly Lazy<IImageGenerator> _inner;
    private readonly ImageGeneratorDescriptor _descriptor;

    public DescribedImageGenerator(Func<IImageGenerator> createGenerator, ImageGeneratorDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(createGenerator);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Id) ||
            string.IsNullOrWhiteSpace(descriptor.DisplayName) ||
            string.IsNullOrWhiteSpace(descriptor.Description))
            throw new ArgumentException("An image generator needs an ID, label, and description.", nameof(descriptor));

        _descriptor = descriptor;
        _inner = new Lazy<IImageGenerator>(() => createGenerator() ??
            throw new InvalidOperationException($"The {_descriptor.DisplayName} image generator is unavailable."));
    }

    public Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request, ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.Value.GenerateAsync(request, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType == typeof(ImageGeneratorDescriptor))
            return _descriptor;
        if (serviceKey is null && serviceType.IsInstanceOfType(this))
            return this;
        return _inner.IsValueCreated ? _inner.Value.GetService(serviceType, serviceKey) : null;
    }

    public void Dispose()
    {
        if (_inner.IsValueCreated)
            _inner.Value.Dispose();
    }
}
#pragma warning restore MEAI001
