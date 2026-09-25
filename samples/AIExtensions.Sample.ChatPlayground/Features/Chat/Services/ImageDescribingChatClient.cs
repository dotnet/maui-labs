using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Chat.Services;

/// <summary>Optionally turns image inputs into captions before sending text to a text-only client.</summary>
internal sealed class ImageDescribingChatClient(
    IChatClient innerClient,
    Func<DataContent, CancellationToken, Task<string>> describeImage)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await base.GetResponseAsync(
            await DescribeMessagesAsync(chatMessages, describeImage, cancellationToken),
            options,
            cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var described = await DescribeMessagesAsync(chatMessages, describeImage, cancellationToken);
        await foreach (var update in base.GetStreamingResponseAsync(described, options, cancellationToken)
                           .WithCancellation(cancellationToken))
            yield return update;
    }

    private static async Task<IReadOnlyList<ChatMessage>> DescribeMessagesAsync(
        IEnumerable<ChatMessage> chatMessages,
        Func<DataContent, CancellationToken, Task<string>> describeImage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatMessages);
        var result = new List<ChatMessage>();
        foreach (var message in chatMessages)
        {
            var contents = new List<AIContent>(message.Contents.Count);
            var changed = false;
            foreach (var content in message.Contents)
            {
                if (content is DataContent data && data.HasTopLevelMediaType("image"))
                {
                    var description = await describeImage(data, cancellationToken);
                    if (string.IsNullOrWhiteSpace(description))
                        throw new InvalidOperationException("The Windows image-description model returned an empty caption.");

                    contents.Add(new TextContent($"[Image: {description}]"));
                    changed = true;
                }
                else
                {
                    contents.Add(content);
                }
            }

            if (changed)
            {
                var copy = message.Clone();
                copy.Contents = contents;
                result.Add(copy);
            }
            else
            {
                result.Add(message);
            }
        }

        return result;
    }
}
