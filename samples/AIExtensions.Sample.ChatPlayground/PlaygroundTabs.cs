using AIExtensions.Sample.ChatPlayground.Features.Chat.Views;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.Views;
using AIExtensions.Sample.ChatPlayground.Features.Images.Views;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class PlaygroundTabs : TabbedPage
{
    public PlaygroundTabs(ChatPage chat, EmbeddingPage embeddings, ImagePage images)
    {
        Children.Add(chat);
        Children.Add(embeddings);
        Children.Add(images);
    }
}
