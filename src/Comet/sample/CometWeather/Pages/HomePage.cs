using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiImage = Microsoft.Maui.Controls.Image;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;
using SolidColorBrush = Microsoft.Maui.Controls.SolidColorBrush;

namespace CometWeather.Pages;

public class HomePageState { }

public class HomePage : Component<HomePageState>
{
    public HomePage()
    {
        WeatherPreferences.SettingsChanged += () => SetState(s => { });
    }

    public override View Render()
    {
        var bg = WeatherPreferences.Background;
        var cardBg = WeatherPreferences.CardBg;
        var textPrimary = WeatherPreferences.TextPrimary;
        var textSecondary = WeatherPreferences.TextSecondary;
        var accent = WeatherPreferences.Accent;

        var root = new MauiGrid { BackgroundColor = bg };

        var scroll = new MauiScrollView
        {
            BackgroundColor = bg,
            Content = BuildContent(bg, cardBg, textPrimary, textSecondary, accent)
        };

        root.Add(scroll);
        return new MauiViewHost(root);
    }

    Microsoft.Maui.Controls.VerticalStackLayout BuildContent(
        Color bg, Color cardBg, Color textPrimary, Color textSecondary, Color accent)
    {
        var stack = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(0, 0, 0, 20),
            BackgroundColor = bg,
        };

        stack.Add(BuildCurrentWeather(bg, textPrimary, accent));
        stack.Add(BuildNext24Hours(bg, cardBg, textPrimary, textSecondary));
        stack.Add(BuildDailyForecast(bg, cardBg, textPrimary, textSecondary, accent));
        stack.Add(BuildMetrics(bg, cardBg, textPrimary, textSecondary));

