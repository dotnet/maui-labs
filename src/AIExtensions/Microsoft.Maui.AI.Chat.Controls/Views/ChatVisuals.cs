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

    /// <summary>
    /// Creates the view for one media item: an inline image for image data or an image link, a file chip for
    /// other data, and a link chip for other links.
    /// </summary>
    public static View CreateMediaView(AIContent content, bool alignEnd = false) =>
        content switch
        {
            DataContent data when data.HasTopLevelMediaType("image") && !data.Data.IsEmpty =>
                CreateImage(CreateImageSource(data)!, data.Name, alignEnd),
            UriContent link when link.HasTopLevelMediaType("image") && IsWebUri(link.Uri) =>
                CreateImage(CreateImageSource(link)!, GetLinkName(link.Uri), alignEnd),
            DataContent data => CreateFileChip(data, alignEnd),
            UriContent link => CreateLinkChip(link, alignEnd),
            _ => Label(AIChatThemeKeys.SecondaryTextStyle, content.GetType().Name),
        };

    /// <summary>
    /// Shows <paramref name="content"/> in <paramref name="current"/> when both are images, so an image that is
    /// replaced while it streams (a partial image) updates in place; otherwise creates a new view.
    /// </summary>
    public static View UpdateMediaView(View? current, AIContent content, bool alignEnd = false)
    {
        if (current is Image image && CreateImageSource(content) is { } source)
        {
            image.Source = source;
            return image;
        }

        return CreateMediaView(content, alignEnd);
    }

    private static ImageSource? CreateImageSource(AIContent content) =>
        content switch
        {
            DataContent data when data.HasTopLevelMediaType("image") && !data.Data.IsEmpty =>
                ImageSource.FromStream(() => new MemoryStream(data.Data.ToArray())),
            UriContent link when link.HasTopLevelMediaType("image") && IsWebUri(link.Uri) =>
                ImageSource.FromUri(link.Uri),
            _ => null,
        };

    private static Image CreateImage(ImageSource source, string? name, bool alignEnd)
    {
        var image = new Image
        {
            Source = source,
            Aspect = Aspect.AspectFit,
            MaximumHeightRequest = 240,
            MaximumWidthRequest = 320,
            HorizontalOptions = alignEnd ? LayoutOptions.End : LayoutOptions.Start,
        };
        SemanticProperties.SetDescription(image, string.IsNullOrWhiteSpace(name) ? "Image" : name);
        return image;
    }

    /// <summary>A chip for media the model linked to rather than returned; tapping it opens the link.</summary>
    public static View CreateLinkChip(UriContent content, bool alignEnd = false)
    {
        var uri = content.Uri;
        var name = GetLinkName(uri);
        var detail = uri.IsAbsoluteUri ? $"{DescribeMediaType(content.MediaType)} · {uri.Host}" : DescribeMediaType(content.MediaType);
        var chip = CreateChip("\U0001F517", name, detail, alignEnd, trailing: null);
        if (IsSafeUri(uri))
        {
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OpenLinkAsync(uri)) });
            SemanticProperties.SetHint(chip, "Opens the link");
        }

        return chip;
    }

    private static string GetLinkName(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            return uri.OriginalString;

        var name = Path.GetFileName(uri.AbsolutePath);
        return string.IsNullOrEmpty(name) ? uri.Host : Uri.UnescapeDataString(name);
    }

    /// <summary>Gets whether a link is safe to open or load: http, https, or mailto.</summary>
    public static bool IsSafeUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsSafeUri(uri);

    public static bool IsSafeUri(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme is "http" or "https" or "mailto";

    private static bool IsWebUri(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme is "http" or "https";

    /// <summary>Opens a link in the platform browser or mail app, when the platform has one.</summary>
    public static async Task OpenLinkAsync(Uri uri)
    {
        try
        {
            await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or InvalidOperationException or NotImplementedException)
        {
            // No browser or launcher on this platform; the link text stays readable.
        }
    }

    public static View CreateFileChip(DataContent content, bool alignEnd = false, View? trailing = null)
    {
        var name = string.IsNullOrWhiteSpace(content.Name) ? DescribeMediaType(content.MediaType) : content.Name!;
        var glyph = content.HasTopLevelMediaType("audio") ? "\U0001F3B5" : "\U0001F4C4";
        var detail = $"{DescribeMediaType(content.MediaType)} · {FormatSize(content.Data.Length)}";
        return CreateChip(glyph, name, detail, alignEnd, trailing);
    }

    private static Border CreateChip(string glyph, string name, string detail, bool alignEnd, View? trailing)
    {
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

    /// <summary>
    /// Describes a tool call: its result when it has one, otherwise whether it is still running, was stopped, or
    /// never finished. Informational calls (run by the provider, or replayed from history) without a result have no
    /// status.
    /// </summary>
    public static string DescribeCall(FunctionInvocationContentBlock? block, bool isTurnActive, bool isTurnStopped) =>
        block?.Result switch
        {
            { Exception: not null } => "Failed",
            not null => "Completed",
            _ when isTurnActive => "Running\u2026",
            _ when block?.Call is { InformationalOnly: true } => string.Empty,
            _ when isTurnStopped => "Stopped",
            _ => "Not completed",
        };

    /// <summary>Formats a tool result for a code block: the error message, or the value.</summary>
    public static string FormatResult(FunctionResultContent result) =>
        result.Exception?.Message ?? FormatValue(result.Result);

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
