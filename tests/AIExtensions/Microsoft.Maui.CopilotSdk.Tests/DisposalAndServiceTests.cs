using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class DisposalAndServiceTests
{
    [Fact]
    public void GetService_returns_self_metadata_and_underlying_client()
    {
        var sentinelClient = new object();
        var backend = new FakeCopilotBackend { UnderlyingClient = sentinelClient };
        var client = new CopilotSdkChatClient(new CopilotSdkConfiguration { Model = "gpt-5" }, backend, ownsBackend: true);

        Assert.Same(client, client.GetService(typeof(IChatClient)));
        Assert.Same(client, client.GetService(typeof(CopilotSdkChatClient)));

        var metadata = Assert.IsType<ChatClientMetadata>(client.GetService(typeof(ChatClientMetadata)));
        Assert.Equal("github-copilot", metadata.ProviderName);
        Assert.Equal("gpt-5", metadata.DefaultModelId);

        Assert.Same(sentinelClient, client.GetService(typeof(CopilotClient)));

        Assert.Null(client.GetService(typeof(IChatClient), serviceKey: "keyed"));
        Assert.Null(client.GetService(typeof(string)));
    }

    [Fact]
    public void GetService_throws_for_null_service_type()
    {
        var client = new CopilotSdkChatClient(new CopilotSdkConfiguration(), new FakeCopilotBackend(), ownsBackend: true);
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
    }

    [Fact]
    public void Dispose_is_idempotent_and_disposes_owned_backend()
    {
        var backend = new FakeCopilotBackend();
        var client = new CopilotSdkChatClient(new CopilotSdkConfiguration(), backend, ownsBackend: true);

        client.Dispose();
        client.Dispose();

        Assert.Equal(1, backend.DisposeCount);
        Assert.Equal(0, backend.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent_and_disposes_owned_backend()
    {
        var backend = new FakeCopilotBackend();
        var client = new CopilotSdkChatClient(new CopilotSdkConfiguration(), backend, ownsBackend: true);

        await client.DisposeAsync();
        await client.DisposeAsync();

        Assert.Equal(1, backend.DisposeAsyncCount);
        Assert.Equal(0, backend.DisposeCount);
    }

    [Fact]
    public async Task Mixing_sync_and_async_dispose_only_disposes_once()
    {
        var backend = new FakeCopilotBackend();
        var client = new CopilotSdkChatClient(new CopilotSdkConfiguration(), backend, ownsBackend: true);

        await client.DisposeAsync();
        client.Dispose();

        Assert.Equal(1, backend.DisposeAsyncCount);
        Assert.Equal(0, backend.DisposeCount);
    }

    [Fact]
    public void Dispose_does_not_dispose_backend_it_does_not_own()
    {
        var backend = new FakeCopilotBackend();
        var client = new CopilotSdkChatClient(new CopilotSdkConfiguration(), backend, ownsBackend: false);

        client.Dispose();

        Assert.Equal(0, backend.DisposeCount);
        Assert.Equal(0, backend.DisposeAsyncCount);
    }

    [Fact]
    public async Task Using_a_disposed_client_throws_object_disposed()
    {
        var backend = new FakeCopilotBackend();
        var client = new CopilotSdkChatClient(new CopilotSdkConfiguration(), backend, ownsBackend: true);
        await client.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() =>
            client.GetStreamingResponseAsync(TestExtensions.UserMessage("hi")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListModelsAsync());
    }

    [Fact]
    public async Task ListModels_and_DeleteConversation_delegate_to_the_backend()
    {
        var backend = new FakeCopilotBackend
        {
            Models = [new ModelInfo { Id = "gpt-5", Name = "GPT-5" }],
        };
        await using var client = TestChatClient.Create(backend);

        var models = await client.ListModelsAsync();
        Assert.Equal("gpt-5", Assert.Single(models).Id);

        await client.DeleteConversationAsync("conv-9");
        Assert.Equal("conv-9", Assert.Single(backend.DeletedSessions));
    }
}
