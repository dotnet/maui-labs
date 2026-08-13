using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

internal static class TestExtensions
{
    public static async Task<List<ChatResponseUpdate>> CollectAsync(
        this IAsyncEnumerable<ChatResponseUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        var list = new List<ChatResponseUpdate>();
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            list.Add(update);
        }

        return list;
    }

    public static List<ChatMessage> UserMessage(string text) => [new ChatMessage(ChatRole.User, text)];
}
