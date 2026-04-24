using Microsoft.Maui.Controls;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiBorder = Microsoft.Maui.Controls.Border;

namespace CometFeatureShowcase.Pages;

public class DataPageState { }

public class DataPage : Component<DataPageState>
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
            Text = "ValueConverters Demo",
            TextColor = Colors.Black,
            FontSize = 24,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
            Margin = new Thickness(0, 8, 0, 0),
        });

        content.Add(new MauiLabel
        {
            Text = "Demonstrating various converter helpers",
            TextColor = Colors.DarkGray,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12),
        });

        // Currency Converter
        content.Add(BuildConverterCard(
            "Currency Format",
            "Price: $1234.56",
            "Formats numbers as currency"));

        // Date/Time Converter
        content.Add(BuildConverterCard(
            "Date/Time Format",
            $"Today: {DateTime.Now:MMM dd, yyyy}",
            "Formats dates and times"));

        // Ordinal Converter
        content.Add(BuildConverterCard(
            "Ordinal Numbers",
            "Position: 1st, 2nd, 3rd, 21st, 22nd, 23rd, etc.",
            "Converts numbers to ordinal form"));

        // Pluralize Converter
        content.Add(BuildConverterCard(
            "Pluralize",
            "1 item / 5 items / 0 items",
            "Handles singular/plural text"));

        // Abbreviate Converter
        content.Add(BuildConverterCard(
            "Abbreviate",
            "1,234,567 → 1.23M",
            "Shortens large numbers"));

        // Boolean to Visibility
        content.Add(BuildConverterCard(
            "Boolean Toggle",
            "Show/Hide: Toggle boolean values",
            "Converts booleans to visibility"));

        // String Manipulation
        content.Add(BuildConverterCard(
            "String Case",
            "UPPERCASE / lowercase / Title Case",
            "Converts string cases"));

        var scroll = new Microsoft.Maui.Controls.ScrollView
        {
            BackgroundColor = Color.FromArgb("#F5F5F5"),
            Content = content,
        };

        root.Add(scroll);
        return new MauiViewHost(root);
    }

    Microsoft.Maui.Controls.View BuildConverterCard(string title, string example, string description)
    {
        var card = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
            BackgroundColor = Colors.White,
            StrokeThickness = 1,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 4),
        };

        var stack = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 4,
        };

        stack.Add(new MauiLabel
        {
            Text = title,
            TextColor = Color.FromArgb("#2196F3"),
            FontSize = 14,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
        });

        stack.Add(new MauiLabel
        {
            Text = example,
            TextColor = Colors.Black,
            FontSize = 13,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
        });

        stack.Add(new MauiLabel
        {
            Text = description,
            TextColor = Colors.Gray,
            FontSize = 11,
        });

        card.Content = stack;
        return card;
    }
}
