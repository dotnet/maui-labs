using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground;

internal static class ChatDiagnosticsExtensions
{
    public static ChatClientBuilder UsePlaygroundDiagnostics(this ChatClientBuilder builder, ILoggerFactory loggerFactory) =>
        builder.UsePlaygroundTelemetry().UseLogging(loggerFactory);

    public static ChatClientBuilder UsePlaygroundTelemetry(this ChatClientBuilder builder) =>
        builder.UseOpenTelemetry(sourceName: ChatDiagnostics.SourceName, configure: client => client.EnableSensitiveData = false);

    public static EmbeddingGeneratorBuilder<TInput, TEmbedding> UsePlaygroundTelemetry<TInput, TEmbedding>(
        this EmbeddingGeneratorBuilder<TInput, TEmbedding> builder) where TEmbedding : Embedding =>
        builder.UseOpenTelemetry(sourceName: ChatDiagnostics.SourceName, configure: generator => generator.EnableSensitiveData = false);
}
