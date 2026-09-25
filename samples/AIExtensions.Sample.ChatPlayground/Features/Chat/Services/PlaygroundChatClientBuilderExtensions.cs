using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Chat.Services;

public static class PlaygroundChatClientBuilderExtensions
{
    public static ChatClientBuilder UseDescriptor(this ChatClientBuilder builder, ChatClientDescriptor descriptor) =>
        builder.Use(inner => new DescribedChatClient(inner, descriptor));

    public static ChatClientBuilder UseImageDescriptions(
        this ChatClientBuilder builder,
        Func<DataContent, CancellationToken, Task<string>> describeImage) =>
        builder.Use(inner => new ImageDescribingChatClient(inner, describeImage));

#pragma warning disable MEAI001 // Image generation middleware is experimental in the installed SDK.
    public static ChatClientBuilder UseImageGenerationPreservingInputs(this ChatClientBuilder builder, IImageGenerator generator) =>
        builder.Use(inner => new ImageGeneratingChatClient(
            inner, generator, ImageGeneratingChatClient.DataContentHandling.GeneratedImages));
#pragma warning restore MEAI001
}
