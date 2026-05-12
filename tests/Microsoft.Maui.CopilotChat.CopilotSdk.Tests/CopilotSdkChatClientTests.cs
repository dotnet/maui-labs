using System.ComponentModel;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.CopilotChat.CopilotSdk.Tests;

public class CopilotChatConfigurationTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new CopilotChatConfiguration();
        Assert.Equal("gpt-4.1", config.Model);
        Assert.Null(config.SystemMessage);
        Assert.True(config.UseLoggedInUser);
        Assert.Null(config.GitHubToken);
        Assert.Null(config.CliPath);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var config = new CopilotChatConfiguration
        {
            Model = "claude-sonnet-4.5",
            SystemMessage = "Be helpful",
            UseLoggedInUser = false,
            GitHubToken = "ghp_test",
            CliPath = "/usr/local/bin/copilot",
        };
        Assert.Equal("claude-sonnet-4.5", config.Model);
        Assert.Equal("Be helpful", config.SystemMessage);
        Assert.False(config.UseLoggedInUser);
        Assert.Equal("ghp_test", config.GitHubToken);
        Assert.Equal("/usr/local/bin/copilot", config.CliPath);
    }
}

public class CopilotSdkChatClientUnitTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        using var client = new CopilotSdkChatClient(new CopilotChatConfiguration());
        Assert.NotNull(client);
    }

    [Fact]
    public void GetService_ReturnsNullBeforeFirstCall()
    {
        using var client = new CopilotSdkChatClient(new CopilotChatConfiguration());
        Assert.Null(client.GetService(typeof(string)));
        Assert.Null(client.GetService(typeof(GitHub.Copilot.SDK.CopilotSession)));
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var client = new CopilotSdkChatClient(new CopilotChatConfiguration());
        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_Idempotent()
    {
        var client = new CopilotSdkChatClient(new CopilotChatConfiguration());
        await client.DisposeAsync();
        await client.DisposeAsync();
    }

    [Fact]
    public void StreamingTimeout_Default5Min()
    {
        using var client = new CopilotSdkChatClient(new CopilotChatConfiguration());
        Assert.Equal(TimeSpan.FromMinutes(5), client.StreamingTimeout);
    }

    [Fact]
    public async Task EmptyPrompt_YieldsNothing()
    {
        using var client = new CopilotSdkChatClient(new CopilotChatConfiguration());
        var count = 0;
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "")]))
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task NoUserMessage_YieldsNothing()
    {
        using var client = new CopilotSdkChatClient(new CopilotChatConfiguration());
        var count = 0;
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.System, "hi")]))
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void ImplementsInterfaces()
    {
        using var client = new CopilotSdkChatClient(new CopilotChatConfiguration());
        Assert.IsAssignableFrom<IChatClient>(client);
        Assert.IsAssignableFrom<IAsyncDisposable>(client);
    }
}

