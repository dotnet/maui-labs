using Microsoft.AspNetCore.Components.AI;
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
                    HeightRequest = 200,
                    Aspect = Aspect.AspectFit,
                    Margin = new Thickness(0, 4),
                };

                // DataContent.Uri is always a valid URI string (may be data: URI)
                if (!string.IsNullOrEmpty(item.Uri))
                {
                    image.Source = ImageSource.FromUri(new Uri(item.Uri));
                }

                _layout.Children.Add(image);
            }
            else
            {
                // Non-image media — show as a label
                _layout.Children.Add(new Label
                {
                    Text = $"📎 {item.MediaType ?? "file"} ({item.Data.Length} bytes)",
                    TextColor = Colors.Gray,
                    FontSize = 11,
                });
            }
        }
    }
}
