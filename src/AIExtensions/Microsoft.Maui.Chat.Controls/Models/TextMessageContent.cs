namespace Microsoft.Maui.Chat.Controls;

/// <summary>Plain text content. The text is observable so it can grow in place while streaming.</summary>
/// <remarks>
/// Setting <see cref="Text"/> or calling <see cref="Append"/> raises both
/// <see cref="BindableObject.PropertyChanged"/> (so bound labels update themselves) and
/// <see cref="MessageContent.ContentChanged"/> (so hosts can coalesce work such as auto-scrolling).
/// </remarks>
public class TextMessageContent : MessageContent
{
    /// <summary>Backing property for <see cref="Text"/>.</summary>
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(TextMessageContent),
            string.Empty,
            propertyChanged: static (bindable, _, _) => ((TextMessageContent)bindable).RaiseContentChanged(),
            coerceValue: static (_, value) => value ?? string.Empty);

    /// <summary>Creates empty text content.</summary>
    public TextMessageContent()
        : base(id: null)
    {
    }

    /// <summary>Creates text content with an initial value.</summary>
    /// <param name="text">The initial text. <see langword="null"/> is treated as an empty string.</param>
    /// <param name="id">A stable identifier. When <see langword="null"/>, a new unique identifier is generated.</param>
    public TextMessageContent(string? text, string? id = null)
        : base(id) => Text = text ?? string.Empty;

    /// <summary>Gets or sets the text. Never <see langword="null"/>.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets whether this content has no text.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Text);

    /// <summary>Appends text in place, the way a streaming response arrives.</summary>
    /// <param name="text">The text to append. <see langword="null"/> or empty is ignored.</param>
    public void Append(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Text += text;
    }
}
