using Microsoft.Maui.Controls;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;

namespace CometFeatureShowcase.Pages;

public class AnimationPageState { }

public class AnimationPage : Component<AnimationPageState>
{
    public override Comet.View Render()
    {
        var root = new Microsoft.Maui.Controls.Grid { BackgroundColor = Color.FromArgb("#F5F5F5") };

        var content = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16),
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

        content.Add(new MauiLabel
        {
            Text = "Animation Demo",
            TextColor = Colors.Black,
            FontSize = 24,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
            Margin = new Thickness(0, 8, 0, 0),
        });

        content.Add(new MauiLabel
        {
            Text = "Click buttons to trigger animations",
            TextColor = Colors.DarkGray,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 16),
        });

        // Animation Box
        var animBox = new MauiBoxView
        {
            Color = Color.FromArgb("#2196F3"),
            CornerRadius = 8,
            WidthRequest = 100,
            HeightRequest = 100,
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
            Margin = new Thickness(0, 16, 0, 16),
        };

        content.Add(animBox);

        // FadeIn Button
        var fadeInButton = new Microsoft.Maui.Controls.Button
        {
            Text = "FadeIn Animation",
            BackgroundColor = Color.FromArgb("#4CAF50"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 8, 0, 0),
        };
        fadeInButton.Clicked += async (s, e) =>
        {
            animBox.Opacity = 0;
            await animBox.FadeToAsync(1, 800);
        };
        content.Add(fadeInButton);

        // FadeOut Button
        var fadeOutButton = new Microsoft.Maui.Controls.Button
        {
            Text = "FadeOut Animation",
            BackgroundColor = Color.FromArgb("#FF9800"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 8, 0, 0),
        };
        fadeOutButton.Clicked += async (s, e) =>
        {
            await animBox.FadeToAsync(0, 800);
        };
        content.Add(fadeOutButton);

        // Scale Animation
        var scaleButton = new Microsoft.Maui.Controls.Button
        {
            Text = "Scale Animation",
            BackgroundColor = Color.FromArgb("#9C27B0"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 8, 0, 0),
        };
        scaleButton.Clicked += async (s, e) =>
        {
            await animBox.ScaleToAsync(1.5, 400);
            await animBox.ScaleToAsync(1, 400);
        };
        content.Add(scaleButton);

        // Rotate Animation
        var rotateButton = new Microsoft.Maui.Controls.Button
        {
            Text = "Rotate Animation",
            BackgroundColor = Color.FromArgb("#F44336"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 8, 0, 0),
        };
        rotateButton.Clicked += async (s, e) =>
        {
            await animBox.RotateToAsync(360, 800);
            animBox.Rotation = 0;
        };
        content.Add(rotateButton);

        // Combined Animation
        var comboButton = new Microsoft.Maui.Controls.Button
        {
            Text = "Combined Animation",
            BackgroundColor = Color.FromArgb("#00BCD4"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 8, 0, 24),
        };
        comboButton.Clicked += async (s, e) =>
        {
            await Task.WhenAll(
                animBox.ScaleToAsync(1.2, 300),
                animBox.RotateToAsync(180, 300)
            );
            await Task.WhenAll(
                animBox.ScaleToAsync(1, 300),
                animBox.RotateToAsync(360, 300)
            );
            animBox.Rotation = 0;
        };
        content.Add(comboButton);

        var scroll = new Microsoft.Maui.Controls.ScrollView
        {
            BackgroundColor = Color.FromArgb("#F5F5F5"),
            Content = content,
        };

        root.Add(scroll);
        return new MauiViewHost(root);
    }
}
