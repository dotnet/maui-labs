using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Provides a <see cref="ViewTemplate"/>: <c>Template="{chat:ViewTemplate local:CalculatorCard}"</c>. The view comes
/// from the app's services when it can (see <see cref="ViewTemplate"/>).
/// </summary>
[ContentProperty(nameof(ViewType))]
[AcceptEmptyServiceProvider]
public sealed class ViewTemplateExtension : IMarkupExtension<DataTemplate>
{
    /// <summary>Gets or sets the type of view to create. It must derive from <see cref="View"/>.</summary>
    [System.ComponentModel.TypeConverter(typeof(TypeTypeConverter))]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type? ViewType { get; set; }

    /// <inheritdoc />
    public DataTemplate ProvideValue(IServiceProvider serviceProvider) =>
        new ViewTemplate(ViewType ?? throw new InvalidOperationException("{chat:ViewTemplate} needs a view type, such as {chat:ViewTemplate local:CalculatorCard}."));

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
