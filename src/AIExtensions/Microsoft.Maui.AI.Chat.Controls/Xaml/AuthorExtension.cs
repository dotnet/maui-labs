using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Provides an <see cref="AuthorMatch"/>: <c>When="{chat:Author researcher}"</c>.</summary>
[ContentProperty(nameof(Name))]
[AcceptEmptyServiceProvider]
public sealed class AuthorExtension : IMarkupExtension<BlockMatch>
{
    /// <summary>Gets or sets the author name, compared case-insensitively.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    public BlockMatch ProvideValue(IServiceProvider serviceProvider) =>
        string.IsNullOrEmpty(Name)
            ? throw new InvalidOperationException("{chat:Author} needs an author name, such as {chat:Author researcher}.")
            : new AuthorMatch { Name = Name };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
