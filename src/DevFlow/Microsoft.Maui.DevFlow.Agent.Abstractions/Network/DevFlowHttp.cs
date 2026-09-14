namespace Microsoft.Maui.DevFlow.Agent.Core.Network;

/// <summary>
/// Convenience methods for creating HttpClient instances that are automatically
/// monitored by Microsoft.Maui.DevFlow's network interceptor. Use for HttpClients created
/// outside of DI (e.g. new HttpClient()). DI-based clients are auto-intercepted.
/// </summary>
public static class DevFlowHttp
{
    private static NetworkRequestStore? _store;

    internal static void SetStore(NetworkRequestStore store) => _store = store;

    /// <summary>
    /// Creates an HttpClient wrapped with the DevFlow network interceptor.
    /// Falls back to a plain HttpClient if network monitoring is not initialized.
    /// </summary>
    public static HttpClient CreateClient(HttpMessageHandler? innerHandler = null)
    {
        var handler = CreateHandler(innerHandler);
        return handler != null ? new HttpClient(handler) : new HttpClient();
    }

    /// <summary>
    /// Creates a DevFlowHttpHandler wrapping the given inner handler.
    /// Returns null if network monitoring is not initialized.
    /// </summary>
    public static DevFlowHttpHandler? CreateHandler(HttpMessageHandler? innerHandler = null)
    {
        if (_store == null) return null;

        return innerHandler != null
            ? new DevFlowHttpHandler(_store, innerHandler)
            : new DevFlowHttpHandler(_store);
    }
}
