using Microsoft.Maui.Devices.Sensors;
using System.Reflection;
using Tmds.DBus.Protocol;

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
    const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    // GeoClue2 accuracy levels (GClueAccuracyLevel).
    const uint AccuracyNone = 0;
    const uint AccuracyCountry = 1;
    const uint AccuracyCity = 4;
    const uint AccuracyNeighborhood = 5;
    const uint AccuracyStreet = 6;
    const uint AccuracyExact = 8;

    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan EnabledProbeTimeout = TimeSpan.FromSeconds(2);
    static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan SinglePollInterval = TimeSpan.FromMilliseconds(500);

    readonly object _sync = new();

    DBusConnection? _connection;
    ObjectPath? _clientPath;
    CancellationTokenSource? _listeningCts;
    Task? _listeningTask;

    volatile Location? _lastKnownLocation;

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

    /// <summary>
    /// True when GeoClue2 is reachable on the system bus and currently allows some accuracy level.
    /// </summary>
    /// <remarks>
    /// A non-null <see cref="DBusAddress.System"/> proves nothing: it falls back to the default socket path even
    /// when there is no system bus. This property therefore connects to the bus and reads the GeoClue Manager's
    /// <c>AvailableAccuracyLevel</c>; a missing bus, a missing GeoClue service or any D-Bus error yields false.
    /// GeoClue reports <c>AvailableAccuracyLevel</c> = NONE when location services are switched off
    /// (e.g. by the desktop's privacy settings). Per-application authorization by the GeoClue agent
    /// can still be refused when a client is started. Each call performs a D-Bus round trip.
    /// </remarks>
    public bool IsEnabled
    {
        get
        {
            try
            {
                // Task.Run: blocking on async code from a UI SynchronizationContext could otherwise deadlock.
                return Task.Run(() => GetAvailableAccuracyAsync(CancellationToken.None))
                    .GetAwaiter().GetResult() > AccuracyNone;
            }
            catch
            {
                return false; // no bus, no GeoClue, timeout or any D-Bus error
            }
        }
    }

    async Task<uint> GetAvailableAccuracyAsync(CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(EnabledProbeTimeout);

        using var connection = CreateConnection();
        await connection.ConnectAsync().AsTask().WaitAsync(cts.Token).ConfigureAwait(false);
        return await GetPropertyAsync<uint>(connection, new ObjectPath(ManagerPath), ManagerInterface, "AvailableAccuracyLevel", cts.Token).ConfigureAwait(false);
    }
    
    static async Task<bool> IsLocationOffAsync(DBusConnection connection, CancellationToken token)
    {
        try
        {
            return await GetPropertyAsync<uint>(connection, new ObjectPath(ManagerPath), ManagerInterface,
                "AvailableAccuracyLevel", token).ConfigureAwait(false) == AccuracyNone;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    public Task<Location?> GetLastKnownLocationAsync() =>
        Task.FromResult(_lastKnownLocation);

    public async Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken)
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
            await connection.ConnectAsync().AsTask().WaitAsync(cts.Token).ConfigureAwait(false);

            clientPath = await CreateClientAsync(
                connection,
                MapAccuracy(request.DesiredAccuracy, request.RequestFullAccuracy),
                cts.Token).ConfigureAwait(false);

            await CallMethodAsync(connection, clientPath.Value, ClientInterface, "Start", cts.Token)
                .ConfigureAwait(false);

            // GeoClue may not provide a position immediately after Start.
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();

                var locationPath = await GetPropertyAsync<ObjectPath>(
                    connection, clientPath.Value, ClientInterface, "Location", cts.Token).ConfigureAwait(false);

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
        DBusConnection connection;

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
            await connection.ConnectAsync().AsTask().WaitAsync(cts.Token).ConfigureAwait(false);

            var clientPath = await CreateClientAsync(
                connection,
                MapAccuracy(request.DesiredAccuracy),
                cts.Token).ConfigureAwait(false);

            await CallMethodAsync(connection, clientPath, ClientInterface, "Start", cts.Token)
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
        DBusConnection? connection;
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

    static async Task StopAndDisposeAsync(DBusConnection connection, ObjectPath? clientPath, CancellationTokenSource cts)
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
        DBusConnection connection,
        ObjectPath clientPath,
        TimeSpan minimumTime,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        var interval = minimumTime > TimeSpan.Zero ? minimumTime : DefaultPollInterval;
        string? lastLocationPath = null;
        var activeCheckInterval = TimeSpan.FromSeconds(1);
        var nextLocationRead = DateTime.UtcNow;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var active = await GetPropertyAsync<bool>(connection, clientPath, ClientInterface, "Active", token).ConfigureAwait(false);
                if (!active)
                {
                    var locationOff = await IsLocationOffAsync(connection, token).ConfigureAwait(false);
                    StopListening(cts);
                    ListeningFailed?.Invoke(this, new GeolocationListeningFailedEventArgs(
                        locationOff ? GeolocationError.PositionUnavailable : GeolocationError.Unauthorized));
                    return;
                }

                if (DateTime.UtcNow >= nextLocationRead)
                {
                    var locationPath = await GetPropertyAsync<ObjectPath>(connection, clientPath, ClientInterface, "Location", token).ConfigureAwait(false);

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
                    nextLocationRead = DateTime.UtcNow + interval;
                }

                await Task.Delay(interval < activeCheckInterval ? interval : activeCheckInterval, token).ConfigureAwait(false);
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

    static async Task<Location> ReadLocationAsync(
        DBusConnection connection,
        ObjectPath path,
        CancellationToken token)
    {
        var latitude = await GetPropertyAsync<double>(connection, path, LocationInterface, "Latitude", token).ConfigureAwait(false);
        var longitude = await GetPropertyAsync<double>(connection, path, LocationInterface, "Longitude", token).ConfigureAwait(false);
        var accuracy = await GetPropertyAsync<double>(connection, path, LocationInterface, "Accuracy", token).ConfigureAwait(false);
        var altitude = await GetPropertyAsync<double>(connection, path, LocationInterface, "Altitude", token).ConfigureAwait(false);
        var speed = await GetPropertyAsync<double>(connection, path, LocationInterface, "Speed", token).ConfigureAwait(false);
        var heading = await GetPropertyAsync<double>(connection, path, LocationInterface, "Heading", token).ConfigureAwait(false);

        // GeoClue reports unknown values as -DBL_MAX (altitude) or a negative number (speed, heading).
        // The local read time is used instead of decoding the D-Bus "Timestamp" (tt) struct.
        return new Location(latitude, longitude)
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

    static DBusConnection CreateConnection() =>
        new(DBusAddress.System ?? throw new InvalidOperationException("The D-Bus system bus is unavailable."));

    static async Task<ObjectPath> CreateClientAsync(DBusConnection connection, uint accuracy, CancellationToken token)
    {
        var clientPath = await CallMethodAsync<ObjectPath>(
            connection, new ObjectPath(ManagerPath), ManagerInterface, "CreateClient", "", null, token).ConfigureAwait(false);

        // DesktopId must be set before Start, otherwise GeoClue rejects the client.
        await SetPropertyAsync(connection, clientPath, ClientInterface, "DesktopId", DesktopId, token).ConfigureAwait(false);
        await SetPropertyAsync(connection, clientPath, ClientInterface, "RequestedAccuracyLevel", accuracy, token).ConfigureAwait(false);

        return clientPath;
    }

    static async Task TryStopClientAsync(DBusConnection connection, ObjectPath clientPath)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await CallMethodAsync(connection, clientPath, ClientInterface, "Stop", cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // The connection or the GeoClue client may already be closed.
        }
    }

    static async Task<T> GetPropertyAsync<T>(
        DBusConnection connection,
        ObjectPath path,
        string interfaceName,
        string propertyName,
        CancellationToken token)
    {
        var value = await CallMethodAsync<VariantValue>(
            connection,
            path,
            PropertiesInterface,
            "Get",
            "ss",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(interfaceName);
                writer.WriteString(propertyName);
            },
            token).ConfigureAwait(false);

        return ConvertVariant<T>(value);
    }

    static Task SetPropertyAsync<T>(
        DBusConnection connection,
        ObjectPath path,
        string interfaceName,
        string propertyName,
        T value,
        CancellationToken token)
    {
        var variant = ToVariant(value);

        return CallMethodAsync(
            connection,
            path,
            PropertiesInterface,
            "Set",
            "ssv",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(interfaceName);
                writer.WriteString(propertyName);
                writer.WriteVariant(variant);
            },
            token);
    }

    static VariantValue ToVariant<T>(T value) => value switch
    {
        uint v => VariantValue.UInt32(v),
        string v => VariantValue.String(v),
        _ => throw new NotSupportedException($"Unsupported D-Bus type: {typeof(T).Name}"),
    };

    static T ConvertVariant<T>(VariantValue value)
    {
        object result =
            typeof(T) == typeof(bool) ? value.GetBool() :
            typeof(T) == typeof(uint) ? value.GetUInt32() :
            typeof(T) == typeof(double) ? value.GetDouble() :
            typeof(T) == typeof(string) ? value.GetString() :
            typeof(T) == typeof(ObjectPath) ? value.GetObjectPath() :
            throw new NotSupportedException($"Unsupported D-Bus type: {typeof(T).Name}");

        return (T)result;
    }

    delegate void WriteArguments(ref MessageWriter writer);

    static Task CallMethodAsync(
        DBusConnection connection,
        ObjectPath path,
        string interfaceName,
        string member,
        CancellationToken token) =>
        CallMethodAsync(connection, path, interfaceName, member, "", null, token);

    static Task CallMethodAsync(
        DBusConnection connection,
        ObjectPath path,
        string interfaceName,
        string member,
        string signature,
        WriteArguments? writeArguments,
        CancellationToken token)
    {
        var message = CreateMessage(connection, path, interfaceName, member, signature, writeArguments);
        return connection.CallMethodAsync(message).WaitAsync(token);
    }

    static Task<T> CallMethodAsync<T>(
        DBusConnection connection,
        ObjectPath path,
        string interfaceName,
        string member,
        string signature,
        WriteArguments? writeArguments,
        CancellationToken token)
    {
        var message = CreateMessage(connection, path, interfaceName, member, signature, writeArguments);

        return connection.CallMethodAsync(
            message,
            static (Message reply, object? _) =>
            {
                var reader = reply.GetBodyReader();

                if (typeof(T) == typeof(ObjectPath))
                    return (T)(object)reader.ReadObjectPath();

                if (typeof(T) == typeof(VariantValue))
                    return (T)(object)reader.ReadVariantValue();

                throw new NotSupportedException($"Unsupported D-Bus reply type: {typeof(T).Name}");
            }).WaitAsync(token);
    }

    static MessageBuffer CreateMessage(
        DBusConnection connection,
        ObjectPath path,
        string interfaceName,
        string member,
        string signature,
        WriteArguments? writeArguments)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(
                destination: GeoClueService,
                path: path.ToString(),
                @interface: interfaceName,
                signature: signature,
                member: member);

            writeArguments?.Invoke(ref writer);

            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }
}