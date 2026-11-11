using CommunityToolkit.Mvvm.ComponentModel;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// The accent color that the frontend-tools scenario's UI actions change. The scenario's view applies it to the chat
/// through the <c>AIChat.Color.Accent</c> theme key.
/// </summary>
public sealed partial class AccentColorController : ObservableObject, IAccentColorSurface
{
    // CSS names that Color.TryParse does not know.
    private static readonly Dictionary<string, string> ExtraNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rebeccapurple"] = "#663399",
    };

    /// <summary>Gets the accent color the agent chose, or <see langword="null"/> for the theme's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description), nameof(OnAccent), nameof(IsCustom))]
    private Color? color;

    /// <summary>Gets whether the agent changed the accent color.</summary>
    public bool IsCustom => Color is not null;

    /// <summary>Gets the color as text, such as <c>#663399</c>.</summary>
    public string Description => Color is { } value ? ToHex(value) : "Theme default";

    /// <summary>Gets a text color that reads on the accent color.</summary>
    public Color OnAccent => Color is { } value && value.GetLuminosity() > 0.6f ? Colors.Black : Colors.White;

    /// <inheritdoc />
    public string? ApplyAccentColor(string color)
    {
        if (!TryParse(color, out var parsed))
            return null;

        Color = parsed;
        return ToHex(parsed);
    }

    /// <summary>Reads a CSS color name or a hex color.</summary>
    public static bool TryParse(string? text, out Color color)
    {
        var value = text?.Trim() ?? string.Empty;
        if (ExtraNames.TryGetValue(value, out var hex))
            value = hex;
        if (value.Length > 0 && Microsoft.Maui.Graphics.Color.TryParse(value, out var parsed))
        {
            color = parsed.WithAlpha(1);
            return true;
        }

        color = Colors.Transparent;
        return false;
    }

    /// <inheritdoc />
    public void ResetAccentColor() => Color = null;

    /// <summary>Formats a color as <c>#RRGGBB</c>.</summary>
    public static string ToHex(Color color) =>
        $"#{ToByte(color.Red):X2}{ToByte(color.Green):X2}{ToByte(color.Blue):X2}";

    private static byte ToByte(float channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
}
