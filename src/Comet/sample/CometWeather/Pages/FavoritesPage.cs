using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiImage = Microsoft.Maui.Controls.Image;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiBorder = Microsoft.Maui.Controls.Border;
using SolidColorBrush = Microsoft.Maui.Controls.SolidColorBrush;

namespace CometWeather.Pages;

public class FavoritesPageState { }

public class FavoritesPage : Component<FavoritesPageState>
{
    public FavoritesPage()
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
        root.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var searchBar = BuildSearchHeader(cardBg, textSecondary);
        MauiGrid.SetRow(searchBar, 0);
        root.Add(searchBar);

        var collectionGrid = BuildFavoritesGrid(bg, cardBg, textPrimary, textSecondary);
        MauiGrid.SetRow(collectionGrid, 1);
        root.Add(collectionGrid);

        return new MauiViewHost(root);
    }

    Microsoft.Maui.Controls.View BuildSearchHeader(Color cardBg, Color textSecondary)
    {
        var header = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            BackgroundColor = cardBg,
            StrokeThickness = 0,
            Margin = new Thickness(20, 60, 20, 10),
            Padding = new Thickness(16, 12),
        };

        var row = new Microsoft.Maui.Controls.HorizontalStackLayout { Spacing = 10 };
        row.Add(new MauiImage
        {
            Source = "search_icon.png",
            HeightRequest = 20,
            WidthRequest = 20,
            VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
            Aspect = Aspect.AspectFit,
        });
        row.Add(new MauiLabel
        {
            Text = "Search",
            TextColor = textSecondary,
            FontSize = 16,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        });

        header.Content = row;
        return header;
    }

    Microsoft.Maui.Controls.View BuildFavoritesGrid(Color bg, Color cardBg, Color textPrimary, Color textSecondary)
    {
        var scroll = new MauiScrollView
        {
            BackgroundColor = bg,
        };

        var outerPadding = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Padding = new Thickness(20, 0, 20, 20),
            Spacing = 0,
        };

        var locations = WeatherData.Locations;
        var rows = (int)Math.Ceiling(locations.Count / 2.0);

        var grid = new MauiGrid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            BackgroundColor = bg,
        };
        grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < locations.Count; i++)
        {
            var card = BuildLocationCard(locations[i], cardBg, textPrimary, textSecondary);
            MauiGrid.SetColumn(card, i % 2);
            MauiGrid.SetRow(card, i / 2);
            grid.Add(card);
        }

        outerPadding.Add(grid);
        scroll.Content = outerPadding;
        return scroll;
    }

    Microsoft.Maui.Controls.View BuildLocationCard(Location loc, Color cardBg, Color textPrimary, Color textSecondary)
    {
        var card = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            BackgroundColor = cardBg,
            StrokeThickness = 0,
            Padding = new Thickness(14),
            HeightRequest = 130,
        };

        var stack = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 4 };

        var topRow = new MauiGrid();
        topRow.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = GridLength.Star });
        topRow.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = GridLength.Auto });

        var icon = new MauiImage
        {
            Source = $"{loc.Icon}.png",
            HeightRequest = 32,
            WidthRequest = 32,
            VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Start,
            Aspect = Aspect.AspectFit,
        };
        MauiGrid.SetColumn(icon, 0);
        topRow.Add(icon);

        // Convert the location temperature value (strip the ° and parse)
        var displayValue = loc.Value;
        if (loc.Value.EndsWith("°") && int.TryParse(loc.Value.TrimEnd('°'), out var tempF))
        {
            displayValue = WeatherPreferences.FormatTemperatureShort(tempF);
        }

        var valueLabel = new MauiLabel
        {
            Text = displayValue,
            TextColor = textPrimary,
            FontSize = 22,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.End,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Start,
        };
        MauiGrid.SetColumn(valueLabel, 1);
        topRow.Add(valueLabel);

        stack.Add(topRow);

        stack.Add(new MauiLabel
        {
            Text = loc.Name,
            TextColor = textPrimary,
            FontSize = 14,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
        });

        stack.Add(new MauiLabel
        {
            Text = loc.WeatherStation,
            TextColor = textSecondary,
            FontSize = 11,
        });

        card.Content = stack;
        return card;
    }
}
