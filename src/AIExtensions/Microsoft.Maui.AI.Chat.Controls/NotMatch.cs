namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches blocks that don't satisfy its <see cref="Match"/>.</summary>
/// <remarks>Without a <see cref="Match"/>, no block matches.</remarks>
[ContentProperty(nameof(Match))]
public sealed class NotMatch : BlockMatch
{
    /// <summary>Gets or sets the condition to invert. In XAML, it is the element's content.</summary>
    public BlockMatch? Match { get; set; }

    /// <inheritdoc />
    public override bool Matches(ContentBlock block) => Matches(block, tool: null);

    internal override bool Matches(ContentBlock block, string? tool)
    {
        ArgumentNullException.ThrowIfNull(block);
        return Match is not null && !Match.Matches(block, tool);
    }
}
