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
        // The WeatherToolBlockHandler projects the raw M.E.AI content into a
        // strongly-typed WeatherToolBlock, so the view binds to typed properties.
        if (ContentContext?.Block is WeatherToolBlock weather && weather.HasResult)
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

        // Call in flight (no result yet) — show what we know.
        if (ContentContext?.Block is WeatherToolBlock pending)
        {
            CityLabel.Text = pending.City ?? "…";
        }
    }
}
