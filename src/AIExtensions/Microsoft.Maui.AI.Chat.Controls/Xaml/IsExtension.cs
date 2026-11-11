using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Provides a <see cref="BlockTypeMatch"/>: <c>When="{chat:Is chat:TextContentBlock}"</c>.</summary>
[ContentProperty(nameof(Type))]
[AcceptEmptyServiceProvider]
public sealed class IsExtension : IMarkupExtension<BlockMatch>
{
    /// <summary>Gets or sets the block type to match, including derived types.</summary>
    [System.ComponentModel.TypeConverter(typeof(TypeTypeConverter))]
    public Type? Type { get; set; }

    /// <inheritdoc />
    public BlockMatch ProvideValue(IServiceProvider serviceProvider) =>
        new BlockTypeMatch
        {
            Type = Type ?? throw new InvalidOperationException("{chat:Is} needs a block type, such as {chat:Is chat:TextContentBlock}."),
        };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
