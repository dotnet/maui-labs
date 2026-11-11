namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches blocks with a role, such as the user's or the assistant's.</summary>
/// <remarks>In XAML, <c>{chat:Role user}</c> creates one. Without a <see cref="Role"/>, no block matches.</remarks>
public sealed class RoleMatch : BlockMatch
{
    /// <summary>Gets or sets the role, such as <c>user</c> or <c>assistant</c>, compared case-insensitively.</summary>
    public new string? Role { get; set; }

    /// <inheritdoc />
    public override bool Matches(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return !string.IsNullOrEmpty(Role) && string.Equals(block.Role?.Value, Role, StringComparison.OrdinalIgnoreCase);
    }
}
