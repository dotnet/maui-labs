namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// xUnit collection fixture wrapper that selects a platform-specific
/// fixture based on DEVFLOW_TEST_PLATFORM.
/// </summary>
public sealed class AppFixture : IAppFixture, IAsyncLifetime
{
    readonly IAppFixture _inner;

    public AppFixture()
    {
        _inner = AppFixtureFactory.Create();
    }

    public Driver.AgentClient Client => _inner.Client;
    public HttpClient Http => _inner.Http;
    public int AgentPort => _inner.AgentPort;
    public string AgentBaseUrl => _inner.AgentBaseUrl;
    public string Platform => _inner.Platform;

    public Task InitializeAsync() => _inner.InitializeAsync();
    public Task DisposeAsync() => _inner.DisposeAsync();
}

[CollectionDefinition("AgentIntegration")]
public class AgentIntegrationCollection : ICollectionFixture<AppFixture>;
