using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Small helpers shared by the built-in views.</summary>
internal static class ChatVisuals
{
    public static T Styled<T>(this T element, string styleKey)
        where T : VisualElement
    {
        element.SetDynamicResource(VisualElement.StyleProperty, styleKey);
        return element;
    }

    public static Label Label(string styleKey, string? text = null) =>
        new Label { Text = text }.Styled(styleKey);

    public static Border Card(View content) =>
        new Border { Content = content }.Styled(AIChatThemeKeys.CardStyle);

    public static Border CodeBlock(Label text) =>
        new Border { Content = text }.Styled(AIChatThemeKeys.CodeBlockStyle);

    /// <summary>Creates an inline image for image content, or a file chip for anything else.</summary>
    public static View CreateMediaView(DataContent content, bool alignEnd = false)
    {
        var name = string.IsNullOrWhiteSpace(content.Name) ? null : content.Name;
        if (content.HasTopLevelMediaType("image") && !content.Data.IsEmpty)
        {
            var bytes = content.Data;
            var image = new Image
            {
                Source = ImageSource.FromStream(() => new MemoryStream(bytes.ToArray())),
                Aspect = Aspect.AspectFit,
                MaximumHeightRequest = 240,
                MaximumWidthRequest = 320,
                HorizontalOptions = alignEnd ? LayoutOptions.End : LayoutOptions.Start,
            };
            SemanticProperties.SetDescription(image, name ?? "Image");
            return image;
        }

        return CreateFileChip(content, alignEnd);
    }

    public static View CreateFileChip(DataContent content, bool alignEnd = false, View? trailing = null)
    {
        var name = string.IsNullOrWhiteSpace(content.Name) ? DescribeMediaType(content.MediaType) : content.Name!;
        var glyph = content.HasTopLevelMediaType("audio") ? "\U0001F3B5" : "\U0001F4C4";
        var detail = $"{DescribeMediaType(content.MediaType)} · {FormatSize(content.Data.Length)}";

        var text = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Label(AIChatThemeKeys.CardTitleStyle, name),
                Label(AIChatThemeKeys.SecondaryTextStyle, detail),
            },
        };

        var row = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = glyph, FontSize = 18, VerticalOptions = LayoutOptions.Center },
                text,
            },
        };
        if (trailing is not null)
            row.Children.Add(trailing);

        var chip = new Border
        {
            Content = row,
            HorizontalOptions = alignEnd ? LayoutOptions.End : LayoutOptions.Start,
        }.Styled(AIChatThemeKeys.AttachmentChipStyle);
        SemanticProperties.SetDescription(chip, $"{name}, {detail}");
        return chip;
    }

    public static string DescribeMediaType(string mediaType)
    {
        var slash = mediaType.IndexOf('/', StringComparison.Ordinal);
        var subtype = slash >= 0 ? mediaType[(slash + 1)..] : mediaType;
        var plus = subtype.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            subtype = subtype[..plus];
        return subtype.ToUpperInvariant();
    }

    public static string FormatSize(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
            _ => (bytes / (1024d * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
        };

    public static string FormatArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var (name, value) in arguments)
        {
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(name).Append(": ").Append(FormatValue(value));
        }

        return builder.ToString();
    }

    public static string FormatValue(object? value) =>
        value switch
        {
            null => "null",
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            JsonElement element => Indent(element),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    private static string Indent(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            element.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
