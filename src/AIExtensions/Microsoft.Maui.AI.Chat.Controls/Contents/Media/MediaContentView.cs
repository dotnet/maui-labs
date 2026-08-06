using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Renders <see cref="MediaContentBlock"/> items as images.
/// </summary>
public class MediaContentView : ContentContextView
{
    private VerticalStackLayout? _layout;

    public MediaContentView()
    {
        _layout = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(12, 8) };
        Content = _layout;
    }

    protected override void RefreshFromContentContext()
    {
        if (_layout is null || ContentContext?.Block is not MediaContentBlock mcb)
            return;

        _layout.Children.Clear();

        foreach (var item in mcb.Items)
        {
            if (item.HasTopLevelMediaType("image"))
            {
                var image = new Image
                {
                    HeightRequest = 240,
                    Aspect = Aspect.AspectFit,
                    Margin = new Thickness(0, 4),
                    HorizontalOptions = LayoutOptions.Start,
                };
                SemanticProperties.SetDescription(image, "Image attachment");

                // Render from the raw bytes (efficient for large generated images).
                var bytes = item.Data.ToArray();
                image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));

                _layout.Children.Add(image);
            }
            else
            {
                // Non-image media — show as a label
                var attachmentLabel = new Label
                {
                    Text = $"📎 {item.MediaType ?? "file"} ({item.Data.Length} bytes)",
                    TextColor = Colors.Gray,
                    FontSize = 11,
                };
                SemanticProperties.SetDescription(attachmentLabel, attachmentLabel.Text);
                _layout.Children.Add(attachmentLabel);
            }
        }
    }
}
