namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches blocks of a type, including derived types.</summary>
/// <remarks>
/// In XAML, <c>{chat:Is chat:TextContentBlock}</c> creates one. A <c>[ToolBlock]</c> type matches only its own tool's
/// calls, which makes it the most precise way to pick the card of a tool. Without a <see cref="Type"/>, no block
/// matches.
/// </remarks>
public sealed class BlockTypeMatch : BlockMatch
{
    private Type? _type;

    /// <summary>Gets or sets the block type, such as <c>{x:Type chat:FunctionInvocationContentBlock}</c>.</summary>
    /// <remarks>In XAML, the type can also be written by name: <c>Type="chat:TextContentBlock"</c>.</remarks>
    /// <exception cref="ArgumentException">The type does not derive from <see cref="ContentBlock"/>.</exception>
    [System.ComponentModel.TypeConverter(typeof(TypeTypeConverter))]
    public Type? Type
    {
        get => _type;
        set
        {
            if (value is not null && !typeof(ContentBlock).IsAssignableFrom(value))
                throw new ArgumentException($"{value.Name} is not a {nameof(ContentBlock)} type.", nameof(value));
            _type = value;
        }
    }

    /// <inheritdoc />
    public override bool Matches(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return _type is not null && _type.IsInstanceOfType(block);
    }
}
