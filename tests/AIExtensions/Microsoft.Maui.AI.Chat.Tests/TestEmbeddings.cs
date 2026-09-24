using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

internal sealed class TestEmbeddings(
    Func<string, float[]> embed, Func<CancellationToken, Task>? beforeGenerate = null)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<string> Inputs { get; } = [];

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (beforeGenerate is not null)
            await beforeGenerate(cancellationToken);

        var result = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var text in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Add(text);
            result.Add(new Embedding<float>(embed(text)));
        }
        return result;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
