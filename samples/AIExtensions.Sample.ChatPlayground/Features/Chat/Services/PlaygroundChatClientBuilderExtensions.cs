using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Chat.Services;

public static class PlaygroundChatClientBuilderExtensions
{
    public static ChatClientBuilder UseDescriptor(this ChatClientBuilder builder, ChatClientDescriptor descriptor) =>
        builder.Use(inner => new DescribedChatClient(inner, descriptor));

#pragma warning disable MEAI001 // Image generation middleware is experimental in the installed SDK.
    public static ChatClientBuilder UseImageGenerationPreservingInputs(this ChatClientBuilder builder, IImageGenerator generator) =>
        builder.Use(inner => new ImageGeneratingChatClient(
            inner, generator, ImageGeneratingChatClient.DataContentHandling.GeneratedImages));
#pragma warning restore MEAI001
}
