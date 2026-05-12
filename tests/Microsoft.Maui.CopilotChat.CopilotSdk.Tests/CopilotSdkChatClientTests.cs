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

public class CopilotSdkChatClientTests
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
        client.Dispose(); // Should not throw
    }
}
