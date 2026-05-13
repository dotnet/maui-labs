using System.ComponentModel;
using System.Data;
using Microsoft.Extensions.AI;

namespace AI.Sample;

/// <summary>
/// Sample tools the AI agent can invoke during chat.
/// </summary>
public class SampleTools
{
    /// <summary>Creates AIFunction definitions for all sample tools.</summary>
    public IList<AITool> GetTools() =>
    [
        AIFunctionFactory.Create(GetCurrentWeather),
        AIFunctionFactory.Create(Calculate),
        AIFunctionFactory.Create(GetRandomFact),
        AIFunctionFactory.Create(GetAppInfo),
    ];

    [Description("Get the current weather for a city. Returns temperature, condition, humidity, wind speed, and feels-like temperature as JSON.")]
    private static string GetCurrentWeather(
        [Description("City name, e.g. 'Seattle' or 'London'")] string city)
    {
        var random = new Random(city.GetHashCode());
        var temp = random.Next(5, 35);
        var conditions = new[] { "Sunny", "Partly Cloudy", "Overcast", "Rainy", "Windy", "Snowy" };
        var condition = conditions[random.Next(conditions.Length)];
        var icons = new[] { "☀️", "⛅", "☁️", "🌧️", "💨", "❄️" };
        var icon = icons[Array.IndexOf(conditions, condition)];
        var humidity = random.Next(30, 90);
        var wind = random.Next(5, 40);
        var feelsLike = temp + random.Next(-3, 4);

        return $$"""
            {
                "location": "{{city}}",
                "temperature": {{temp}},
                "temperatureFahrenheit": {{temp * 9.0 / 5 + 32:F0}},
                "conditions": "{{condition}}",
                "conditionIcon": "{{icon}}",
                "humidity": {{humidity}},
                "windSpeed": {{wind}},
                "feelsLike": {{feelsLike}}
            }
            """;
    }

    [Description("Evaluate a math expression. Supports basic arithmetic (+, -, *, /).")]
    private static string Calculate(
        [Description("Math expression to evaluate, e.g. '(42 * 3) + 7'")] string expression)
    {
        try
        {
            var result = new DataTable().Compute(expression, null);
            return $"{expression} = {result}";
        }
        catch (Exception ex)
        {
            return $"Error evaluating '{expression}': {ex.Message}";
        }
    }

    [Description("Get a random fun fact about technology, science, or programming.")]
    private static string GetRandomFact()
    {
        var facts = new[]
        {
            "The first computer bug was an actual moth found in a Harvard Mark II computer in 1947.",
            ".NET MAUI supports iOS, Android, macOS, and Windows from a single codebase.",
            "The term 'debugging' was popularized by Grace Hopper after finding the moth.",
            "C# was originally called 'Cool' (C-like Object Oriented Language).",
            "The first version of .NET was released in 2002.",
            "XAML was first introduced with WPF in 2006.",
            "The average person blinks about 15-20 times per minute.",
            "Honey never spoils — archaeologists found 3000-year-old honey that was still edible.",
        };
        return facts[Random.Shared.Next(facts.Length)];
    }

    [Description("Get information about this app — name, version, and platform.")]
    private static string GetAppInfo()
    {
        return $"App: {AppInfo.Name}, Version: {AppInfo.VersionString}, Platform: {DeviceInfo.Platform} ({DeviceInfo.Idiom})";
    }
}
