using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Local tools that stand in for a server's weather and clock services. The weather is simulated, so the scenarios
/// work offline and the same city always gets the same forecast, and each call waits before it answers so the cards
/// that show it visibly update.
/// </summary>
public sealed class ScenarioTools
{
    private static readonly string[] WeatherConditions =
        ["Sunny", "Partly cloudy", "Cloudy", "Light rain", "Showers", "Thunderstorms", "Windy", "Foggy", "Snow"];

    private readonly TimeProvider _time;

    /// <param name="latency">How long each call waits before it answers.</param>
    /// <param name="time">The clock that the local time tool reads.</param>
    public ScenarioTools(TimeSpan latency, TimeProvider? time = null)
    {
        Latency = latency;
        _time = time ?? TimeProvider.System;
        GetWeather = AIFunctionFactory.Create(
            GetWeatherAsync,
            new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Gets the current weather for one city: temperature and feels-like in \u00B0C, " +
                    "conditions, humidity in percent and wind speed in km/h.",
                SerializerOptions = ScenarioJson.Options,
            });
        GetLocalTime = AIFunctionFactory.Create(
            GetLocalTimeAsync,
            new AIFunctionFactoryOptions
            {
                Name = "get_local_time",
                Description = "Gets the current local date and time in an IANA time zone, such as Asia/Tokyo or " +
                    "America/New_York.",
                SerializerOptions = ScenarioJson.Options,
            });
    }

    /// <summary>Gets how long each call waits before it answers.</summary>
    public TimeSpan Latency { get; }

    /// <summary>Gets <c>get_weather(city)</c>.</summary>
    public AIFunction GetWeather { get; }

    /// <summary>Gets <c>get_local_time(timeZone)</c>.</summary>
    public AIFunction GetLocalTime { get; }

    /// <summary>Gets the simulated weather for a city; the same city always gets the same weather.</summary>
    public static WeatherReport SimulateWeather(string city)
    {
        var name = string.IsNullOrWhiteSpace(city) ? "Unknown" : city.Trim();
        var hash = Hash(name);
        var conditions = WeatherConditions[hash % (uint)WeatherConditions.Length];
        var temperature = (int)(hash / 7 % 36) - 4;
        if (conditions == "Snow")
            temperature = Math.Min(temperature, 1);
        var humidity = 35 + (int)(hash / 13 % 60);
        var wind = conditions == "Windy" ? 38 + (int)(hash / 17 % 20) : 3 + (int)(hash / 17 % 30);
        return new WeatherReport
        {
            City = name,
            Temperature = temperature,
            FeelsLike = temperature - wind / 8 + (humidity > 75 ? 1 : 0),
            Conditions = conditions,
            Humidity = humidity,
            WindSpeed = wind,
        };
    }

    private async Task<WeatherReport> GetWeatherAsync(
        [System.ComponentModel.Description("The city, such as Paris or Seattle.")] string city,
        CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        return SimulateWeather(city);
    }

    private async Task<LocalTimeReport> GetLocalTimeAsync(
        [System.ComponentModel.Description("The IANA time zone, such as Europe/Paris.")] string timeZone,
        CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone))
            return new LocalTimeReport { TimeZone = timeZone, Error = $"Unknown time zone '{timeZone}'." };

        var local = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone);
        var sign = local.Offset < TimeSpan.Zero ? "-" : "+";
        return new LocalTimeReport
        {
            TimeZone = zone.Id,
            LocalTime = local.ToString("dddd d MMMM, HH:mm", CultureInfo.InvariantCulture),
            UtcOffset = $"UTC{sign}{local.Offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)}",
        };
    }

    private Task DelayAsync(CancellationToken cancellationToken) =>
        Latency > TimeSpan.Zero ? Task.Delay(Latency, cancellationToken) : Task.CompletedTask;

    // FNV-1a: stable across runs and platforms, unlike string.GetHashCode.
    private static uint Hash(string text)
    {
        var hash = 2166136261;
        foreach (var character in text.ToUpperInvariant())
        {
            hash ^= character;
            hash *= 16777619;
        }

        return hash;
    }
}

/// <summary>The result of <c>get_weather</c>.</summary>
public sealed record WeatherReport
{
    [JsonPropertyName("city")]
    public string City { get; init; } = string.Empty;

    /// <summary>Gets the temperature in degrees Celsius.</summary>
    [JsonPropertyName("temperature")]
    public int Temperature { get; init; }

    /// <summary>Gets the temperature it feels like, in degrees Celsius.</summary>
    [JsonPropertyName("feelsLike")]
    public int FeelsLike { get; init; }

    [JsonPropertyName("conditions")]
    public string Conditions { get; init; } = string.Empty;

    /// <summary>Gets the relative humidity in percent.</summary>
    [JsonPropertyName("humidity")]
    public int Humidity { get; init; }

    /// <summary>Gets the wind speed in kilometres per hour.</summary>
    [JsonPropertyName("windSpeed")]
    public int WindSpeed { get; init; }
}

/// <summary>The result of <c>get_local_time</c>.</summary>
public sealed record LocalTimeReport
{
    [JsonPropertyName("timeZone")]
    public string TimeZone { get; init; } = string.Empty;

    [JsonPropertyName("localTime")]
    public string? LocalTime { get; init; }

    [JsonPropertyName("utcOffset")]
    public string? UtcOffset { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>
/// A typed card for <c>get_weather</c>: the generator writes its handler, which fills <see cref="City"/> from the
/// call and the other properties from the result, and <c>options.AddGeneratedToolBlocks()</c> registers it.
/// </summary>
[ToolBlock("get_weather")]
public partial class WeatherToolBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "city")]
    public string? City { get; set; }

    [ToolResult(Name = "temperature")]
    public int? Temperature { get; set; }

    [ToolResult(Name = "feelsLike")]
    public int? FeelsLike { get; set; }

    [ToolResult(Name = "conditions")]
    public string? Conditions { get; set; }

    [ToolResult(Name = "humidity")]
    public int? Humidity { get; set; }

    [ToolResult(Name = "windSpeed")]
    public int? WindSpeed { get; set; }
}

/// <summary>Shows weather conditions as emoji.</summary>
public static class WeatherGlyphs
{
    /// <summary>Gets the emoji for a <see cref="WeatherReport.Conditions"/> value.</summary>
    public static string For(string? conditions) => conditions?.ToLowerInvariant() switch
    {
        null or "" => "\u2026",
        var text when text.Contains("thunder", StringComparison.Ordinal) => "\u26C8\uFE0F",
        var text when text.Contains("snow", StringComparison.Ordinal) => "\u2744\uFE0F",
        var text when text.Contains("shower", StringComparison.Ordinal) => "\U0001F327\uFE0F",
        var text when text.Contains("rain", StringComparison.Ordinal) => "\U0001F326\uFE0F",
        // The foggy bridge: the fog emoji itself is a grey blur at card size.
        var text when text.Contains("fog", StringComparison.Ordinal) => "\U0001F301",
        var text when text.Contains("wind", StringComparison.Ordinal) => "\U0001F4A8",
        var text when text.Contains("partly", StringComparison.Ordinal) => "\u26C5",
        var text when text.Contains("cloud", StringComparison.Ordinal) => "\u2601\uFE0F",
        _ => "\u2600\uFE0F",
    };
}
