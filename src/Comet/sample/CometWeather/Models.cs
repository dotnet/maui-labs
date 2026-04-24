using System.Text.Json.Serialization;

namespace CometWeather;

public class Minimum
{
    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "";
}

public class Maximum
{
    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "";
}

public class Temperature
{
    [JsonPropertyName("minimum")]
    public Minimum Minimum { get; set; } = new();

    [JsonPropertyName("maximum")]
    public Maximum Maximum { get; set; } = new();
}

public class Day
{
    [JsonPropertyName("phrase")]
    public string Phrase { get; set; } = "";
}

public class Night
{
    [JsonPropertyName("phrase")]
    public string Phrase { get; set; } = "";
}

public class Forecast
{
    [JsonPropertyName("dateTime")]
    public DateTime DateTime { get; set; }

    [JsonPropertyName("temperature")]
    public Temperature Temperature { get; set; } = new();

    [JsonPropertyName("day")]
    public Day Day { get; set; } = new();

    [JsonPropertyName("night")]
    public Night Night { get; set; } = new();
}

public class Metric
{
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string WeatherStation { get; set; } = "";
    public string Value { get; set; } = "";
}

public class Location
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string WeatherStation { get; set; } = "";
    public string Value { get; set; } = "";
}
