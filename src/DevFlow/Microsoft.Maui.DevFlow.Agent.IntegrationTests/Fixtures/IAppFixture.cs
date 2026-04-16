namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Represents a platform-specific fixture that manages the lifecycle
/// of the DevFlow sample app for integration testing.
/// </summary>
public interface IAppFixture : IAsyncLifetime
{
    Driver.AgentClient Client { get; }
    HttpClient Http { get; }
    int AgentPort { get; }
    string AgentBaseUrl { get; }
    string Platform { get; }
}
