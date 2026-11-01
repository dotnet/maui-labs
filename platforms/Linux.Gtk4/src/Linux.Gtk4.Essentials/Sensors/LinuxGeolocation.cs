using System.Reflection;
using Microsoft.Maui.Devices.Sensors;
using Tmds.DBus.Protocol;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

/// <summary>
/// <see cref="IGeolocation"/> implementation for Linux backed by GeoClue2 over the D-Bus system bus.
/// </summary>
public class LinuxGeolocation : IGeolocation
{
    const string GeoClueService = "org.freedesktop.GeoClue2";
    const string ManagerPath = "/org/freedesktop/GeoClue2/Manager";
    const string ManagerInterface = "org.freedesktop.GeoClue2.Manager";
    const string ClientInterface = "org.freedesktop.GeoClue2.Client";
    const string LocationInterface = "org.freedesktop.GeoClue2.Location";

    // GeoClue2 accuracy levels (GClueAccuracyLevel).
    const uint AccuracyCountry = 1;
    const uint AccuracyCity = 4;
    const uint AccuracyNeighborhood = 5;
    const uint AccuracyStreet = 6;
    const uint AccuracyExact = 8;

    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan SinglePollInterval = TimeSpan.FromMilliseconds(500);

    readonly object _sync = new();

    DBusServiceConnection? _connection;
    ObjectPath? _clientPath;
    CancellationTokenSource? _listeningCts;
    Task? _listeningTask;

    volatile MauiLocation? _lastKnownLocation;

    public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged;
    public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed;

    /// <summary>
    /// Desktop file id (without the ".desktop" suffix) reported to GeoClue.
    /// GeoClue agents use it to authorize the application, so it should match the app's .desktop file.
    /// </summary>
    public static string DesktopId { get; set; } =
        Assembly.GetEntryAssembly()?.GetName().Name ?? "maui-app";

    public bool IsListeningForeground
    {
        get
        {
            lock (_sync)
                return _listeningCts is { IsCancellationRequested: false };
        }
    }

    public bool IsEnabled => OperatingSystem.IsLinux() && DBusServiceConnection.IsSystemBusAvailable;

    public Task<MauiLocation?> GetLastKnownLocationAsync() =>
        Task.FromResult(_lastKnownLocation);

