using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Provides a <see cref="RoleMatch"/>: <c>When="{chat:Role user}"</c>.</summary>
[ContentProperty(nameof(Role))]
[AcceptEmptyServiceProvider]
public sealed class RoleExtension : IMarkupExtension<BlockMatch>
{
    /// <summary>Gets or sets the role, such as <c>user</c> or <c>assistant</c>, compared case-insensitively.</summary>
    public string? Role { get; set; }

    /// <inheritdoc />
    public BlockMatch ProvideValue(IServiceProvider serviceProvider) =>
        string.IsNullOrEmpty(Role)
            ? throw new InvalidOperationException("{chat:Role} needs a role, such as {chat:Role user}.")
            : new RoleMatch { Role = Role };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
