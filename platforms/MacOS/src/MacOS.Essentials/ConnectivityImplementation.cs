using System.Net;
using CoreFoundation;
using SystemConfiguration;
using Microsoft.Maui.Networking;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class ConnectivityImplementation : IConnectivity
{
    NetworkReachability? _defaultRouteReachability;
    NetworkReachability? _remoteHostReachability;
    event EventHandler<ConnectivityChangedEventArgs>? _connectivityChanged;

    NetworkAccess _currentAccess;
    List<ConnectionProfile> _currentProfiles = new();

    public NetworkAccess NetworkAccess
    {
        get
        {
            var internetStatus = GetInternetConnectionStatus();
            if (internetStatus == NetworkConnectionStatus.ReachableViaWiFi)
                return NetworkAccess.Internet;

            var remoteStatus = GetRemoteHostStatus();
            if (remoteStatus == NetworkConnectionStatus.ReachableViaWiFi)
                return NetworkAccess.Internet;

            return NetworkAccess.None;
        }
    }

    public IEnumerable<ConnectionProfile> ConnectionProfiles
    {
        get
        {
            if (IsNetworkAvailable(out _))
                yield return ConnectionProfile.WiFi;
        }
    }

    public event EventHandler<ConnectivityChangedEventArgs> ConnectivityChanged
    {
        add
        {
            if (_connectivityChanged is null)
            {
                _currentAccess = NetworkAccess;
                _currentProfiles = new List<ConnectionProfile>(ConnectionProfiles);
                StartListeners();
            }
            _connectivityChanged += value;
        }
        remove
        {
            _connectivityChanged -= value;
            if (_connectivityChanged is null)
                StopListeners();
        }
    }

    void StartListeners()
    {
        var ip = new IPAddress(0);
        _defaultRouteReachability = new NetworkReachability(ip);
#pragma warning disable CA1422
        _defaultRouteReachability.SetNotification(OnReachabilityChanged);
        _defaultRouteReachability.Schedule(CFRunLoop.Main, CFRunLoop.ModeDefault);
#pragma warning restore CA1422

        _remoteHostReachability = new NetworkReachability("www.microsoft.com");
        _remoteHostReachability.TryGetFlags(out _);
#pragma warning disable CA1422
        _remoteHostReachability.SetNotification(OnReachabilityChanged);
        _remoteHostReachability.Schedule(CFRunLoop.Main, CFRunLoop.ModeDefault);
#pragma warning restore CA1422
    }

    void StopListeners()
    {
        _defaultRouteReachability?.Dispose();
        _defaultRouteReachability = null;
        _remoteHostReachability?.Dispose();
        _remoteHostReachability = null;
    }

    async void OnReachabilityChanged(NetworkReachabilityFlags flags)
    {
        await Task.Delay(100);
        var access = NetworkAccess;
        var profiles = new List<ConnectionProfile>(ConnectionProfiles);

        if (_currentAccess != access || !_currentProfiles.SequenceEqual(profiles))
        {
            _currentAccess = access;
            _currentProfiles = profiles;
            var args = new ConnectivityChangedEventArgs(access, profiles);
            DispatchQueue.MainQueue.DispatchAsync(() => _connectivityChanged?.Invoke(null, args));
        }
    }

    enum NetworkConnectionStatus { NotReachable, ReachableViaWiFi }

    static NetworkConnectionStatus GetRemoteHostStatus()
    {
        using var reachability = new NetworkReachability("www.microsoft.com");
        if (!reachability.TryGetFlags(out var flags))
            return NetworkConnectionStatus.NotReachable;

        if (!IsReachableWithoutRequiringConnection(flags))
            return NetworkConnectionStatus.NotReachable;

        return NetworkConnectionStatus.ReachableViaWiFi;
    }

    static NetworkConnectionStatus GetInternetConnectionStatus()
    {
        if (!IsNetworkAvailable(out var flags))
            return NetworkConnectionStatus.NotReachable;

        if ((flags & NetworkReachabilityFlags.Reachable) != 0)
            return NetworkConnectionStatus.ReachableViaWiFi;

        if (((flags & NetworkReachabilityFlags.ConnectionOnDemand) != 0 ||
             (flags & NetworkReachabilityFlags.ConnectionOnTraffic) != 0) &&
            (flags & NetworkReachabilityFlags.InterventionRequired) == 0)
            return NetworkConnectionStatus.ReachableViaWiFi;

        return NetworkConnectionStatus.NotReachable;
    }

    static bool IsNetworkAvailable(out NetworkReachabilityFlags flags)
    {
        var ip = new IPAddress(0);
        using var reachability = new NetworkReachability(ip);
        if (!reachability.TryGetFlags(out flags))
            return false;
        return IsReachableWithoutRequiringConnection(flags);
    }

    static bool IsReachableWithoutRequiringConnection(NetworkReachabilityFlags flags)
    {
        var isReachable = (flags & NetworkReachabilityFlags.Reachable) != 0;
        var noConnectionRequired = (flags & NetworkReachabilityFlags.ConnectionRequired) == 0;
        return isReachable && noConnectionRequired;
    }
}
