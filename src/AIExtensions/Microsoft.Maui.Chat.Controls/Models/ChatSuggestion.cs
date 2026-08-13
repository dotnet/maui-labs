namespace Microsoft.Maui.Chat.Controls;

/// <summary>A labelled prompt offered to the local participant, typically shown while a conversation is empty.</summary>
/// <remarks>Suggestions are provider neutral: they are simply text the composer sends when picked.</remarks>
public class ChatSuggestion
{
    /// <summary>Creates a suggestion.</summary>
    /// <param name="label">The text shown on the chip.</param>
    /// <param name="prompt">The text sent when the chip is picked. Defaults to <paramref name="label"/>.</param>
    /// <param name="icon">An optional glyph shown before the label.</param>
    /// <exception cref="ArgumentException"><paramref name="label"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public ChatSuggestion(string label, string? prompt = null, string? icon = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Label = label;
        Prompt = string.IsNullOrWhiteSpace(prompt) ? label : prompt;
        Icon = icon;
    }

    /// <summary>Gets the text shown on the chip.</summary>
    public string Label { get; }

    /// <summary>Gets the text sent when the chip is picked.</summary>
    public string Prompt { get; }

    /// <summary>Gets the optional glyph shown before the label.</summary>
    public string? Icon { get; init; }

    /// <summary>Gets whether <see cref="Icon"/> has a value, for binding chip visibility.</summary>
    public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);

    /// <summary>Gets the label prefixed by <see cref="Icon"/> when one is present.</summary>
    public string DisplayText => HasIcon ? $"{Icon} {Label}" : Label;
}
