using Microsoft.Maui.Networking;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class ConnectionStatusService
{
    public ConnectionStatusResult GetStatus()
    {
        var connectivity = Connectivity.Current;
        return new ConnectionStatusResult(
            connectivity.NetworkAccess.ToString(),
            [.. connectivity.ConnectionProfiles.Select(profile => profile.ToString())]);
    }
}
