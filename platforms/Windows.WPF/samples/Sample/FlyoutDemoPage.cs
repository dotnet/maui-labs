using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample;

public class FlyoutDemoPage : FlyoutPage
{
    public FlyoutDemoPage()
    {
        Title = "FlyoutPage Demo";
        FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split;

        // Build the flyout menu
        var menuItems = new List<(string Title, string Tag, string Description, string Color)>
        {
            ("Home", "home", "Welcome home! Browse your content here.", "#4A90E2"),
            ("Settings", "settings", "Configure your preferences and options.", "#7B68EE"),
            ("About", "about", "MAUI WPF FlyoutPage demo.", "#2ECC71"),
            ("Notifications", "notifications", "Stay up to date with alerts and messages.", "#F39C12"),
            ("Profile", "profile", "View and edit your profile information.", "#1ABC9C"),
        };

        var listView = new ListView
        {
            ItemsSource = menuItems.Select(m => m.Title).ToList(),
        };

        listView.ItemSelected += (s, e) =>
        {
            if (e.SelectedItem is string title)
            {
                var item = menuItems.FirstOrDefault(m => m.Title == title);
                Detail = new NavigationPage(CreateDetailPage(item.Title, item.Description, item.Color));
            }
        };

        Flyout = new ContentPage
        {
            Title = "Menu",
            Content = listView,
        };

        Detail = new NavigationPage(CreateDetailPage("Home", "Welcome home! Browse your content here.", "#4A90E2"));
    }

    static ContentPage CreateDetailPage(string title, string description, string accentColor)
    {
        var page = new ContentPage
        {
            Title = title,
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(40),
                Spacing = 20,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Color.FromArgb(accentColor),
                        HeightRequest = 4,
                        WidthRequest = 200,
                        HorizontalOptions = LayoutOptions.Center,
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
                    },
                    new Label
                    {
                        Text = title,
                        FontSize = 32,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                    }.WithPrimaryText(),
                    new Label
                    {
                        Text = description,
                        FontSize = 18,
                        HorizontalTextAlignment = TextAlignment.Center,
                    }.WithSecondaryText(),
                },
            },
        };
        return page.WithPageBackground();
    }
}
