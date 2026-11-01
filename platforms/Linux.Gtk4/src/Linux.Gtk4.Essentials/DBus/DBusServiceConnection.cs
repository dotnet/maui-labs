using Tmds.DBus.Protocol;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

/// <summary>
/// Thin wrapper over a <see cref="DBusConnection"/> targeting a single D-Bus service:
/// method calls and access to properties via org.freedesktop.DBus.Properties.
/// </summary>
internal sealed class DBusServiceConnection : IDisposable
{
    const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    readonly DBusConnection _connection;
    readonly string _destination;

    public delegate void WriteArguments(ref MessageWriter writer);

    DBusServiceConnection(DBusConnection connection, string destination)
    {
        _connection = connection;
        _destination = destination;
    }

    /// <summary>
    /// Creates a (not yet connected) connection to <paramref name="destination"/> on the system bus.
    /// </summary>
    public static DBusServiceConnection ForSystemBus(string destination) =>
        new(new DBusConnection(
                DBusAddress.System ?? throw new InvalidOperationException("The D-Bus system bus is unavailable.")),
            destination);

    public static bool IsSystemBusAvailable => DBusAddress.System is not null;

    public Task ConnectAsync(CancellationToken token) =>
        _connection.ConnectAsync().AsTask().WaitAsync(token);

    // ---- Method calls ----

    public Task CallMethodAsync(
        ObjectPath path,
        string interfaceName,
        string member,
        CancellationToken token) =>
        CallMethodAsync(path, interfaceName, member, "", null, token);

    public Task CallMethodAsync(
        ObjectPath path,
        string interfaceName,
        string member,
        string signature,
        WriteArguments? writeArguments,
        CancellationToken token)
    {
        var message = CreateMessage(path, interfaceName, member, signature, writeArguments);
        return _connection.CallMethodAsync(message).WaitAsync(token);
    }

    public Task<T> CallMethodAsync<T>(
        ObjectPath path,
        string interfaceName,
        string member,
        CancellationToken token) =>
        CallMethodAsync<T>(path, interfaceName, member, "", null, token);

    public Task<T> CallMethodAsync<T>(
        ObjectPath path,
        string interfaceName,
        string member,
        string signature,
        WriteArguments? writeArguments,
        CancellationToken token)
    {
        var message = CreateMessage(path, interfaceName, member, signature, writeArguments);

        return _connection.CallMethodAsync(
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

    // ---- Properties ----

    public async Task<T> GetPropertyAsync<T>(
        ObjectPath path,
        string interfaceName,
        string propertyName,
        CancellationToken token)
    {
        var value = await CallMethodAsync<VariantValue>(
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

    public Task SetPropertyAsync<T>(
        ObjectPath path,
        string interfaceName,
        string propertyName,
        T value,
        CancellationToken token)
    {
        var variant = ToVariant(value);

        return CallMethodAsync(
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

    public void Dispose() => _connection.Dispose();

    // ---- Helpers ----

    MessageBuffer CreateMessage(
        ObjectPath path,
        string interfaceName,
        string member,
        string signature,
        WriteArguments? writeArguments)
    {
        var writer = _connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(
                destination: _destination,
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
}