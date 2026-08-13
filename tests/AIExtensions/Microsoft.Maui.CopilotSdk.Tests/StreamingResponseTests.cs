using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class StreamingResponseTests
{
    [Fact]
    public async Task Streaming_maps_deltas_reasoning_and_usage_to_updates()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession("session-abc")
        {
            OnSend = (s, _) =>
            {
                s.EmitAll(
                    SdkEvents.ReasoningDelta("thinking..."),
                    SdkEvents.Delta("Hello", "m1"),
                    SdkEvents.Delta(" world", "m1"),
                    SdkEvents.Usage(input: 12, output: 8),
                    SdkEvents.FinalMessage("Hello world", "m1", "gpt-5"),
                    SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend);
        var updates = await client.GetStreamingResponseAsync(TestExtensions.UserMessage("hi")).CollectAsync();

        // Reasoning surfaces as reasoning content, text as text content, and they are not mixed.
        Assert.Contains(updates, u => u.Contents is [TextReasoningContent { Text: "thinking..." }]);
        Assert.Contains(updates, u => u.Contents is [TextContent { Text: "Hello" }]);
        Assert.Contains(updates, u => u.Contents is [TextContent { Text: " world" }]);

        // The final complete message is not re-emitted as duplicate text (its id was streamed).
        Assert.DoesNotContain(updates, u => u.Contents is [TextContent { Text: "Hello world" }]);

        // Usage is surfaced.
        var usage = Assert.Single(updates, u => u.Contents is [UsageContent]);
        var usageContent = Assert.IsType<UsageContent>(usage.Contents[0]);
        Assert.Equal(12, usageContent.Details.InputTokenCount);
        Assert.Equal(8, usageContent.Details.OutputTokenCount);
        Assert.Equal(20, usageContent.Details.TotalTokenCount);

        // Terminal update carries the stop finish reason.
        Assert.Equal(ChatFinishReason.Stop, updates[^1].FinishReason);
    }

    [Fact]
    public async Task Streaming_sets_conversation_id_and_raw_representation_on_every_update()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession("session-xyz")
        {
            OnSend = (s, _) =>
            {
                s.EmitAll(SdkEvents.Delta("Hi", "m1"), SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend);
        var updates = await client.GetStreamingResponseAsync(TestExtensions.UserMessage("hi")).CollectAsync();

        Assert.NotEmpty(updates);
        Assert.All(updates, u => Assert.Equal("session-xyz", u.ConversationId));
        Assert.All(updates, u => Assert.NotNull(u.RawRepresentation));
        Assert.All(updates, u => Assert.IsAssignableFrom<GitHub.Copilot.SessionEvent>(u.RawRepresentation!));
    }

    [Fact]
    public async Task Breaking_after_stop_preserves_completed_session_without_aborting()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(new FakeCopilotSession("session-stop")
        {
            OnSend = (current, _) =>
            {
                current.EmitAll(
                    SdkEvents.Delta("done", "m1"),
                    SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });
        await using var client = TestChatClient.Create(backend);

        await foreach (var update in client.GetStreamingResponseAsync(
            TestExtensions.UserMessage("hi")))
        {
            if (update.FinishReason == ChatFinishReason.Stop)
                break;
        }

        Assert.Equal(0, session.AbortCount);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task Streaming_emits_final_message_text_when_no_deltas_were_streamed()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession()
        {
            OnSend = (s, _) =>
            {
                // Non-streaming style: only the final message, no deltas.
                s.EmitAll(SdkEvents.FinalMessage("Complete answer", "m1"), SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend);
        var updates = await client.GetStreamingResponseAsync(TestExtensions.UserMessage("hi")).CollectAsync();

        Assert.Contains(updates, u => u.Contents is [TextContent { Text: "Complete answer" }]);
    }

    [Fact]
    public async Task GetResponse_aggregates_text_usage_and_metadata()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession("conv-1")
        {
            OnSend = (s, _) =>
            {
                s.EmitAll(
                    SdkEvents.ReasoningDelta("ponder"),
                    SdkEvents.Delta("The ", "m1"),
                    SdkEvents.Delta("answer", "m1"),
                    SdkEvents.Usage(input: 20, output: 4, reasoning: 3),
                    SdkEvents.FinalMessage("The answer", "m1"),
                    SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend);
        var response = await client.GetResponseAsync(
            TestExtensions.UserMessage("q"),
            new ChatOptions { ModelId = "gpt-5" });

        // Text is coalesced; reasoning is NOT folded into the assistant text.
        Assert.Equal("The answer", response.Text);
        Assert.Contains(response.Messages[^1].Contents, c => c is TextReasoningContent { Text: "ponder" });

        Assert.Equal("conv-1", response.ConversationId);
        Assert.Equal("gpt-5", response.ModelId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.NotNull(response.Usage);
        Assert.Equal(20, response.Usage!.InputTokenCount);
        Assert.Equal(4, response.Usage.OutputTokenCount);
        Assert.Equal(3, response.Usage.ReasoningTokenCount);
        Assert.Equal(24, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task Final_reasoning_without_deltas_is_preserved()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession("conv-1")
        {
            OnSend = (session, _) =>
            {
                session.EmitAll(
                    SdkEvents.FinalReasoning("complete thought", "reason-1"),
                    SdkEvents.Delta("answer", "m1"),
                    SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });
        await using var client = TestChatClient.Create(backend);

        var updates = await client
            .GetStreamingResponseAsync(TestExtensions.UserMessage("think"))
            .CollectAsync();

        var reasoning = Assert.Single(
            updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>());
        Assert.Equal("complete thought", reasoning.Text);
    }

    [Fact]
    public async Task Final_reasoning_is_not_duplicated_after_deltas()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession("conv-1")
        {
            OnSend = (session, _) =>
            {
                session.EmitAll(
                    SdkEvents.ReasoningDelta("partial", "reason-1"),
                    SdkEvents.FinalReasoning("partial", "reason-1"),
                    SdkEvents.Delta("answer", "m1"),
                    SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });
        await using var client = TestChatClient.Create(backend);

        var updates = await client
            .GetStreamingResponseAsync(TestExtensions.UserMessage("think"))
            .CollectAsync();

        Assert.Single(
            updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>());
    }

    [Fact]
    public async Task GetResponse_reports_model_from_configuration_when_runtime_is_silent()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession()
        {
            OnSend = (s, _) =>
            {
                s.EmitAll(SdkEvents.Delta("hi", "m1"), SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend, new CopilotSdkConfiguration { Model = "configured-model" });
        var response = await client.GetResponseAsync(TestExtensions.UserMessage("q"));

        Assert.Equal("configured-model", response.ModelId);
    }
}
