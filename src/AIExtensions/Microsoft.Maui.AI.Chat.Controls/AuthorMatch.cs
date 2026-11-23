namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches blocks by their author (<see cref="ContentBlock.AuthorName"/>), such as an agent in a multi-agent conversation.</summary>
/// <remarks>In XAML, <c>{chat:Author researcher}</c> creates one. Without a <see cref="Name"/>, no block matches.</remarks>
public sealed class AuthorMatch : BlockMatch
{
    /// <summary>Gets or sets the author name, compared case-insensitively.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    public override bool Matches(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return !string.IsNullOrEmpty(Name) && string.Equals(block.AuthorName, Name, StringComparison.OrdinalIgnoreCase);
    }
}
