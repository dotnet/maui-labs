using System.Globalization;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shows a <see cref="WeatherToolBlock"/>: the city while the tool runs, then its weather.</summary>
public partial class WeatherCardView : ScenarioCardView
{
    public WeatherCardView() => InitializeComponent();

    protected override void Refresh()
    {
        if (CityLabel is null || Block is not WeatherToolBlock weather)
            return;

        var city = string.IsNullOrWhiteSpace(weather.City) ? "\u2026" : weather.City;
        CityLabel.Text = city;
        var hasWeather = weather.HasResult && weather.Temperature is not null;
        var running = !weather.HasResult && IsRunning(weather);

        ResultRow.IsVisible = hasWeather;
        PendingRow.IsVisible = !hasWeather;
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = running;
        GlyphLabel.Text = hasWeather ? WeatherGlyphs.For(weather.Conditions) : "\U0001F321\uFE0F";
        GlyphLabel.Opacity = hasWeather ? 1 : 0.6;

        if (hasWeather)
        {
            TemperatureLabel.Text = $"{weather.Temperature}\u00B0C";
            ConditionsLabel.Text = weather.Conditions;
            // A line per fact, so a phone-width card never wraps in the middle of one.
            DetailsLabel.Text = string.Format(CultureInfo.CurrentCulture,
                "Feels like {0}\u00B0\nHumidity {1}%\nWind {2} km/h", weather.FeelsLike, weather.Humidity, weather.WindSpeed);
            SemanticProperties.SetDescription(Card,
                $"Weather in {city}: {weather.Temperature} degrees, {weather.Conditions}. " +
                DetailsLabel.Text.Replace('\n', ',') + ".");
            return;
        }

        PendingLabel.Text = weather.HasResult ? $"Could not get the weather: {ScenarioJson.AsString(weather.Result?.Result)}"
            : running ? $"Checking the weather in {city}\u2026"
            : IsStopped(weather) ? "Stopped before the weather arrived."
            : "No weather was returned.";
        SemanticProperties.SetDescription(Card, $"Weather in {city}: {PendingLabel.Text}");
    }
}