    public async Task<MauiLocation?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsEnabled)
            return null;

        var timeout = request.Timeout > TimeSpan.Zero ? request.Timeout : DefaultTimeout;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
        cts.CancelAfter(timeout);

        using var connection = CreateConnection();
        ObjectPath? clientPath = null;

        try
        {
            await connection.ConnectAsync(cts.Token).ConfigureAwait(false);

            clientPath = await CreateClientAsync(
                connection,
                MapAccuracy(request.DesiredAccuracy, request.RequestFullAccuracy),
                cts.Token).ConfigureAwait(false);

            await connection.CallMethodAsync(clientPath.Value, ClientInterface, "Start", cts.Token)
                .ConfigureAwait(false);

            // GeoClue may not provide a position immediately after Start.
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();

                var locationPath = await connection.GetPropertyAsync<ObjectPath>(
                    clientPath.Value, ClientInterface, "Location", cts.Token).ConfigureAwait(false);

                if (IsValidPath(locationPath))
                {
                    var location = await ReadLocationAsync(connection, locationPath, cts.Token).ConfigureAwait(false);
                    _lastKnownLocation = location;
                    return location;
                }

                await Task.Delay(SinglePollInterval, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Timed out.
            return null;
        }
        finally
        {
            if (clientPath is not null)
                await TryStopClientAsync(connection, clientPath.Value).ConfigureAwait(false);
        }
    }

    public async Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsEnabled)
            return false;

        if (request.MinimumTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "MinimumTime must be zero or greater.");

        CancellationTokenSource cts;
        DBusServiceConnection connection;

        lock (_sync)
        {
            if (_listeningCts is { IsCancellationRequested: false })
                throw new InvalidOperationException("Location listening is already active.");

            cts = new CancellationTokenSource();
            connection = CreateConnection();
            _listeningCts = cts;
            _connection = connection;
        }

        try
        {
            await connection.ConnectAsync(cts.Token).ConfigureAwait(false);

            var clientPath = await CreateClientAsync(
                connection,
                MapAccuracy(request.DesiredAccuracy),
                cts.Token).ConfigureAwait(false);

            await connection.CallMethodAsync(clientPath, ClientInterface, "Start", cts.Token)
                .ConfigureAwait(false);

            lock (_sync)
            {
                if (!ReferenceEquals(_listeningCts, cts))
                {
                    // StopListeningForeground was called while starting.
                    return false;
                }

                _clientPath = clientPath;
                _listeningTask = ListenForLocationsAsync(connection, clientPath, request.MinimumTime, cts);
            }

            return true;
        }
        catch
        {
            StopListening(cts);
            return false;
        }
    }

    public void StopListeningForeground()
    {
        CancellationTokenSource? cts;

        lock (_sync)
            cts = _listeningCts;

        if (cts is not null)
            StopListening(cts);
    }

    void StopListening(CancellationTokenSource cts)
    {
        DBusServiceConnection? connection;
        ObjectPath? clientPath;

        lock (_sync)
        {
            if (!ReferenceEquals(_listeningCts, cts))
                return;

            connection = _connection;
            clientPath = _clientPath;

            _listeningCts = null;
            _connection = null;
            _clientPath = null;
            _listeningTask = null;
        }

        cts.Cancel();

        if (connection is not null)
        {
            // Stop the client before closing the connection. Closing the connection
            // also makes GeoClue release the client, so failures here are harmless.
            _ = StopAndDisposeAsync(connection, clientPath, cts);
        }
        else
        {
            cts.Dispose();
        }
    }

    static async Task StopAndDisposeAsync(DBusServiceConnection connection, ObjectPath? clientPath, CancellationTokenSource cts)
    {
        try
        {
            if (clientPath is not null)
                await TryStopClientAsync(connection, clientPath.Value).ConfigureAwait(false);
        }
        finally
        {
            connection.Dispose();
            cts.Dispose();
        }
    }

    async Task ListenForLocationsAsync(
        DBusServiceConnection connection,
        ObjectPath clientPath,
        TimeSpan minimumTime,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        var interval = minimumTime > TimeSpan.Zero ? minimumTime : DefaultPollInterval;
        string? lastLocationPath = null;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var locationPath = await connection.GetPropertyAsync<ObjectPath>(
                    clientPath, ClientInterface, "Location", token).ConfigureAwait(false);

                // GeoClue exposes a new Location object for every update,
                // so a change of object path means a new fix is available.
                var path = locationPath.ToString();
                if (IsValidPath(locationPath) && path != lastLocationPath)
                {
                    var location = await ReadLocationAsync(connection, locationPath, token).ConfigureAwait(false);

                    lastLocationPath = path;
                    _lastKnownLocation = location;

                    if (!token.IsCancellationRequested)
                        LocationChanged?.Invoke(this, new GeolocationLocationChangedEventArgs(location));
                }

                await Task.Delay(interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Listening stopped normally.
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            StopListening(cts);
            ListeningFailed?.Invoke(this, new GeolocationListeningFailedEventArgs(GeolocationError.PositionUnavailable));
        }
        catch
        {
            // Errors raised while shutting down (e.g. disposed connection) are ignored.
        }
    }

    static async Task<MauiLocation> ReadLocationAsync(
        DBusServiceConnection connection,
        ObjectPath path,
        CancellationToken token)
    {
        var latitude = await connection.GetPropertyAsync<double>(path, LocationInterface, "Latitude", token).ConfigureAwait(false);
        var longitude = await connection.GetPropertyAsync<double>(path, LocationInterface, "Longitude", token).ConfigureAwait(false);
        var accuracy = await connection.GetPropertyAsync<double>(path, LocationInterface, "Accuracy", token).ConfigureAwait(false);
        var altitude = await connection.GetPropertyAsync<double>(path, LocationInterface, "Altitude", token).ConfigureAwait(false);
        var speed = await connection.GetPropertyAsync<double>(path, LocationInterface, "Speed", token).ConfigureAwait(false);
        var heading = await connection.GetPropertyAsync<double>(path, LocationInterface, "Heading", token).ConfigureAwait(false);

        // GeoClue reports unknown values as -DBL_MAX (altitude) or a negative number (speed, heading).
        // The local read time is used instead of decoding the D-Bus "Timestamp" (tt) struct.
        return new MauiLocation(latitude, longitude)
        {
            Accuracy = accuracy >= 0 ? accuracy : null,
            Altitude = altitude > double.MinValue ? altitude : null,
            Speed = speed >= 0 ? speed : null,
            Course = heading >= 0 ? heading : null,
            Timestamp = DateTimeOffset.UtcNow,
        };
    }

    static uint MapAccuracy(GeolocationAccuracy accuracy, bool requestFullAccuracy = false)
    {
        if (requestFullAccuracy)
            return AccuracyExact;

        return accuracy switch
        {
            GeolocationAccuracy.Lowest => AccuracyCountry,
            GeolocationAccuracy.Low => AccuracyCity,
            GeolocationAccuracy.Medium => AccuracyNeighborhood,
            GeolocationAccuracy.High => AccuracyStreet,
            GeolocationAccuracy.Best => AccuracyExact,
            _ => AccuracyNeighborhood,
        };
    }

    static bool IsValidPath(ObjectPath path) => path.ToString() != "/";

    static DBusServiceConnection CreateConnection() =>
        DBusServiceConnection.ForSystemBus(GeoClueService);

    static async Task<ObjectPath> CreateClientAsync(DBusServiceConnection connection, uint accuracy, CancellationToken token)
    {
        var clientPath = await connection.CallMethodAsync<ObjectPath>(
            new ObjectPath(ManagerPath), ManagerInterface, "CreateClient", token).ConfigureAwait(false);

        // DesktopId must be set before Start, otherwise GeoClue rejects the client.
        await connection.SetPropertyAsync(clientPath, ClientInterface, "DesktopId", DesktopId, token).ConfigureAwait(false);
        await connection.SetPropertyAsync(clientPath, ClientInterface, "RequestedAccuracyLevel", accuracy, token).ConfigureAwait(false);

        return clientPath;
    }

    static async Task TryStopClientAsync(DBusServiceConnection connection, ObjectPath clientPath)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await connection.CallMethodAsync(clientPath, ClientInterface, "Stop", cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // The connection or the GeoClue client may already be closed.
        }
    }
}