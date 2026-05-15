// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Maui.AI.Controls.Tests;

public class AgentSessionHitlTests
{
    [Fact]
    public async Task WaitForResponse_BlocksUntilProvided()
    {
        // We test the HITL mechanism in isolation (no IChatClient needed for just this part)
        // Use the public API surface
        var session = CreateSession();

        var waitTask = session.WaitForResponse("confirm_plan");
        Assert.False(waitTask.IsCompleted);

        session.ProvideResponse("confirm_plan", new PlanConfirmationResult { Confirmed = true });

        var result = await waitTask;
        Assert.IsType<PlanConfirmationResult>(result);
        Assert.True(((PlanConfirmationResult)result).Confirmed);
    }

    [Fact]
    public async Task WaitForResponse_MultipleKeys_IndependentResolution()
    {
        var session = CreateSession();

        var wait1 = session.WaitForResponse("key1");
        var wait2 = session.WaitForResponse("key2");

        session.ProvideResponse("key2", "second");
        session.ProvideResponse("key1", "first");

        Assert.Equal("first", await wait1);
        Assert.Equal("second", await wait2);
    }

    [Fact]
    public void ProvideResponse_WithNoWaiter_IsIgnored()
    {
        var session = CreateSession();
        // Should not throw
        session.ProvideResponse("non_existent", "value");
    }

    [Fact]
    public async Task WaitForResponse_SameKey_ReturnsExistingTcs()
    {
        var session = CreateSession();
        var wait1 = session.WaitForResponse("key");
        var wait2 = session.WaitForResponse("key");

        // Same TaskCompletionSource, so both return same task
        Assert.Same(wait1, wait2);

        session.ProvideResponse("key", "result");
        Assert.Equal("result", await wait1);
        Assert.Equal("result", await wait2);
    }

    private static AgentSession CreateSession()
    {
        return new AgentSession(new FakeChatClient());
    }

    /// <summary>Minimal IChatClient for testing session logic without streaming.</summary>
    private sealed class FakeChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public void Dispose() { }

        public Microsoft.Extensions.AI.ChatClientMetadata Metadata =>
            new("fake");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<Microsoft.Extensions.AI.ChatResponseUpdate>();
    }
}
