using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Services;

/// <summary>UI label and exact vector-space identity for one registered embedding generator.</summary>
public sealed record EmbeddingGeneratorDescriptor(
    string Id, string DisplayName, string Description, string IndexIdentity);

/// <summary>Exposes UI metadata without opening the embedding model until it is used.</summary>
public sealed class DescribedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Lazy<IEmbeddingGenerator<string, Embedding<float>>> _inner;
    private readonly EmbeddingGeneratorDescriptor _descriptor;

    public DescribedEmbeddingGenerator(
        Func<IEmbeddingGenerator<string, Embedding<float>>> createGenerator,
        EmbeddingGeneratorDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(createGenerator);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Id) ||
            string.IsNullOrWhiteSpace(descriptor.DisplayName) ||
            string.IsNullOrWhiteSpace(descriptor.Description) ||
            string.IsNullOrWhiteSpace(descriptor.IndexIdentity))
            throw new ArgumentException("An embedding generator needs an ID, label, description, and model identity.", nameof(descriptor));

        _descriptor = descriptor;
        _inner = new Lazy<IEmbeddingGenerator<string, Embedding<float>>>(
            () => createGenerator() ?? throw new InvalidOperationException(
                $"The {_descriptor.DisplayName} embedding generator is unavailable."));
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.Value.GenerateAsync(values, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType == typeof(EmbeddingGeneratorDescriptor))
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