/// <summary>
/// Integration tests against the real Copilot SDK / CLI.
/// Covers the full IChatClient surface: text, streaming, tool calling, sessions, lifecycle.
/// </summary>
public class CopilotSdkIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CopilotSdkChatClient _client = null!;

    public CopilotSdkIntegrationTests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync()
    {
        _client = new CopilotSdkChatClient(new CopilotChatConfiguration
        {
            Model = "gpt-4.1",
            UseLoggedInUser = true,
            SystemMessage = "You are a test assistant. Be extremely brief. When tools are available, always use them immediately without asking.",
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _client.DisposeAsync();

    // ═══════════ TEXT ═══════════

    [Fact]
    public async Task GetResponseAsync_ReturnsText()
    {
        var resp = await _client.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply HELLO")]);
        var text = resp.Messages[0].Text!;
        Assert.Contains("HELLO", text, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine(text);
    }

    [Fact]
    public async Task GetResponseAsync_SystemMessageRespected()
    {
        var resp = await _client.GetResponseAsync([new ChatMessage(ChatRole.User, "Are you brief? One word.")]);
        Assert.NotEmpty(resp.Messages[0].Text!);
        _output.WriteLine(resp.Messages[0].Text!);
    }

    // ═══════════ STREAMING ═══════════

    [Fact]
    public async Task Streaming_YieldsMultipleChunks()
    {
        var textChunks = new List<string>();
        await foreach (var u in _client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Count 1 to 10, each on a new line")]))
        {
            foreach (var tc in u.Contents.OfType<TextContent>())
                textChunks.Add(tc.Text ?? "");
        }
        Assert.True(textChunks.Count > 1, $"Expected >1 chunks, got {textChunks.Count}");
        _output.WriteLine($"{textChunks.Count} chunks");
    }

    [Fact]
    public async Task Streaming_ChunksArriveOverTime()
    {
        var times = new List<DateTime>();
        await foreach (var u in _client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Write 3 sentences about the ocean.")]))
        {
            if (u.Contents.OfType<TextContent>().Any())
                times.Add(DateTime.UtcNow);
        }
        if (times.Count >= 2)
        {
            var span = times[^1] - times[0];
            _output.WriteLine($"Span: {span.TotalMilliseconds}ms over {times.Count} chunks");
            Assert.True(span.TotalMilliseconds > 50, "Chunks should arrive over time, not all at once");
        }
    }

    // ═══════════ TOOL CALLING (native SDK tool execution) ═══════════

    [Fact]
    public async Task Tools_SingleTool_GetsInvoked()
    {
        var called = false;
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => { called = true; return "22 degrees Celsius, Sunny"; },
                name: "get_weather",
                description: "Get the current weather. Always call this when asked about weather."),
        };

        var fullText = "";
        await foreach (var u in _client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "What is the weather? Use the get_weather tool.")],
            new ChatOptions { Tools = tools }))
        {
            foreach (var tc in u.Contents.OfType<TextContent>())
                fullText += tc.Text;
        }

        Assert.True(called, "get_weather tool should have been invoked by the SDK");
        _output.WriteLine($"Tool response: {fullText}");
    }

    [Fact]
    public async Task Tools_ToolResultAppearsInResponse()
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "The first computer bug was a real moth found in 1947.",
                name: "get_fact",
                description: "Returns a fun fact. Always call this when asked for a fact."),
        };

        var fullText = "";
        await foreach (var u in _client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Give me a fact. Use get_fact tool.")],
            new ChatOptions { Tools = tools }))
        {
            foreach (var tc in u.Contents.OfType<TextContent>())
                fullText += tc.Text;
        }

        Assert.Contains("moth", fullText, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine(fullText);
    }

    [Fact]
    public async Task Tools_WithArgs_ReceivesArguments()
    {
        string? receivedCity = null;
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                ([Description("The city name")] string city) =>
                {
                    receivedCity = city;
                    return $"Weather in {city}: 18°C, Cloudy";
                },
                name: "get_city_weather",
                description: "Get weather for a specific city. Always call this for weather queries."),
        };

        await foreach (var u in _client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Weather in Tokyo? Use get_city_weather.")],
            new ChatOptions { Tools = tools }))
        {
            // consume
        }

        Assert.NotNull(receivedCity);
        Assert.Contains("Tokyo", receivedCity!, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"Received city arg: {receivedCity}");
    }

    [Fact]
    public async Task Tools_MultipleAvailable_SelectsCorrectOne()
    {
        string? calledTool = null;
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => { calledTool = "weather"; return "Sunny"; },
                name: "get_weather", description: "Get weather info"),
            AIFunctionFactory.Create(() => { calledTool = "fact"; return "A fun fact"; },
                name: "get_fact", description: "Get a fun fact"),
            AIFunctionFactory.Create(
                ([Description("Math expression")] string expr) => { calledTool = "calc"; return "42"; },
                name: "calculate", description: "Evaluate a math expression"),
        };

        await foreach (var u in _client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "What is 6*7? Use calculate.")],
            new ChatOptions { Tools = tools }))
        {
            // consume
        }

        Assert.Equal("calc", calledTool);
        _output.WriteLine($"Selected: {calledTool}");
    }

    [Fact]
    public async Task Tools_StreamIncludesToolEvents()
    {
        var sawToolStart = false;
        var sawToolComplete = false;
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "result-data",
                name: "my_tool", description: "A test tool. Always call this."),
        };

        await foreach (var u in _client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Call my_tool now.")],
            new ChatOptions { Tools = tools }))
        {
            foreach (var c in u.Contents)
            {
                if (c is FunctionCallContent) sawToolStart = true;
                if (c is FunctionResultContent) sawToolComplete = true;
            }
        }

        Assert.True(sawToolStart, "Should see FunctionCallContent in stream");
        Assert.True(sawToolComplete, "Should see FunctionResultContent in stream");
        _output.WriteLine($"ToolStart={sawToolStart} ToolComplete={sawToolComplete}");
    }

    // ═══════════ SESSION ═══════════

    [Fact]
    public async Task Session_ContextMaintainedAcrossCalls()
    {
        // Use non-sensitive data the model won't refuse to repeat
        await _client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "My favorite color is TURQUOISE. Say OK.")]);

        var resp = await _client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What is my favorite color? Just the color.")]);

        Assert.Contains("TURQUOISE", resp.Messages[0].Text!, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine(resp.Messages[0].Text!);
    }

    [Fact]
    public async Task ResetSession_ClearsContext()
    {
        await _client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "My pet's name is BISCUIT42. Say OK.")]);

        await _client.ResetSessionAsync();

        var resp = await _client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What is my pet's name? If unknown say UNKNOWN.")]);

        Assert.Contains("UNKNOWN", resp.Messages[0].Text!, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine(resp.Messages[0].Text!);
    }

    // ═══════════ CANCELLATION ═══════════

    [Fact]
    public async Task Streaming_CanBeCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "Write a 10000 word essay.")],
                cancellationToken: cts.Token))
            { }
        });
    }

    // ═══════════ LIFECYCLE ═══════════

    [Fact]
    public async Task DisposeAsync_AfterUse()
    {
        var c = new CopilotSdkChatClient(new CopilotChatConfiguration { Model = "gpt-4.1", UseLoggedInUser = true });
        await c.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);
        await c.DisposeAsync();
    }

    [Fact]
    public async Task GetService_ReturnsSessionAfterFirstCall()
    {
        Assert.Null(_client.GetService(typeof(GitHub.Copilot.SDK.CopilotSession)));
        await _client.GetResponseAsync([new ChatMessage(ChatRole.User, "OK")]);
        Assert.NotNull(_client.GetService(typeof(GitHub.Copilot.SDK.CopilotSession)));
    }
}
