using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class ToolBridgeTests
{
    private static AIFunction Weather() =>
        AIFunctionFactory.Create(
            (string city) => $"Sunny in {city}",
            "get_weather",
            "Gets the weather");

    [Fact]
    public async Task Tools_are_advertised_as_pending_proxies_with_a_custom_allowlist()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(TextSession());

        await using var client = TestChatClient.Create(backend);
        await client.GetResponseAsync(
            TestExtensions.UserMessage("hello"),
            new ChatOptions { Tools = [Weather()] });

        var parameters = backend.Calls[0].Parameters;
        var proxy = Assert.IsType<PendingToolAIFunction>(
            Assert.Single(parameters.ToolDeclarations));
        Assert.Equal("get_weather", proxy.Name);
        Assert.Equal(["custom:get_weather"], parameters.AvailableTools);
        Assert.Empty(parameters.ExcludedTools);
    }

    [Fact]
    public async Task No_tools_excludes_all_built_in_tools()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(TextSession());

        await using var client = TestChatClient.Create(backend);
        await client.GetResponseAsync(TestExtensions.UserMessage("hello"));

        var parameters = backend.Calls[0].Parameters;
        Assert.Empty(parameters.ToolDeclarations);
        Assert.Empty(parameters.AvailableTools);
        Assert.Equal(["builtin:*"], parameters.ExcludedTools);
    }

    [Fact]
    public async Task External_tool_request_surfaces_function_call_and_keeps_session_alive()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(new FakeCopilotSession("conv-1")
        {
            OnSend = (current, _) =>
            {
                current.Emit(SdkEvents.ToolRequested(
                    "request-1",
                    "call-1",
                    "get_weather",
                    new { city = "Paris" }));
                return Task.CompletedTask;
            },
        });
        await using var client = TestChatClient.Create(backend);

        var updates = await client
            .GetStreamingResponseAsync(
                TestExtensions.UserMessage("weather?"),
                new ChatOptions { Tools = [Weather()] })
            .CollectAsync();

        var terminal = updates[^1];
        Assert.Equal(ChatFinishReason.ToolCalls, terminal.FinishReason);
        var call = Assert.IsType<FunctionCallContent>(
            Assert.Single(terminal.Contents));
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal(
            "request-1",
            call.AdditionalProperties!["copilot.request_id"]);
        Assert.Equal(
            "Paris",
            ((JsonElement)call.Arguments!["city"]!).GetString());
        Assert.Equal(0, session.AbortCount);
        Assert.Equal(0, session.DisposeCount);
    }

    [Fact]
    public async Task Breaking_after_tool_boundary_preserves_pending_session_for_continuation()
    {
        var (backend, session) = CreateSingleToolBackend();
        await using var client = TestChatClient.Create(backend);
        var options = new ChatOptions { Tools = [Weather()] };
        FunctionCallContent? call = null;

        await foreach (var update in client.GetStreamingResponseAsync(
            TestExtensions.UserMessage("weather?"),
            options))
        {
            call = update.Contents.OfType<FunctionCallContent>().SingleOrDefault();
            if (update.FinishReason == ChatFinishReason.ToolCalls)
                break;
        }

        Assert.NotNull(call);
        Assert.Equal(0, session.AbortCount);
        Assert.Equal(0, session.DisposeCount);

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, "Sunny")]),
                new ChatMessage(
                    ChatRole.User,
                    "The parallel approval was accepted."),
            ],
            new ChatOptions
            {
                ConversationId = "conv-1",
                Tools = [Weather()],
            });

        Assert.Equal("It is Sunny", response.Text);
    }

    [Fact]
    public async Task Tool_mode_none_does_not_advertise_caller_tools()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(TextSession());
        await using var client = TestChatClient.Create(backend);

        await client.GetResponseAsync(
            TestExtensions.UserMessage("hello"),
            new ChatOptions
            {
                Tools = [Weather()],
                ToolMode = ChatToolMode.None,
            });

        var parameters = backend.Calls[0].Parameters;
        Assert.Empty(parameters.ToolDeclarations);
        Assert.Empty(parameters.AvailableTools);
        Assert.Equal(["builtin:*"], parameters.ExcludedTools);
    }

    [Fact]
    public async Task Required_tool_mode_fails_explicitly()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(TextSession());
        await using var client = TestChatClient.Create(backend);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.GetResponseAsync(
                TestExtensions.UserMessage("hello"),
                new ChatOptions
                {
                    Tools = [Weather()],
                    ToolMode = ChatToolMode.RequireAny,
                }));

        Assert.Contains("required-tool", exception.Message);
        Assert.Empty(backend.Calls);
    }

    [Fact]
    public async Task Parallel_tool_requests_are_surfaced_and_resolved_by_call_id()
    {
        var backend = new FakeCopilotBackend();
        var completionEmitted = false;
        var session = backend.AddSession(new FakeCopilotSession("conv-1")
        {
            OnSend = (current, _) =>
            {
                current.EmitAll(
                    SdkEvents.ToolRequested(
                        "request-1",
                        "call-1",
                        "get_weather",
                        new { city = "Paris" }),
                    SdkEvents.ToolRequested(
                        "request-2",
                        "call-2",
                        "get_weather",
                        new { city = "Rome" }));
                return Task.CompletedTask;
            },
            OnHandleToolCall = (current, _, _, _) =>
            {
                if (current.ToolCallResults.Count == 2 && !completionEmitted)
                {
                    completionEmitted = true;
                    current.EmitAll(
                        SdkEvents.Delta("both done", "message-2"),
                        SdkEvents.Idle());
                }
                return Task.CompletedTask;
            },
        });
        await using var client = TestChatClient.Create(backend);
        var options = new ChatOptions { Tools = [Weather()] };
        var first = await client
            .GetStreamingResponseAsync(
                TestExtensions.UserMessage("weather?"),
                options)
            .CollectAsync();
        var calls = first
            .SelectMany(update => update.Contents)
            .OfType<FunctionCallContent>()
            .ToArray();

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(
                    ChatRole.Tool,
                    [
                        new FunctionResultContent("call-2", "Rome-result"),
                        new FunctionResultContent("call-1", "Paris-result"),
                    ]),
            ],
            new ChatOptions
            {
                ConversationId = "conv-1",
                Tools = [Weather()],
            });

        Assert.Equal(2, calls.Length);
        Assert.Equal("both done", response.Text);
        var results = session.ToolCallResults.ToDictionary(
            result => result.RequestId,
            result => result.Result?.ToString());
        Assert.Equal("Rome-result", results["request-2"]);
        Assert.Equal("Paris-result", results["request-1"]);
    }

    [Fact]
    public async Task Tool_result_continuation_completes_proxy_and_streams_answer()
    {
        var (backend, session) = CreateSingleToolBackend();
        await using var client = TestChatClient.Create(backend);
        var options = new ChatOptions { Tools = [Weather()] };
        var first = await client
            .GetStreamingResponseAsync(
                TestExtensions.UserMessage("weather?"),
                options)
            .CollectAsync();
        var call = Assert.Single(
            first.SelectMany(update => update.Contents)
                .OfType<FunctionCallContent>());

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, "Sunny")]),
            ],
            new ChatOptions
            {
                ConversationId = "conv-1",
                Tools = [Weather()],
            });

        Assert.Equal("It is Sunny", response.Text);
        Assert.Equal("Sunny", Assert.Single(session.ToolCallResults).Result);
        Assert.Single(backend.Calls);
    }

    [Fact]
    public async Task Unknown_tool_result_call_id_fails_explicitly()
    {
        var (backend, _) = CreateSingleToolBackend();
        await using var client = TestChatClient.Create(backend);
        await client.GetStreamingResponseAsync(
                TestExtensions.UserMessage("weather?"),
                new ChatOptions { Tools = [Weather()] })
            .CollectAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync(
                [
                    new ChatMessage(
                        ChatRole.Tool,
                        [new FunctionResultContent("unknown", "result")]),
                ],
                new ChatOptions
                {
                    ConversationId = "conv-1",
                    Tools = [Weather()],
                }));

        Assert.Contains("unknown", exception.Message);
    }

    [Fact]
    public async Task Tool_exception_is_returned_to_the_sdk_proxy()
    {
        var (backend, session) = CreateSingleToolBackend();
        await using var client = TestChatClient.Create(backend);
        var first = await client.GetStreamingResponseAsync(
                TestExtensions.UserMessage("weather?"),
                new ChatOptions { Tools = [Weather()] })
            .CollectAsync();
        var call = Assert.Single(
            first.SelectMany(update => update.Contents)
                .OfType<FunctionCallContent>());
        var result = new FunctionResultContent(call.CallId, null)
        {
            Exception = new InvalidOperationException("boom"),
        };

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.Tool, [result])],
            new ChatOptions
            {
                ConversationId = "conv-1",
                Tools = [Weather()],
            });

        var submitted = Assert.Single(session.ToolCallResults);
        Assert.Null(submitted.Result);
        Assert.Equal("boom", submitted.Error);
    }

    [Fact]
    public async Task Multimodal_tool_result_reaches_the_sdk_proxy_unchanged()
    {
        var (backend, session) = CreateSingleToolBackend();
        await using var client = TestChatClient.Create(backend);
        var first = await client.GetStreamingResponseAsync(
                TestExtensions.UserMessage("weather?"),
                new ChatOptions { Tools = [Weather()] })
            .CollectAsync();
        var call = Assert.Single(
            first.SelectMany(update => update.Contents)
                .OfType<FunctionCallContent>());
        AIContent[] content =
        [
            new TextContent("caption"),
            new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
        ];

        await client.GetResponseAsync(
            [
                new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, content)]),
            ],
            new ChatOptions
            {
                ConversationId = "conv-1",
                Tools = [Weather()],
            });

        Assert.Same(content, Assert.Single(session.ToolCallResults).Result);
    }

    [Fact]
    public async Task Missing_process_local_tool_session_fails_explicitly()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(TextSession("conv-1"));
        await using var client = TestChatClient.Create(backend);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync(
                [
                    new ChatMessage(
                        ChatRole.Tool,
                        [new FunctionResultContent("call-1", "result")]),
                ],
                new ChatOptions
                {
                    ConversationId = "conv-1",
                    Tools = [Weather()],
                }));

        Assert.Contains("same client instance", exception.Message);
    }

    [Fact]
    public async Task Full_tool_loop_composes_with_function_invoking_chat_client()
    {
        var (backend, _) = CreateSingleToolBackend();
        await using var copilot = TestChatClient.Create(backend);
        using IChatClient pipeline = new ChatClientBuilder(copilot)
            .UseFunctionInvocation()
            .Build();

        var response = await pipeline.GetResponseAsync(
            TestExtensions.UserMessage("What is the weather in Paris?"),
            new ChatOptions { Tools = [Weather()] });

        Assert.Contains("Sunny in Paris", response.Text);
        Assert.Single(backend.Calls);
    }

    private static (FakeCopilotBackend Backend, FakeCopilotSession Session)
        CreateSingleToolBackend()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(new FakeCopilotSession("conv-1")
        {
            OnSend = (current, _) =>
            {
                current.Emit(SdkEvents.ToolRequested(
                    "request-1",
                    "call-1",
                    "get_weather",
                    new { city = "Paris" }));
                return Task.CompletedTask;
            },
            OnHandleToolCall = (current, _, result, _) =>
            {
                current.EmitAll(
                    SdkEvents.Delta($"It is {result}", "message-2"),
                    SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });
        return (backend, session);
    }

    private static FakeCopilotSession TextSession(
        string sessionId = "session-1") =>
        new(sessionId)
        {
            OnSend = (session, _) =>
            {
                session.EmitAll(
                    SdkEvents.Delta("hello", "message-1"),
                    SdkEvents.Idle());
                return Task.CompletedTask;
            },
        };
}
