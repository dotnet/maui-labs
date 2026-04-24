using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiImage = Microsoft.Maui.Controls.Image;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiButton = Microsoft.Maui.Controls.Button;
using SolidColorBrush = Microsoft.Maui.Controls.SolidColorBrush;

namespace CometWeather.Pages;

public class SettingsPageState { }

public class SettingsPage : Component<SettingsPageState>
{
    public SettingsPage()
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
        var divider = WeatherPreferences.DividerColor;

        var root = new MauiGrid { BackgroundColor = bg };

        var scroll = new MauiScrollView
        {
            BackgroundColor = bg,
            Content = BuildContent(bg, cardBg, textPrimary, textSecondary, accent, divider)
        };

        root.Add(scroll);
        return new MauiViewHost(root);
    }

    Microsoft.Maui.Controls.VerticalStackLayout BuildContent(
        Color bg, Color cardBg, Color textPrimary, Color textSecondary, Color accent, Color divider)
    {
        var stack = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(20, 60, 20, 40),
            BackgroundColor = bg,
        };

        stack.Add(BuildProfileHeader(cardBg, textPrimary, textSecondary, accent));
        stack.Add(new Microsoft.Maui.Controls.BoxView { HeightRequest = 24, BackgroundColor = Colors.Transparent });
        stack.Add(BuildSectionLabel("Units", textSecondary));
        stack.Add(new Microsoft.Maui.Controls.BoxView { HeightRequest = 8, BackgroundColor = Colors.Transparent });
        stack.Add(BuildUnitsSection(cardBg, textPrimary, divider));
        stack.Add(new Microsoft.Maui.Controls.BoxView { HeightRequest = 24, BackgroundColor = Colors.Transparent });
        stack.Add(BuildSectionLabel("Theme", textSecondary));
        stack.Add(new Microsoft.Maui.Controls.BoxView { HeightRequest = 8, BackgroundColor = Colors.Transparent });
        stack.Add(BuildThemeSection(cardBg, textPrimary, divider));
        stack.Add(new Microsoft.Maui.Controls.BoxView { HeightRequest = 32, BackgroundColor = Colors.Transparent });
        stack.Add(BuildSupportLink(accent));

        return stack;
    }

    Microsoft.Maui.Controls.View BuildProfileHeader(
        Color cardBg, Color textPrimary, Color textSecondary, Color accent)
    {
        var card = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            BackgroundColor = cardBg,
            StrokeThickness = 0,
            Padding = new Thickness(16),
        };

        var row = new MauiGrid();
        row.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = GridLength.Star });
        row.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = GridLength.Auto });

        var avatarBorder = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
            BackgroundColor = accent,
            StrokeThickness = 0,
            WidthRequest = 56,
            HeightRequest = 56,
            Content = new MauiLabel
            {
                Text = "JV",
                TextColor = Colors.White,
                FontSize = 18,
                FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
                HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            }
        };
        MauiGrid.SetColumn(avatarBorder, 0);
        row.Add(avatarBorder);

        var nameStack = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 2,
            Margin = new Thickness(12, 0),
            VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
        };
        nameStack.Add(new MauiLabel { Text = "Gerald Versluis", TextColor = textPrimary, FontSize = 15, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });
        nameStack.Add(new MauiLabel { Text = "gerald@microsoft.com", TextColor = textSecondary, FontSize = 12 });
        MauiGrid.SetColumn(nameStack, 1);
        row.Add(nameStack);

        var signOutBtn = new MauiButton
        {
            Text = "Sign Out",
            TextColor = textPrimary,
            BackgroundColor = Colors.Transparent,
            BorderColor = accent,
            BorderWidth = 1,
            CornerRadius = 8,
            FontSize = 12,
            Padding = new Thickness(10, 6),
            VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
        };
        MauiGrid.SetColumn(signOutBtn, 2);
        row.Add(signOutBtn);

        card.Content = row;
        return card;
    }

    MauiLabel BuildSectionLabel(string text, Color textSecondary) =>
        new MauiLabel { Text = text, TextColor = textSecondary, FontSize = 13, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold };

    Microsoft.Maui.Controls.View BuildUnitsSection(Color cardBg, Color textPrimary, Color divider)
    {
        var selected = WeatherPreferences.CurrentUnit;
        var card = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            BackgroundColor = cardBg,
            StrokeThickness = 0,
        };

        var stack = new Microsoft.Maui.Controls.VerticalStackLayout();
        stack.Add(BuildOptionRow("Imperial", selected == TemperatureUnit.Imperial, textPrimary,
            () => WeatherPreferences.SetUnit(TemperatureUnit.Imperial)));
        stack.Add(BuildDivider(divider));
        stack.Add(BuildOptionRow("Metric", selected == TemperatureUnit.Metric, textPrimary,
            () => WeatherPreferences.SetUnit(TemperatureUnit.Metric)));
        stack.Add(BuildDivider(divider));
        stack.Add(BuildOptionRow("Hybrid", selected == TemperatureUnit.Hybrid, textPrimary,
            () => WeatherPreferences.SetUnit(TemperatureUnit.Hybrid)));

        card.Content = stack;
        return card;
    }

    Microsoft.Maui.Controls.View BuildThemeSection(Color cardBg, Color textPrimary, Color divider)
    {
        var selected = WeatherPreferences.CurrentTheme;
        var card = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            BackgroundColor = cardBg,
            StrokeThickness = 0,
        };

        var stack = new Microsoft.Maui.Controls.VerticalStackLayout();
        stack.Add(BuildOptionRow("Default", selected == ThemeMode.Default, textPrimary,
            () => WeatherPreferences.SetTheme(ThemeMode.Default)));
        stack.Add(BuildDivider(divider));
        stack.Add(BuildOptionRow("Dark", selected == ThemeMode.Dark, textPrimary,
            () => WeatherPreferences.SetTheme(ThemeMode.Dark)));
        stack.Add(BuildDivider(divider));
        stack.Add(BuildOptionRow("Light", selected == ThemeMode.Light, textPrimary,
            () => WeatherPreferences.SetTheme(ThemeMode.Light)));

        card.Content = stack;
        return card;
    }

    Microsoft.Maui.Controls.View BuildOptionRow(
        string label, bool isSelected, Color textPrimary, Action onSelect)
    {
        var row = new MauiGrid
        {
            Padding = new Thickness(16, 14),
        };
        row.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = GridLength.Star });
        row.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = GridLength.Auto });

        var lbl = new MauiLabel { Text = label, TextColor = textPrimary, FontSize = 15 };
        MauiGrid.SetColumn(lbl, 0);
        row.Add(lbl);

        var checkImg = new MauiImage
        {
            Source = "checkmark_icon.png",
            HeightRequest = 18,
            WidthRequest = 18,
            IsVisible = isSelected,
            VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
            Aspect = Aspect.AspectFit,
        };
        MauiGrid.SetColumn(checkImg, 1);
        row.Add(checkImg);

        var tapGesture = new Microsoft.Maui.Controls.TapGestureRecognizer();
        tapGesture.Tapped += (s, e) => onSelect();
        row.GestureRecognizers.Add(tapGesture);

        return row;
    }

    Microsoft.Maui.Controls.View BuildDivider(Color divider) =>
        new Microsoft.Maui.Controls.BoxView { HeightRequest = 1, BackgroundColor = divider, Margin = new Thickness(16, 0) };

    Microsoft.Maui.Controls.View BuildSupportLink(Color accent)
    {
        var lbl = new MauiLabel
        {
            Text = "Support",
            TextColor = accent,
            FontSize = 15,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
        };
        var tap = new Microsoft.Maui.Controls.TapGestureRecognizer();
        tap.Tapped += (s, e) => { /* open support URL */ };
        lbl.GestureRecognizers.Add(tap);
        return lbl;
    }
}
