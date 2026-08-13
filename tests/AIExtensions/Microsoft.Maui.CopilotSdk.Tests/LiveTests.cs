using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

/// <summary>
/// Live, opt-in tests that exercise a real GitHub Copilot runtime. They are skipped (visibly, with a
/// reason) unless <c>COPILOT_SDK_LIVE_TESTS=1</c> is set. These make no assertions about exact model
/// output beyond stable, easily satisfied expectations.
/// </summary>
[Trait("Category", "Live")]
public class LiveTests
{
    [LiveFact]
    public async Task Live_produces_real_text_and_a_conversation_id()
    {
        await using var client = new CopilotSdkChatClient(LiveTestSupport.CreateConfiguration());

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply with exactly the single word: pong")]);

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.Contains("pong", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(response.ConversationId));
    }

    [LiveFact]
    public async Task Live_streams_multiple_chunks_and_a_stop()
    {
        await using var client = new CopilotSdkChatClient(LiveTestSupport.CreateConfiguration());

        var updates = await client
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Count from 1 to 8, one number per line.")])
            .CollectAsync();

        Assert.NotEmpty(updates);
        Assert.All(updates, u => Assert.False(string.IsNullOrEmpty(u.ConversationId)));
        Assert.Contains(updates, u => u.FinishReason == ChatFinishReason.Stop);
    }

    [LiveFact]
    public async Task Live_second_turn_uses_conversation_id_for_memory()
    {
        await using var client = new CopilotSdkChatClient(LiveTestSupport.CreateConfiguration());

        var first = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "My name is Alonzo. Remember it and acknowledge briefly.")]);
        Assert.False(string.IsNullOrEmpty(first.ConversationId));

        var second = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What is my name? Answer with just the name.")],
            new ChatOptions { ConversationId = first.ConversationId });

        Assert.Contains("Alonzo", second.Text, StringComparison.OrdinalIgnoreCase);
    }

    [LiveFact]
    public async Task Live_tool_bridge_invokes_a_dotnet_tool_through_function_invocation()
    {
        var invoked = false;

        [Description("Gets the current price of a product by its id.")]
        string GetPrice([Description("The product id")] string productId)
        {
            invoked = true;
            return $"The price of {productId} is 42 dollars.";
        }

        await using var copilot = new CopilotSdkChatClient(LiveTestSupport.CreateConfiguration());
        using IChatClient pipeline = new ChatClientBuilder(copilot).UseFunctionInvocation().Build();

        var tool = AIFunctionFactory.Create(GetPrice);
        var response = await pipeline.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the get_price tool for product 'sku-1' and tell me the price.")],
            new ChatOptions { Tools = [tool] });

        Assert.True(invoked, "The .NET tool should have been invoked through the external tool bridge.");
        Assert.Contains("42", response.Text);
    }

    [LiveFact]
    public async Task Live_manual_tool_bridge_resumes_after_submitting_result()
    {
        await using var client = new CopilotSdkChatClient(
            LiveTestSupport.CreateConfiguration());
        var tool = AIFunctionFactory.Create(
            (string productId) => $"The price of {productId} is 42 dollars.",
            "get_price",
            "Gets the current price of a product by its id.");
        var first = await client
            .GetStreamingResponseAsync(
                [new ChatMessage(
                    ChatRole.User,
                    "Use the get_price tool for product 'sku-1' and tell me the price.")],
                new ChatOptions { Tools = [tool] })
            .CollectAsync();
        var call = Assert.Single(
            first.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        var conversationId = Assert.Single(
            first.Select(update => update.ConversationId).Distinct());

        var second = await client
            .GetStreamingResponseAsync(
                [new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(
                        call.CallId,
                        "The price of sku-1 is 42 dollars.")])],
                new ChatOptions
                {
                    ConversationId = conversationId,
                    Tools = [tool],
                })
            .CollectAsync();

        Assert.Contains(
            "42",
            string.Concat(second.SelectMany(update =>
                update.Contents.OfType<TextContent>().Select(content => content.Text))));
    }

    [LiveFact]
    public async Task Live_cancellation_stops_the_request()
    {
        await using var client = new CopilotSdkChatClient(LiveTestSupport.CreateConfiguration());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "Write a very long, detailed 2000-word essay about the ocean.")],
                cancellationToken: cts.Token))
            {
                // keep consuming until cancellation trips
            }
        });
    }
}