        return stack;
    }

    Microsoft.Maui.Controls.View BuildCurrentWeather(Color bg, Color textPrimary, Color accent)
    {
        var grid = new MauiGrid
        {
            BackgroundColor = bg,
            Padding = new Thickness(20, 60, 20, 20),
            RowSpacing = 8,
        };
        grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = GridLength.Auto });

        var locationLabel = new MauiLabel
        {
            Text = "Redmond, WA",
            TextColor = textPrimary,
            FontSize = 22,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        };
        MauiGrid.SetRow(locationLabel, 0);
        grid.Add(locationLabel);

        var weatherIcon = new MauiImage
        {
            Source = "fluent_weather_sunny_high_20_filled.png",
            HeightRequest = 120,
            WidthRequest = 120,
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
            Aspect = Aspect.AspectFit,
        };
        MauiGrid.SetRow(weatherIcon, 1);
        grid.Add(weatherIcon);

        var tempLabel = new MauiLabel
        {
            Text = WeatherPreferences.FormatTemperature(70),
            TextColor = textPrimary,
            FontSize = 64,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        };
        MauiGrid.SetRow(tempLabel, 2);
        grid.Add(tempLabel);

        var conditionBorder = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            BackgroundColor = accent,
            StrokeThickness = 0,
            Padding = new Thickness(16, 6),
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
            Content = new MauiLabel
            {
                Text = "Mostly Sunny",
                TextColor = Colors.White,
                FontSize = 14,
            }
        };
        MauiGrid.SetRow(conditionBorder, 3);
        grid.Add(conditionBorder);

        return grid;
    }

    Microsoft.Maui.Controls.View BuildNext24Hours(Color bg, Color cardBg, Color textPrimary, Color textSecondary)
    {
        var outer = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(0, 16),
            BackgroundColor = bg,
        };

        outer.Add(new MauiLabel
        {
            Text = "Next 24 Hours",
            TextColor = textPrimary,
            FontSize = 16,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
            Margin = new Thickness(20, 0, 20, 0),
        });

        var hourlyLayout = new Microsoft.Maui.Controls.HorizontalStackLayout
        {
            Spacing = 12,
        };
        // Leading spacer for content inset (scroll goes edge-to-edge)
        hourlyLayout.Add(new MauiBoxView { WidthRequest = 8, HeightRequest = 1, Color = Colors.Transparent });
        foreach (var h in WeatherData.Hours)
        {
            hourlyLayout.Add(BuildHourlyItem(h, cardBg, textPrimary, textSecondary));
        }
        // Trailing spacer for content inset
        hourlyLayout.Add(new MauiBoxView { WidthRequest = 8, HeightRequest = 1, Color = Colors.Transparent });

        outer.Add(new MauiScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = hourlyLayout,
        });

        return outer;
    }

    Microsoft.Maui.Controls.View BuildHourlyItem(Forecast h, Color cardBg, Color textPrimary, Color textSecondary)
    {
        var card = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = cardBg,
            StrokeThickness = 0,
            Padding = new Thickness(10, 12),
            WidthRequest = 70,
        };

        var stack = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 6, HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center };
        stack.Add(new MauiLabel
        {
            Text = h.DateTime.ToString("h tt"),
            TextColor = textSecondary,
            FontSize = 11,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        });
        stack.Add(new MauiImage
        {
            Source = $"{h.Day.Phrase}.png",
            HeightRequest = 28,
            WidthRequest = 28,
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
            Aspect = Aspect.AspectFit,
        });
        stack.Add(new MauiLabel
        {
            Text = WeatherPreferences.FormatTemperatureShort(h.Temperature.Minimum.Value),
            TextColor = textPrimary,
            FontSize = 13,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        });
        card.Content = stack;
        return card;
    }

    Microsoft.Maui.Controls.View BuildDailyForecast(
        Color bg, Color cardBg, Color textPrimary, Color textSecondary, Color accent)
    {
        var outer = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(0, 16),
            BackgroundColor = bg,
        };

        outer.Add(new MauiLabel
        {
            Text = "Daily Forecast",
            TextColor = textPrimary,
            FontSize = 16,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
            Margin = new Thickness(20, 0, 20, 0),
        });

        var dailyLayout = new Microsoft.Maui.Controls.HorizontalStackLayout
        {
            Spacing = 12,
        };
        // Leading spacer for content inset (scroll goes edge-to-edge)
        dailyLayout.Add(new MauiBoxView { WidthRequest = 8, HeightRequest = 1, Color = Colors.Transparent });
        foreach (var d in WeatherData.Week)
        {
            dailyLayout.Add(BuildDailyItem(d, cardBg, textPrimary, textSecondary, accent));
        }
        // Trailing spacer for content inset
        dailyLayout.Add(new MauiBoxView { WidthRequest = 8, HeightRequest = 1, Color = Colors.Transparent });

        outer.Add(new MauiScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = dailyLayout,
        });

        return outer;
    }

    Microsoft.Maui.Controls.View BuildDailyItem(
        Forecast d, Color cardBg, Color textPrimary, Color textSecondary, Color accent)
    {
        var card = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = cardBg,
            StrokeThickness = 0,
            Padding = new Thickness(10, 12),
            WidthRequest = 80,
        };

        var stack = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 6, HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center };

        stack.Add(new MauiLabel
        {
            Text = d.DateTime.ToString("ddd"),
            TextColor = textSecondary,
            FontSize = 11,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        });
        stack.Add(new MauiImage
        {
            Source = $"{d.Day.Phrase}.png",
            HeightRequest = 28,
            WidthRequest = 28,
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
            Aspect = Aspect.AspectFit,
        });
        stack.Add(new MauiLabel
        {
            Text = WeatherPreferences.FormatTemperatureShort(d.Temperature.Maximum.Value),
            TextColor = textPrimary,
            FontSize = 13,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        });

        var barHeight = Math.Max(4, (d.Temperature.Maximum.Value - d.Temperature.Minimum.Value) * 2);
        stack.Add(new MauiBoxView
        {
            Color = accent,
            WidthRequest = 6,
            HeightRequest = barHeight,
            CornerRadius = 3,
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
        });

        stack.Add(new MauiLabel
        {
            Text = WeatherPreferences.FormatTemperatureShort(d.Temperature.Minimum.Value),
            TextColor = textSecondary,
            FontSize = 11,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        });

        card.Content = stack;
        return card;
    }

    Microsoft.Maui.Controls.View BuildMetrics(Color bg, Color cardBg, Color textPrimary, Color textSecondary)
    {
        var grid = new MauiGrid
        {
            Padding = new Thickness(20, 16),
            ColumnSpacing = 12,
            RowSpacing = 12,
            BackgroundColor = bg,
        };
        grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var metrics = WeatherData.Metrics;
        int rows = (int)Math.Ceiling(metrics.Count / 3.0);
        for (int r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < metrics.Count; i++)
        {
            var card = BuildMetricCard(metrics[i], cardBg, textPrimary, textSecondary);
            MauiGrid.SetColumn(card, i % 3);
            MauiGrid.SetRow(card, i / 3);
            grid.Add(card);
        }

        return grid;
    }

    Microsoft.Maui.Controls.View BuildMetricCard(Metric m, Color cardBg, Color textPrimary, Color textSecondary)
    {
        var card = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            BackgroundColor = cardBg,
            StrokeThickness = 0,
            Padding = new Thickness(12),
            HeightRequest = 120,
        };

        var stack = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 4 };
        stack.Add(new MauiImage
        {
            Source = $"{m.Icon}.png",
            HeightRequest = 24,
            WidthRequest = 24,
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Start,
            Aspect = Aspect.AspectFit,
        });
        stack.Add(new MauiLabel
        {
            Text = m.Value,
            TextColor = textPrimary,
            FontSize = 20,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
        });
        stack.Add(new MauiLabel
        {
            Text = m.Title,
            TextColor = textSecondary,
            FontSize = 11,
        });
        stack.Add(new MauiLabel
        {
            Text = m.WeatherStation,
            TextColor = textSecondary,
            FontSize = 10,
        });

        card.Content = stack;
        return card;
    }
}
