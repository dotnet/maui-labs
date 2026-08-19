namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// Text content with a structured document projection for a specialized renderer.
/// </summary>
/// <typeparam name="TDocument">The provider-owned structured document type.</typeparam>
/// <remarks>
/// The text remains the universal fallback, so a host with no template for <typeparamref name="TDocument"/>
/// still renders a readable message through <see cref="ChatTextContentView"/>. The neutral chat package
/// does not interpret or depend on the document type.
/// </remarks>
public class StructuredTextMessageContent<TDocument> : TextMessageContent
    where TDocument : notnull
{
    private TDocument _document;

    /// <summary>Creates structured text content.</summary>
    /// <param name="text">The readable text fallback.</param>
    /// <param name="document">The structured document.</param>
    /// <param name="id">An optional stable identity.</param>
    public StructuredTextMessageContent(
        string? text,
        TDocument document,
        string? id = null)
        : base(text, id)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    /// <summary>Gets the current structured document.</summary>
    public TDocument Document => _document;

    /// <summary>Atomically replaces the readable fallback and structured document.</summary>
    /// <param name="text">The new readable text fallback.</param>
    /// <param name="document">The new structured document.</param>
    public void Replace(
        string? text,
        TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _document = document;
        OnPropertyChanged(nameof(Document));

        var value = text ?? string.Empty;
        if (string.Equals(Text, value, StringComparison.Ordinal))
            RaiseContentChanged();
        else
            Text = value;
    }
}
