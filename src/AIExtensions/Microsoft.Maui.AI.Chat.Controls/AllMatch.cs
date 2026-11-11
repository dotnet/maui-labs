namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches blocks that satisfy every one of its <see cref="Conditions"/>.</summary>
/// <remarks>Without conditions, every block matches. Null conditions are ignored.</remarks>
[ContentProperty(nameof(Conditions))]
public sealed class AllMatch : BlockMatch
{
    /// <summary>Gets the conditions. In XAML, they are the element's content.</summary>
    public IList<BlockMatch> Conditions { get; } = new List<BlockMatch>();

    /// <inheritdoc />
    public override bool Matches(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        foreach (var condition in Conditions)
        {
            if (condition is not null && !condition.Matches(block))
                return false;
        }

        return true;
    }
}
