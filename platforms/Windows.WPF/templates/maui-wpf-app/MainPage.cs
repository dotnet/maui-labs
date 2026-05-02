namespace MauiWpfApp;

public class MainPage : Microsoft.Maui.Controls.ContentPage
{
    public MainPage()
    {
        Content = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 25,
            Padding = new Microsoft.Maui.Thickness(30, 0),
            VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
            Children =
            {
                new Microsoft.Maui.Controls.Label
                {
                    Text = "Hello, .NET MAUI on WPF!",
                    HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
                    FontSize = 32
                },
                new Microsoft.Maui.Controls.Label
                {
                    Text = "Welcome to your first MAUI WPF app.",
                    HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
                    FontSize = 18
                }
            }
        };
    }
}
