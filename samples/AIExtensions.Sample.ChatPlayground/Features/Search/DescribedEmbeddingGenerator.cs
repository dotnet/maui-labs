using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Search;

/// <summary>Exposes an embedding model's index identity without opening it until generation.</summary>
public sealed class DescribedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Lazy<IEmbeddingGenerator<string, Embedding<float>>> _inner;
    private readonly ChatSearchDescriptor _descriptor;

    public DescribedEmbeddingGenerator(
        Func<IEmbeddingGenerator<string, Embedding<float>>> createGenerator,
        ChatSearchDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(createGenerator);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Id) || descriptor.Id == ChatSearchDescriptor.ContainsId ||
            string.IsNullOrWhiteSpace(descriptor.DisplayName) ||
            string.IsNullOrWhiteSpace(descriptor.Description) ||
            string.IsNullOrWhiteSpace(descriptor.IndexIdentity) ||
            descriptor.IndexIdentity == ChatSearchDescriptor.Contains.IndexIdentity ||
            !Enum.IsDefined(descriptor.DataLocation))
        {
            throw new ArgumentException("An embedding generator needs an ID, label, description, data location, and model identity.", nameof(descriptor));
        }

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
        if (serviceKey is null && serviceType == typeof(ChatSearchDescriptor))
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
