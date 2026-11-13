namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Pairs a condition (<see cref="When"/>) with the template (<see cref="Template"/>) that shows the blocks it matches.
/// </summary>
/// <remarks>
/// <para>
/// Add templates to a <see cref="BlockTemplateSelector"/>, which uses the first that matches a block:
/// </para>
/// <code language="xml">
/// &lt;chat:BlockTemplateSelector&gt;
///     &lt;chat:BlockTemplate When="{chat:Tool calculate}" Template="{chat:ViewTemplate local:CalculatorCard}" /&gt;
///     &lt;chat:BlockTemplate When="{chat:Is chat:TextContentBlock}"&gt;
///         &lt;DataTemplate x:DataType="chat:TextContentBlock"&gt;
///             &lt;Label Text="{Binding RawText}" /&gt;
///         &lt;/DataTemplate&gt;
///     &lt;/chat:BlockTemplate&gt;
/// &lt;/chat:BlockTemplateSelector&gt;
/// </code>
/// <para>
/// The view's <see cref="BindableObject.BindingContext"/> is the block, and stays the block while it streams. A
/// binding refreshes when the property it reads raises
/// <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>, so <c>{Binding RawText}</c> and
/// <c>{Binding Result.Result}</c> stay live. A binding to the block itself (<c>{Binding .}</c>) keeps its first value,
/// and so does a binding that hands a view an object or list the block changes in place, because reading the
/// property again returns the same instance. Put logic over the whole block in a <see cref="BlockView"/>, and expose
/// a list the block changes in place as an <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>.
/// </para>
/// </remarks>
[ContentProperty(nameof(Template))]
public sealed class BlockTemplate : BindableObject
{
    /// <summary>Backing property for <see cref="When"/>.</summary>
    public static readonly BindableProperty WhenProperty =
        BindableProperty.Create(nameof(When), typeof(BlockMatch), typeof(BlockTemplate));

    /// <summary>Backing property for <see cref="Template"/>.</summary>
    public static readonly BindableProperty TemplateProperty =
        BindableProperty.Create(nameof(Template), typeof(DataTemplate), typeof(BlockTemplate));

    /// <summary>
    /// Gets or sets the blocks this template applies to, such as <c>{chat:Tool calculate}</c>. When it isn't set, the
    /// template applies to every block.
    /// </summary>
    public BlockMatch? When
    {
        get => (BlockMatch?)GetValue(WhenProperty);
        set => SetValue(WhenProperty, value);
    }

    /// <summary>
    /// Gets or sets the template of the view that shows a matching block, such as <c>{chat:ViewTemplate local:Card}</c>
    /// or an inline <see cref="DataTemplate"/>. Without one, a matching block shows nothing. In XAML, it is the
    /// element's content.
    /// </summary>
    public DataTemplate? Template
    {
        get => (DataTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }
}
