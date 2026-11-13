using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Provides a <see cref="ToolMatch"/>: <c>When="{chat:Tool calculate}"</c>.</summary>
[ContentProperty(nameof(Name))]
[AcceptEmptyServiceProvider]
public sealed class ToolExtension : IMarkupExtension<BlockMatch>
{
    /// <summary>Gets or sets the tool name, compared case-insensitively.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    public BlockMatch ProvideValue(IServiceProvider serviceProvider) =>
        string.IsNullOrEmpty(Name)
            ? throw new InvalidOperationException("{chat:Tool} needs a tool name, such as {chat:Tool calculate}.")
            : new ToolMatch { Name = Name };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
