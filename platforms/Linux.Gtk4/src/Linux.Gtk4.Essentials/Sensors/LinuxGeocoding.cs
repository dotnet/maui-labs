using Microsoft.Maui.Devices.Sensors;
using System.Globalization;
using System.Text.Json;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

/// <summary>
/// <see cref="IGeocoding"/> backed by the public OpenStreetMap Nominatim service.
/// </summary>
/// <remarks>
/// The public Nominatim instance is limited to about one request per second and requires an
/// identifying User-Agent: see https://operations.osmfoundation.org/policies/nominatim/.
/// Requests to the public instance are throttled accordingly.
/// </remarks>
public class LinuxGeocoding : IGeocoding
{
    /// <summary>Base address of the public Nominatim instance.</summary>
    public const string DefaultBaseAddress = "https://nominatim.openstreetmap.org/";

    const string DefaultUserAgent = "LinuxGtkEssentials/1.0";

    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan PublicInstanceMinInterval = TimeSpan.FromSeconds(1);

    static readonly Lazy<HttpClient> SharedClient = new(() =>
    {
        var client = new HttpClient { Timeout = DefaultTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        return client;
    });

    // Shared by all instances: the public usage limit applies to the whole application.
    static readonly SemaphoreSlim PublicInstanceGate = new(1, 1);
    static DateTime s_nextPublicRequestUtc = DateTime.MinValue;

    readonly HttpClient _client;
    readonly string _baseAddress;
    readonly bool _isPublicInstance;

    /// <summary>Creates a geocoder that calls <paramref name="baseAddress"/> with <paramref name="client"/>.</summary>
    public LinuxGeocoding(HttpClient? client = null, string baseAddress = DefaultBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        if (client != null && client.DefaultRequestHeaders.UserAgent.Count == 0)
            client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);

        _client = client ?? SharedClient.Value;
        _baseAddress = baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/";
        _isPublicInstance = string.Equals(_baseAddress, DefaultBaseAddress, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Placemark>> GetPlacemarksAsync(double latitude, double longitude)
    {
        var json = await GetStringAsync(BuildReverseUrl(latitude, longitude, _baseAddress)).ConfigureAwait(false);
        return ParsePlacemarks(json);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Location>> GetLocationsAsync(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var json = await GetStringAsync(BuildSearchUrl(address, _baseAddress)).ConfigureAwait(false);
        return ParseLocations(json);
    }

    async Task<string> GetStringAsync(string url)
    {
        // Self-hosted instances have their own policy: no throttling.
        if (!_isPublicInstance)
            return await _client.GetStringAsync(url).ConfigureAwait(false);

        // Public instance: at most one request per second across the application.
        await PublicInstanceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var delay = s_nextPublicRequestUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay).ConfigureAwait(false);

            return await _client.GetStringAsync(url).ConfigureAwait(false);
        }
        finally
        {
            s_nextPublicRequestUtc = DateTime.UtcNow + PublicInstanceMinInterval;
            PublicInstanceGate.Release();
        }
    }

    /// <summary>Builds the reverse-geocoding URL. Coordinates are always formatted with the invariant culture.</summary>
    public static string BuildReverseUrl(double latitude, double longitude, string baseAddress = DefaultBaseAddress) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{baseAddress}reverse?format=json&lat={latitude:R}&lon={longitude:R}&addressdetails=1");

    /// <summary>Builds the forward-geocoding URL.</summary>
    public static string BuildSearchUrl(string address, string baseAddress = DefaultBaseAddress) =>
        $"{baseAddress}search?format=json&q={Uri.EscapeDataString(address)}";

    /// <summary>Converts a Nominatim <c>/reverse</c> JSON response to placemarks.</summary>
    public static IReadOnlyList<Placemark> ParsePlacemarks(string json)
    {
        var data = JsonSerializer.Deserialize(json, LinuxGtkJsonSerializerContext.Default.NominatimPlacemarkResponse);
        if (data?.Lat is null || data.Lon is null)
            return [];

        var address = data.Address;
        return
        [
            new Placemark
            {
                CountryName = address?.Country ?? string.Empty,
                CountryCode = address?.CountryCode?.ToUpperInvariant() ?? string.Empty,
                Location = new Location(data.LatValue, data.LonValue),
                FeatureName = data.Name ?? address?.Road ?? string.Empty,
                Thoroughfare = address?.Road ?? string.Empty,
                SubThoroughfare = address?.HouseNumber ?? string.Empty,
                Locality = address?.City ?? address?.Town ?? address?.Village ?? address?.Hamlet ?? string.Empty,
                SubLocality = address?.Suburb ?? string.Empty,
                AdminArea = address?.State ?? string.Empty,
                SubAdminArea = address?.County ?? string.Empty,
                PostalCode = address?.Postcode ?? string.Empty,
            }
        ];
    }

    /// <summary>Converts a Nominatim <c>/search</c> JSON response to locations.</summary>
    public static IReadOnlyList<Location> ParseLocations(string json)
    {
        var data = JsonSerializer.Deserialize(json, LinuxGtkJsonSerializerContext.Default.IEnumerableNominatimResponse);
        return data?.Select(d => new Location(d.LatValue, d.LonValue)).ToList() ?? [];
    }
}