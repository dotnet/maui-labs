using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors
{
    [JsonSerializable(typeof(PlacemarkResponseInterop))]
    [JsonSerializable(typeof(AddressInterop))]
    [JsonSerializable(typeof(IEnumerable<NominatimResponse>))]
    public partial class LinuxGtkJsonSerializerContext : JsonSerializerContext
    {
    }

    public class NominatimResponse
    {
        [JsonPropertyName("lat")]
        public string Lat { get; set; } = null!;

        [JsonPropertyName("lon")]
        public string Lon { get; set; } = null!;


        public double LatValue => double.Parse(Lat, CultureInfo.InvariantCulture);
        public double LonValue => double.Parse(Lon, CultureInfo.InvariantCulture);
    }

    public class AddressInterop
    {
        [JsonPropertyName("road")]
        public string? Road { get; set; }

        [JsonPropertyName("hamlet")]
        public string? Hamlet { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("town")]
        public string? Town { get; set; }

        [JsonPropertyName("village")]
        public string? Village { get; set; }

        [JsonPropertyName("municipality")]
        public string? Municipality { get; set; }

        [JsonPropertyName("county")]
        public string? County { get; set; }

        [JsonPropertyName("ISO3166-2-lvl6")]
        public string? ISO31662lvl6 { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("ISO3166-2-lvl4")]
        public string? ISO31662lvl4 { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("postcode")]
        public string? Postcode { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("country_code")]
        public string? CcountryCode { get; set; }
    }

    public class PlacemarkResponseInterop
    {
        [JsonPropertyName("place_id")]
        public long PlaceId { get; set; }

        [JsonPropertyName("licence")]
        public string? License { get; set; }

        [JsonPropertyName("osm_type")]
        public string? OsmType { get; set; }

        [JsonPropertyName("osm_id")]
        public long OsmId { get; set; }

        [JsonPropertyName("lat")]
        public string? Lat { get; set; }

        [JsonPropertyName("lon")]
        public string? Lon { get; set; }

        [JsonPropertyName("class")]
        public string? Class { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("place_rank")]
        public int PlaceRank { get; set; }

        [JsonPropertyName("importance")]
        public double Importance { get; set; }

        [JsonPropertyName("addresstype")]
        public string? AddressType { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("address")]
        public AddressInterop? Address { get; set; }

        [JsonPropertyName("boundingbox")]
        public List<string>? boundingbox { get; set; }

        public double LatValue => double.Parse(Lat!, CultureInfo.InvariantCulture);
        public double LonValue => double.Parse(Lon!, CultureInfo.InvariantCulture);
    }
}
