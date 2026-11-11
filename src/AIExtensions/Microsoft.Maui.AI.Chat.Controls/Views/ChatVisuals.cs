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
        new ChatLabel { Text = text }.Styled(styleKey);

    public static Border Card(View content) =>
        new ChatBorder { Content = content }.Styled(AIChatThemeKeys.CardStyle);

    public static Border CodeBlock(Label text) =>
        new ChatBorder { Content = text }.Styled(AIChatThemeKeys.CodeBlockStyle);

    /// <summary>A chevron that shows or hides a card's details; see <see cref="SetExpanded"/>.</summary>
    public static Button DisclosureButton(Action toggle, string automationId)
    {
        var button = new ChatButton
        {
            FontSize = 20,
            WidthRequest = 28,
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center,
            Command = new Command(toggle),
            AutomationId = automationId,
        }.Styled(AIChatThemeKeys.IconButtonStyle);
        button.SetDynamicResource(Button.TextProperty, AIChatThemeKeys.ExpandIcon);
        return button;
    }

    /// <summary>Shows the chevron that expands the details, or the one that collapses them, and names the action.</summary>
    public static void SetExpanded(Button disclosure, bool expanded, string subject)
    {
        disclosure.SetDynamicResource(Button.TextProperty, expanded ? AIChatThemeKeys.CollapseIcon : AIChatThemeKeys.ExpandIcon);
        SemanticProperties.SetDescription(disclosure, $"{(expanded ? "Hide" : "Show")} details for {subject}");
    }

    /// <summary>A glyph of the icon font, from the theme: decoration next to the text that names it.</summary>
    public static Label Icon(string iconKey, double fontSize = 20)
    {
        var icon = new ChatLabel { FontSize = fontSize, VerticalOptions = LayoutOptions.Center };
        icon.SetDynamicResource(Microsoft.Maui.Controls.Label.FontFamilyProperty, AIChatThemeKeys.IconFontFamily);
        icon.SetDynamicResource(Microsoft.Maui.Controls.Label.TextProperty, iconKey);
        icon.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, AIChatThemeKeys.SecondaryTextColor);
        AutomationProperties.SetExcludedWithChildren(icon, true);
        return icon;
    }

    /// <summary>
    /// Creates the view for one media item: an inline image for image data or an image link, a file chip for
    /// other data, and a link chip for other links.
    /// </summary>
    public static View CreateMediaView(AIContent content, bool alignEnd = false) =>
        content switch
        {
            DataContent data when data.HasTopLevelMediaType("image") && !data.Data.IsEmpty =>
                CreateImage(CreateImageSource(data)!, data.Name, alignEnd, ImageHeader.ReadSize(data.Data.Span)),
            UriContent link when link.HasTopLevelMediaType("image") && IsWebUri(link.Uri) =>
                new LinkedImageView(
                    CreateImage(CreateImageSource(link)!, GetLinkName(link.Uri), alignEnd),
                    () => CreateLinkChip(link, alignEnd)),
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
        if (current is ChatImage image && CreateImageSource(content) is { } source)
        {
            image.PixelSize = content is DataContent data ? ImageHeader.ReadSize(data.Data.Span) : null;
            image.Source = source;
            return image;
        }

        return CreateMediaView(content, alignEnd);
    }

    internal static ImageSource? CreateImageSource(AIContent content) =>
        content switch
        {
            DataContent data when data.HasTopLevelMediaType("image") && !data.Data.IsEmpty =>
                ImageSource.FromStream(() => new MemoryStream(data.Data.ToArray())),
            UriContent link when link.HasTopLevelMediaType("image") && IsWebUri(link.Uri) =>
                ImageSource.FromUri(link.Uri),
            _ => null,
        };

    internal static ChatImage CreateImage(ImageSource source, string? name, bool alignEnd, Size? pixelSize = null)
    {
        var image = new ChatImage
        {
            PixelSize = pixelSize,
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
        var chip = CreateChip(Icon(AIChatThemeKeys.LinkIcon), name, detail, alignEnd, trailing: null);
        if (IsSafeUri(uri))
        {
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OpenLinkAsync(uri)) });
            SemanticProperties.SetHint(chip, "Opens the link");
        }

        return chip;
    }

    /// <summary>
    /// A chip for an image that is not shown: remote images are off, its source is not a web link (for example
    /// <c>attachment://chart.png</c>), or it could not be loaded. A web link names its site and opens when tapped,
    /// with <paramref name="open"/> when given.
    /// </summary>
    public static Border CreateImageChip(string? source, string? description, Func<Uri, Task<bool>>? open = null)
    {
        Uri.TryCreate(source, UriKind.Absolute, out var uri);
        string name, detail;
        if (uri is not null && IsWebUri(uri))
        {
            // The site rather than the file, so the reader knows where the link goes before opening it.
            name = !string.IsNullOrWhiteSpace(description) ? description! : "Image";
            detail = string.IsNullOrWhiteSpace(description) ? uri.Host : $"Image \u00B7 {uri.Host}";
        }
        else
        {
            var file = uri is null ? source : GetLinkName(uri);
            name = !string.IsNullOrWhiteSpace(description) ? description! : !string.IsNullOrWhiteSpace(file) ? file! : "Image";
            detail = string.IsNullOrWhiteSpace(file) || file == name ? "Image" : $"Image \u00B7 {file}";
        }

        var chip = CreateChip(Icon(AIChatThemeKeys.ImageIcon), name, detail, alignEnd: false, trailing: null);
        if (uri is not null && IsWebUri(uri))
        {
            open ??= link => OpenLinkAsync(link);
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await open(uri)) });
            SemanticProperties.SetHint(chip, "Opens the link");
        }

        return chip;
    }

    internal static string GetLinkName(Uri uri)
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

    /// <summary>
    /// Opens a safe link (http, https, mailto) in the platform browser or mail app, with <paramref name="launcher"/>
    /// when given. Never throws: taps run in commands, where an exception would crash the app.
    /// </summary>
    /// <returns>Whether the link opened.</returns>
    public static async Task<bool> OpenLinkAsync(Uri uri, Func<Uri, Task<bool>>? launcher = null)
    {
        if (!IsSafeUri(uri))
            return false;

        try
        {
            return launcher is not null ? await launcher(uri) : await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception)
        {
            // No browser or mail app (Android throws ActivityNotFoundException on a kiosk or managed device), no UI
            // context, or no launcher on this platform: the tap does nothing, and the link stays readable.
            return false;
        }
    }

    /// <summary>A chip for a file: a thumbnail for an image with data, otherwise an icon for its kind.</summary>
    public static View CreateFileChip(DataContent content, bool alignEnd = false, View? trailing = null)
    {
        var name = string.IsNullOrWhiteSpace(content.Name) ? DescribeMediaType(content.MediaType) : content.Name!;
        var detail = $"{DescribeMediaType(content.MediaType)} · {FormatSize(content.Data.Length)}";
        View leading = CreateImageSource(content) is { } source
            ? CreateThumbnail(source)
            : Icon(
                content.HasTopLevelMediaType("image") ? AIChatThemeKeys.ImageIcon :
                content.HasTopLevelMediaType("audio") ? AIChatThemeKeys.AudioIcon :
                AIChatThemeKeys.DocumentIcon);
        return CreateChip(leading, name, detail, alignEnd, trailing);
    }

    private static ChatImage CreateThumbnail(ImageSource source)
    {
        const double Size = 32;
        var thumbnail = new ChatImage
        {
            Source = source,
            Aspect = Aspect.AspectFill,
            WidthRequest = Size,
            HeightRequest = Size,
            VerticalOptions = LayoutOptions.Center,
            Clip = new Microsoft.Maui.Controls.Shapes.RoundRectangleGeometry(new CornerRadius(6), new Rect(0, 0, Size, Size)),
        };
        AutomationProperties.SetExcludedWithChildren(thumbnail, true);
        return thumbnail;
    }

    private static Border CreateChip(View leading, string name, string detail, bool alignEnd, View? trailing)
    {
        var text = new ChatVerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Label(AIChatThemeKeys.CardTitleStyle, name),
                Label(AIChatThemeKeys.SecondaryTextStyle, detail),
            },
        };

        var row = new ChatHorizontalStackLayout
        {
            Spacing = 8,
            Children = { leading, text },
        };
        if (trailing is not null)
            row.Children.Add(trailing);
        else
            text.Margin = new Thickness(0, 0, 6, 0); // The chip's end padding is sized for a trailing button.

        var chip = new ChatBorder
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

