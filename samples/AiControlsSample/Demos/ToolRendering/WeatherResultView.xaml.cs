using Microsoft.AspNetCore.Components.AI;
using Microsoft.Maui.AI.Chat.Controls;

namespace AiControlsSample;

public partial class WeatherResultView : ContentContextView
{
    public WeatherResultView()
    {
        InitializeComponent();
    }

    protected override void RefreshFromContentContext()
    {
        // With the source generator, the block is a strongly-typed WeatherToolBlock
        if (ContentContext?.Block is WeatherToolBlock weather)
        {
            // If [ToolResult] properties were deserialized (Result was a JsonElement object)
            if (!string.IsNullOrEmpty(weather.Location) || weather.Temperature != 0)
            {
                CityLabel.Text = weather.Location ?? weather.City ?? "Unknown";
                TempLabel.Text = $"{weather.Temperature}°C";
                ConditionLabel.Text = weather.Conditions ?? "--";
                IconLabel.Text = weather.ConditionIcon ?? "🌡️";
                HumidityLabel.Text = weather.Humidity != 0 ? $"{weather.Humidity}%" : "--";
                WindLabel.Text = weather.WindSpeed != 0 ? $"{weather.WindSpeed} km/h" : "--";
                FeelsLikeLabel.Text = weather.FeelsLike != 0 ? $"{weather.FeelsLike}°C" : "--";
                return;
            }

            // Fallback: Result was a string (not JsonElement) — parse manually
            if (weather.Result is { } resultContent)
            {
                var json = resultContent.Result?.ToString();
                if (TryParseWeatherJson(json, weather.City))
                    return;
            }

            // At minimum, show what we know from the tool parameters
            CityLabel.Text = weather.City ?? "Unknown";
            return;
        }

        // Generic fallback for FunctionInvocationContentBlock (backwards compat)
        if (ContentContext?.Block is FunctionInvocationContentBlock ficb && ficb.Result is { } fallbackResult)
        {
            TryParseWeatherJson(fallbackResult.Result?.ToString(), null);
        }
    }

    private bool TryParseWeatherJson(string? json, string? fallbackCity)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            CityLabel.Text = root.TryGetProperty("location", out var loc) ? loc.GetString() : (fallbackCity ?? "Unknown");
            TempLabel.Text = root.TryGetProperty("temperature", out var temp) ? $"{temp}°C" : "--";
            ConditionLabel.Text = root.TryGetProperty("conditions", out var cond) ? cond.GetString() : "--";
            IconLabel.Text = root.TryGetProperty("conditionIcon", out var icon) ? icon.GetString() : "🌡️";
            HumidityLabel.Text = root.TryGetProperty("humidity", out var hum) ? $"{hum}%" : "--";
            WindLabel.Text = root.TryGetProperty("windSpeed", out var wind) ? $"{wind} km/h" : "--";
            FeelsLikeLabel.Text = root.TryGetProperty("feelsLike", out var feels) ? $"{feels}°C" : "--";
            return true;
        }
        catch
        {
            return false;
        }
    }
}
