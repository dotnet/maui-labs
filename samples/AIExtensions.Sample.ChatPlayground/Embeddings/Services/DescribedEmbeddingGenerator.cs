using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Exposes UI metadata for a registered embedding generator.</summary>
public sealed class DescribedEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    EmbeddingGeneratorDescriptor descriptor)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        inner.GenerateAsync(values, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType == typeof(EmbeddingGeneratorDescriptor))
            return descriptor;
        if (serviceKey is null && serviceType.IsInstanceOfType(this))
            return this;
        return inner.GetService(serviceType, serviceKey);
    }

    public void Dispose() => inner.Dispose();
}
