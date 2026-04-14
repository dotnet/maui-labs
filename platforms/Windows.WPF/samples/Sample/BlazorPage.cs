#if WPFAPP

namespace Sample;

public class BlazorPage : ContentPage
{
    public BlazorPage()
    {
        Title = "Blazor WebView";
        this.WithPageBackground();

        var backButton = new Button
        {
            Text = "← Back",
            BackgroundColor = AppColors.AccentPink,
            TextColor = Colors.White,
            HeightRequest = 44,
            Margin = new Thickness(10, 10, 10, 0)
        };
        backButton.Clicked += async (s, e) => await Navigation.PopAsync();

        Content = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(20),
            Children =
            {
                backButton,
                new Label
                {
                    Text = "Blazor WebView",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                },
                new Label
                {
                    Text = "Blazor Hybrid is available on WPF via Microsoft.AspNetCore.Components.WebView.Wpf",
                    FontSize = 14,
                    TextColor = Colors.Gray,
                },
            }
        };
    }
}
#endif


