using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Native.Essentials;

/// <summary>
/// The native DevFlow agent with the .NET MAUI Essentials-backed endpoints switched on.
/// </summary>
/// <remarks>
/// The base <see cref="NativeDevFlowAgentService"/> answers preferences, secure storage, device,
/// permission, geolocation and sensor requests with a <c>501 not_supported</c> envelope because a
/// plain .NET app has no Essentials by default. Referencing this add-on and starting the agent
/// through <see cref="EssentialsDevFlowAgent"/> replaces those with the exact implementations the
/// MAUI agent uses — the endpoint bodies are a shared source file, not a copy.
///
/// Theme remains unsupported: MAUI's theme endpoints are driven by <c>Application.RequestedTheme</c>,
/// which does not exist outside MAUI Controls.
/// </remarks>
public class EssentialsNativeDevFlowAgentService : NativeDevFlowAgentService
{
    private readonly EssentialsAgentSupport _essentials = new();

    /// <summary>
    /// Creates a native agent service with Essentials-backed endpoints.
    /// </summary>
    public EssentialsNativeDevFlowAgentService(AgentOptions? options = null)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override bool IsStorageSupported => true;

    /// <inheritdoc />
    protected override bool IsDeviceInfoSupported => true;

    /// <inheritdoc />
    protected override bool IsSensorsSupported => true;

    protected override Task<HttpResponse> HandlePreferencesList(HttpRequest request)
        => _essentials.HandlePreferencesList(request);

    protected override Task<HttpResponse> HandlePreferencesGet(HttpRequest request)
        => _essentials.HandlePreferencesGet(request);

    protected override Task<HttpResponse> HandlePreferencesSet(HttpRequest request)
        => _essentials.HandlePreferencesSet(request);

    protected override Task<HttpResponse> HandlePreferencesDelete(HttpRequest request)
        => _essentials.HandlePreferencesDelete(request);

    protected override Task<HttpResponse> HandlePreferencesClear(HttpRequest request)
        => _essentials.HandlePreferencesClear(request);

    protected override Task<HttpResponse> HandleSecureStorageGet(HttpRequest request)
        => _essentials.HandleSecureStorageGet(request);

    protected override Task<HttpResponse> HandleSecureStorageSet(HttpRequest request)
        => _essentials.HandleSecureStorageSet(request);

    protected override Task<HttpResponse> HandleSecureStorageDelete(HttpRequest request)
        => _essentials.HandleSecureStorageDelete(request);

    protected override Task<HttpResponse> HandleSecureStorageClear(HttpRequest request)
        => _essentials.HandleSecureStorageClear(request);

    protected override string GetAppDataBasePath()
        => _essentials.GetAppDataBasePath();

    protected override Task<HttpResponse> HandlePlatformDeviceInfo(HttpRequest request)
        => _essentials.HandlePlatformDeviceInfo(request);

    protected override Task<HttpResponse> HandlePlatformDeviceDisplay(HttpRequest request)
        => _essentials.HandlePlatformDeviceDisplay(request);

    protected override Task<HttpResponse> HandlePlatformBattery(HttpRequest request)
        => _essentials.HandlePlatformBattery(request);

    protected override Task<HttpResponse> HandlePlatformConnectivity(HttpRequest request)
        => _essentials.HandlePlatformConnectivity(request);

    protected override Task<HttpResponse> HandlePlatformVersionTracking(HttpRequest request)
        => _essentials.HandlePlatformVersionTracking(request);

    protected override Task<HttpResponse> HandlePlatformPermissions(HttpRequest request)
        => _essentials.HandlePlatformPermissions(request);

    protected override Task<HttpResponse> HandlePlatformPermissionCheck(HttpRequest request)
        => _essentials.HandlePlatformPermissionCheck(request);

    protected override Task<HttpResponse> HandlePlatformGeolocation(HttpRequest request)
        => _essentials.HandlePlatformGeolocation(request);

    protected override Task<HttpResponse> HandleSensorsList(HttpRequest request)
        => _essentials.HandleSensorsList(request);

    protected override Task<HttpResponse> HandleSensorStart(HttpRequest request)
        => _essentials.HandleSensorStart(request);

    protected override Task<HttpResponse> HandleSensorStop(HttpRequest request)
        => _essentials.HandleSensorStop(request);

    protected override Task HandleSensorWebSocket(System.Net.Sockets.TcpClient client, System.Net.Sockets.NetworkStream stream, HttpRequest request, CancellationToken ct)
        => _essentials.HandleSensorWebSocket(client, stream, request, ct);
}
