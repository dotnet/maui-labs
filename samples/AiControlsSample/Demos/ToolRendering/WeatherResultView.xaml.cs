using System.Text.Json;
using Microsoft.Extensions.AI;
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
        if (ContentContext?.Content is not FunctionResultContent result)
            return;

        var json = result.Result?.ToString();
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            CityLabel.Text = root.TryGetProperty("location", out var loc) ? loc.GetString() : "Unknown";
            TempLabel.Text = root.TryGetProperty("temperature", out var temp) ? $"{temp}°C" : "--";
            ConditionLabel.Text = root.TryGetProperty("conditions", out var cond) ? cond.GetString() : "--";
            IconLabel.Text = root.TryGetProperty("conditionIcon", out var icon) ? icon.GetString() : "🌡️";
            HumidityLabel.Text = root.TryGetProperty("humidity", out var hum) ? $"{hum}%" : "--";
            WindLabel.Text = root.TryGetProperty("windSpeed", out var wind) ? $"{wind} km/h" : "--";
            FeelsLikeLabel.Text = root.TryGetProperty("feelsLike", out var feels) ? $"{feels}°C" : "--";
        }
        catch
        {
            // Ignore parse errors
        }
    }
}