/// <summary>An image loaded from a web link. When it cannot be loaded, a chip for the link takes its place.</summary>
internal sealed class LinkedImageView : ContentView
{
    private readonly bool _themed = ChatElements.EnsureTheme();
    private readonly Func<View> _createFallback;

    public LinkedImageView(Image image, Func<View> createFallback)
    {
        _createFallback = createFallback;
        Image = image;
        HorizontalOptions = image.HorizontalOptions;
        Content = image;
        Image.PropertyChanged += OnImagePropertyChanged;

        // The image (or the chip that replaces it) takes its size once it loads, after the row was laid out: a list
        // following the answer keeps up. The new size is known here, so the list scrolls to the right end.
        SizeChanged += (_, _) => ChatMessageList.FollowResizedContent(this);
    }

    public Image Image { get; }

    /// <summary>Replaces the image with the fallback chip.</summary>
    internal void ShowFallback()
    {
        Image.PropertyChanged -= OnImagePropertyChanged;
        Content = _createFallback();
    }

    private void OnImagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != Microsoft.Maui.Controls.Image.IsLoadingProperty.PropertyName || Image.IsLoading || Image.Handler is null)
            return;

        // Image has no failure event: a load that ends with nothing to measure failed (a 404, or a page, not a picture).
        if (((IView)Image).Measure(double.PositiveInfinity, double.PositiveInfinity).Height <= 0)
            ShowFallback();
    }
}
