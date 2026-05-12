using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace CopilotChat.Sample;

/// <summary>
/// Sample tools the Copilot can invoke during chat.
/// </summary>
public class SampleTools
{
    /// <summary>Creates AIFunction definitions for all sample tools.</summary>
    public IList<AITool> GetTools() =>
    [
        AIFunctionFactory.Create(GetCurrentWeather),
        AIFunctionFactory.Create(Calculate),
        AIFunctionFactory.Create(GetRandomFact),
        AIFunctionFactory.Create(NavigateToPage),
        AIFunctionFactory.Create(GetAppInfo),
    ];

    [Description("Get the current weather for a city. Returns temperature, condition, and humidity.")]
    private static string GetCurrentWeather(
        [Description("City name, e.g. 'Seattle' or 'London'")] string city)
    {
        // Simulated weather data
        var random = new Random(city.GetHashCode());
        var temp = random.Next(5, 35);
        var conditions = new[] { "Sunny", "Partly Cloudy", "Overcast", "Rainy", "Windy", "Snowy" };
        var condition = conditions[random.Next(conditions.Length)];
        var humidity = random.Next(30, 90);
        return $"Weather in {city}: {temp}°C, {condition}, Humidity: {humidity}%";
    }

    [Description("Evaluate a math expression. Supports basic arithmetic (+, -, *, /, ^, sqrt).")]
    private static string Calculate(
        [Description("Math expression to evaluate, e.g. '(42 * 3) + 7'")] string expression)
    {
        try
        {
            var result = new System.Data.DataTable().Compute(expression, null);
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

    [Description("Navigate to a page in the app. Available pages: home, chat, settings.")]
    private static string NavigateToPage(
        [Description("Page to navigate to: 'home', 'chat', or 'settings'")] string page)
    {
        var route = page.ToLowerInvariant() switch
        {
            "home" => "//home",
            "chat" => "//chat",
            "settings" => "//settings",
            _ => null,
        };

        if (route is null)
            return $"Unknown page '{page}'. Available: home, chat, settings.";

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Shell.Current.GoToAsync(route);
        });

        return $"Navigated to {page} page.";
    }

    [Description("Get information about this app — name, version, and platform.")]
    private static string GetAppInfo()
    {
        return $"App: {AppInfo.Name}, Version: {AppInfo.VersionString}, Platform: {DeviceInfo.Platform} ({DeviceInfo.Idiom})";
    }
}
