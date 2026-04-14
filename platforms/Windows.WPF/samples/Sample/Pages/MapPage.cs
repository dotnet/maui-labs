using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class MapPage : ContentPage
{
    public MapPage()
    {
        Title = "Map";

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Padding = new Thickness(24),
                Children =
                {
                    new Label
                    {
                        Text = "🗺️ Map",
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold,
                    },
                    new Label
                    {
                        Text = "Native MapView is not available on WPF.\nUse Microsoft.Maui.Controls.Maps or a WebView with an embedded map service.",
                        FontSize = 14,
                        TextColor = Colors.Gray,
                    },
                    new Button
                    {
                        Text = "Open Bing Maps in Browser",
                        Command = new Command(async () =>
                        {
                            try
                            {
                                await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync("https://www.bing.com/maps");
                            }
                            catch { }
                        }),
                    },
                },
            },
        };
    }
}
