using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MauiLinuxApp;

public class MainPage : ContentPage
{
    private int _count;

    public MainPage()
    {
        var label = new Label
        {
            Text = "Hello, MAUI on Linux!",
            FontSize = 32,
            HorizontalOptions = LayoutOptions.Center,
        };

        var countLabel = new Label
        {
            Text = "Click count: 0",
            FontSize = 18,
            HorizontalOptions = LayoutOptions.Center,
        };

        var button = new Button
        {
            Text = "Click me",
            HorizontalOptions = LayoutOptions.Center,
        };

        button.Clicked += (s, e) =>
        {
            _count++;
            countLabel.Text = $"Click count: {_count}";
        };

        Content = new VerticalStackLayout
        {
            Spacing = 25,
            Padding = 30,
            VerticalOptions = LayoutOptions.Center,
            Children = { label, button, countLabel },
        };
    }
}
