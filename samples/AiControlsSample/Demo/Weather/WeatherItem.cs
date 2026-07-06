namespace AiControlsSample;

/// <summary>
/// One city's weather within a <see cref="WeatherCollectionBlock"/>. Created (pending) when the
/// <c>GetCurrentWeather</c> call arrives and filled in when its result streams back.
/// </summary>
public sealed class WeatherItem
{
    public required string CallId { get; init; }

    /// <summary>The requested city (from the tool call argument), known before the result arrives.</summary>
    public string? City { get; set; }

    /// <summary><see langword="true"/> once the tool result has been applied.</summary>
    public bool HasResult { get; set; }

    // Result fields (from the GetCurrentWeather JSON).
    public string? Location { get; set; }
    public int Temperature { get; set; }
    public string? Conditions { get; set; }
    public string? ConditionIcon { get; set; }
    public int Humidity { get; set; }
    public int WindSpeed { get; set; }
    public int FeelsLike { get; set; }
}
