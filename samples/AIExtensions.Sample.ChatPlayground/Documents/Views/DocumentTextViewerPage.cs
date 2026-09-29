using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class DocumentTextViewerPage : ContentPage
{
    internal DocumentTextViewerPage(string title, string content)
    {
        Title = title;

        var text = new Label
        {
            Text = content,
            FontFamily = "Courier",
            FontSize = 12,
            LineBreakMode = LineBreakMode.CharacterWrap,
            Padding = new Thickness(12),
        };
        var copy = new Button { Text = "Copy" };
        copy.Clicked += async (_, _) => await Clipboard.Default.SetTextAsync(content);
        var close = new Button { Text = "Close" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var buttons = new HorizontalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(12),
            Children = { copy, close },
        };
        var layout = new Grid
        {
            RowDefinitions =
            {
                new(GridLength.Star),
                new(GridLength.Auto),
            },
        };
        layout.Add(new ScrollView { Content = text });
        layout.Add(buttons, row: 1);
        Content = layout;
    }
}
