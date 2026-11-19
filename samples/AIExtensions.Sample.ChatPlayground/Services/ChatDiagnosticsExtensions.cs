using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

internal static class ChatDiagnosticsExtensions
{
    public static ChatClientBuilder UsePlaygroundTelemetry(this ChatClientBuilder builder) =>
        builder.UseOpenTelemetry(sourceName: ChatDiagnostics.SourceName, configure: client => client.EnableSensitiveData = false);

    public static EmbeddingGeneratorBuilder<TInput, TEmbedding> UsePlaygroundTelemetry<TInput, TEmbedding>(
        this EmbeddingGeneratorBuilder<TInput, TEmbedding> builder) where TEmbedding : Embedding =>
        builder.UseOpenTelemetry(sourceName: ChatDiagnostics.SourceName, configure: generator => generator.EnableSensitiveData = false);
}
