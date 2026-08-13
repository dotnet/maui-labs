using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddCopilotSdkChatClient_registers_configuration()
    {
        var services = new ServiceCollection();
        services.AddCopilotSdkChatClient(config => config.Model = "gpt-5");

        using var provider = services.BuildServiceProvider();

        var configuration = provider.GetRequiredService<CopilotSdkConfiguration>();
        Assert.Equal("gpt-5", configuration.Model);
    }

    [Fact]
    public void AddCopilotSdkChatClient_registers_chat_client_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddCopilotSdkChatClient();

        using var provider = services.BuildServiceProvider();

        var asInterface = provider.GetRequiredService<IChatClient>();
        var asConcrete = provider.GetRequiredService<CopilotSdkChatClient>();

        Assert.IsType<CopilotSdkChatClient>(asInterface);
        Assert.Same(asConcrete, asInterface);
    }

    [Fact]
    public void AddCopilotSdkChatClient_throws_for_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddCopilotSdkChatClient());
    }
}
