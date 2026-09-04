namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// Controls how a runtime page snapshot is rendered.
/// </summary>
public sealed class CurrentPageSnapshotOptions
{
    /// <summary>
    /// Include the current text of non-password <c>Entry</c>, <c>Editor</c>, and
    /// <c>SearchBar</c> controls. Defaults to <see langword="false"/> so user-entered
    /// text is not sent to an AI unless the app opts in. Password text is never included.
    /// </summary>
    public bool IncludeInputText { get; init; }

    /// <summary>
    /// Maximum length of a single text value before it is truncated. Defaults to 300.
    /// </summary>
    public int MaximumTextLength { get; init; } = 300;

    internal void Validate()
    {
        if (MaximumTextLength <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(MaximumTextLength),
                MaximumTextLength,
                "Maximum text length must be greater than zero.");
    }
}
