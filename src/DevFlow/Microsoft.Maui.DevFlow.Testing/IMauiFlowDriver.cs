using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Operation-level surface consumed by the canonical flow runner. Lifecycle, transport, and
/// device ownership deliberately remain outside this public contract.
/// </summary>
public interface IMauiFlowDriver
{
    WorkflowCommandReceipt? LastWorkflowCommandReceipt { get; }

    Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null);
    Task<ElementInfo?> GetElementAsync(string id);
    Task<bool> TapAsync(string elementId);
    Task<bool> FillAsync(string elementId, string text);
    Task<bool> SetPropertyAsync(string elementId, string propertyName, string value);
    Task<bool> ScrollAsync(
        string? elementId = null,
        double deltaX = 0,
        double deltaY = 0,
        bool animated = true,
        int? itemIndex = null,
        string? scrollToPosition = null);
    Task<bool> NavigateAsync(string route);
    Task<bool> BackAsync();
    Task<ThemeResult> SetThemeAsync(DevFlowTheme theme);
    Task<string?> GetPropertyAsync(string elementId, string propertyName);
    Task<AgentStatus?> GetStatusAsync();
    /// <summary>
    /// Optional value-free tree source used only for diagnostic selector evidence. Existing driver
    /// implementations may omit it; selector capture then records an explicit omission without
    /// changing selector resolution or replay.
    /// </summary>
    Task<List<ElementInfo>> GetTreeAsync(int maxDepth = 0)
        => Task.FromResult(new List<ElementInfo>());
}

/// <summary>Adapter that allows the public runner to consume the existing <see cref="AgentClient"/> API.</summary>
public sealed class AgentClientMauiFlowDriver : IMauiFlowDriver
{
    private readonly AgentClient _client;

    public AgentClientMauiFlowDriver(AgentClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public WorkflowCommandReceipt? LastWorkflowCommandReceipt => _client.LastWorkflowCommandReceipt;

    public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
        => _client.QueryAsync(type, automationId, text);

    public Task<ElementInfo?> GetElementAsync(string id) => _client.GetElementAsync(id);
    public Task<bool> TapAsync(string elementId) => _client.TapAsync(elementId);
    public Task<bool> FillAsync(string elementId, string text) => _client.FillAsync(elementId, text);
    public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value)
        => _client.SetPropertyAsync(elementId, propertyName, value);

    public Task<bool> ScrollAsync(
        string? elementId = null,
        double deltaX = 0,
        double deltaY = 0,
        bool animated = true,
        int? itemIndex = null,
        string? scrollToPosition = null)
        => _client.ScrollAsync(
            elementId,
            deltaX,
            deltaY,
            animated,
            itemIndex: itemIndex,
            scrollToPosition: scrollToPosition);

    public Task<bool> NavigateAsync(string route) => _client.NavigateAsync(route);
    public Task<bool> BackAsync() => _client.BackAsync();
    public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => _client.SetThemeAsync(theme);
    public Task<string?> GetPropertyAsync(string elementId, string propertyName)
        => _client.GetPropertyAsync(elementId, propertyName);
    public Task<AgentStatus?> GetStatusAsync() => _client.GetStatusAsync();
    public Task<List<ElementInfo>> GetTreeAsync(int maxDepth = 0) => _client.GetTreeAsync(maxDepth);
}
