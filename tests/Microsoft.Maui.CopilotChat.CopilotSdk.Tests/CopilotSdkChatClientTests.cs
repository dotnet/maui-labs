using Microsoft.Extensions.AI;
using Xunit;

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
        };

        Assert.Equal("claude-sonnet-4.5", config.Model);
        Assert.Equal("Be helpful", config.SystemMessage);
        Assert.False(config.UseLoggedInUser);
        Assert.Equal("ghp_test", config.GitHubToken);
    }
}

public class CopilotSdkChatClientUnitTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var config = new CopilotChatConfiguration();
        var client = new CopilotSdkChatClient(config);

        Assert.NotNull(client);
        client.Dispose();
    }

    [Fact]
    public void GetService_ReturnsNullForUnknownType()
    {
        var config = new CopilotChatConfiguration();
        using var client = new CopilotSdkChatClient(config);

        Assert.Null(client.GetService(typeof(string)));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var config = new CopilotChatConfiguration();
        var client = new CopilotSdkChatClient(config);

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var config = new CopilotChatConfiguration();
        var client = new CopilotSdkChatClient(config);

        await client.DisposeAsync();
        await client.DisposeAsync();
    }

    [Fact]
    public void StreamingTimeout_DefaultIs5Minutes()
    {
        var config = new CopilotChatConfiguration();
        using var client = new CopilotSdkChatClient(config);

        Assert.Equal(TimeSpan.FromMinutes(5), client.StreamingTimeout);
    }

    [Fact]
    public void StreamingTimeout_CanBeChanged()
    {
        var config = new CopilotChatConfiguration();
        using var client = new CopilotSdkChatClient(config);

        client.StreamingTimeout = TimeSpan.FromSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(30), client.StreamingTimeout);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_EmptyPrompt_YieldsNothing()
    {
        var config = new CopilotChatConfiguration();
        using var client = new CopilotSdkChatClient(config);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "")
        };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages))
            updates.Add(update);

        Assert.Empty(updates);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NoUserMessage_YieldsNothing()
    {
        var config = new CopilotChatConfiguration();
        using var client = new CopilotSdkChatClient(config);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are helpful")
        };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages))
            updates.Add(update);

        Assert.Empty(updates);
    }

    [Fact]
    public void ImplementsIChatClient()
    {
        var config = new CopilotChatConfiguration();
        using var client = new CopilotSdkChatClient(config);

        Assert.IsAssignableFrom<IChatClient>(client);
        Assert.IsAssignableFrom<IAsyncDisposable>(client);
    }
}

/// <summary>
/// Integration tests that require a real Copilot CLI and authentication.
/// Skipped if Copilot CLI is not available.
/// </summary>
public class CopilotSdkChatClientIntegrationTests
{
    private static bool IsCopilotAvailable()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "copilot",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static CopilotSdkChatClient CreateClient(string? systemMessage = null)
    {
        return new CopilotSdkChatClient(new CopilotChatConfiguration
        {
            Model = "gpt-4.1",
            UseLoggedInUser = true,
            SystemMessage = systemMessage,
        });
    }

    [Fact]
    public async Task GetResponseAsync_BasicPrompt_ReturnsNonEmptyResponse()
    {
        if (!IsCopilotAvailable())
            return; // Skip silently when CLI not available

        await using var client = CreateClient("You are a helpful assistant. Be extremely brief.");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Reply with exactly: hello world")
        };

        var response = await client.GetResponseAsync(messages);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Messages);

        var text = response.Messages[0].Text;
        Assert.NotNull(text);
        Assert.NotEmpty(text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_BasicPrompt_YieldsTextChunks()
    {
        if (!IsCopilotAvailable())
            return;

        await using var client = CreateClient("Be extremely brief. One sentence max.");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is 2+2? Reply with just the number.")
        };

        var chunks = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages))
            chunks.Add(update);

        Assert.NotEmpty(chunks);

        var textChunks = chunks
            .SelectMany(c => c.Contents.OfType<TextContent>())
            .ToList();
        Assert.NotEmpty(textChunks);

        var fullText = string.Join("", textChunks.Select(tc => tc.Text));
        Assert.Contains("4", fullText);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_CanBeCancelled()
    {
        if (!IsCopilotAvailable())
            return;

        await using var client = CreateClient();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Write a very long essay about the history of computing. Make it at least 10000 words.")
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var chunks = new List<ChatResponseUpdate>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync(messages, cancellationToken: cts.Token))
                chunks.Add(update);
        });

        // Should have received some chunks before cancellation
        // (but not guaranteed if the SDK is slow to start)
    }

    [Fact]
    public async Task GetResponseAsync_WithSystemMessage_RespectsInstructions()
    {
        if (!IsCopilotAvailable())
            return;

        await using var client = CreateClient("You must always respond with exactly: PONG");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "ping")
        };

        var response = await client.GetResponseAsync(messages);
        var text = response.Messages[0].Text ?? "";

        Assert.Contains("PONG", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetResponseAsync_MultipleSequentialCalls_MaintainSession()
    {
        if (!IsCopilotAvailable())
            return;

        await using var client = CreateClient("Be extremely brief. One word answers only.");

        // First message
        var response1 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "My name is TestBot42. Remember it. Just say OK.")
        ]);
        Assert.NotNull(response1.Messages[0].Text);

        // Second message — should remember context from session
        var response2 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "What name did I just tell you? Reply with just the name.")
        ]);
        var text2 = response2.Messages[0].Text ?? "";
        Assert.Contains("TestBot42", text2, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetSessionAsync_ClearsSessionState()
    {
        if (!IsCopilotAvailable())
            return;

        await using var client = CreateClient("Be extremely brief.");

        // Establish context
        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Remember the code word: ZEBRA42. Say OK.")
        ]);

        // Reset
        await client.ResetSessionAsync();

        // After reset, session should not remember
        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "What was the code word I told you? If you don't know, say UNKNOWN.")
        ]);
        var text = response.Messages[0].Text ?? "";

        // After reset, it should NOT know the code word
        Assert.Contains("UNKNOWN", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisposeAsync_AfterUse_CleansUpResources()
    {
        if (!IsCopilotAvailable())
            return;

        var client = CreateClient("Be brief.");

        // Use the client
        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Say hello")
        ]);

        // Dispose should not throw
        await client.DisposeAsync();
    }
}
