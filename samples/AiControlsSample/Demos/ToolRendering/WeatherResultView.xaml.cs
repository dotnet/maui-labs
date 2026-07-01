using Microsoft.Maui.AI.Chat;
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
        // The GetCurrentWeather tool returns a JSON object as its result.
        // Parse it directly from the FunctionInvocationContentBlock and render a rich card.
        if (ContentContext?.Block is FunctionInvocationContentBlock ficb && ficb.Result is { } resultContent)
        {
            if (TryParseWeatherJson(resultContent.Result?.ToString()))
                return;
        }

        // Nothing to render yet (call in flight or no result).
        CityLabel.Text = "…";
    }

    private bool TryParseWeatherJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            CityLabel.Text = root.TryGetProperty("location", out var loc) ? loc.GetString() : "Unknown";
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
