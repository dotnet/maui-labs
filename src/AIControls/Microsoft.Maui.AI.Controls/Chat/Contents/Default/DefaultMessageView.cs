namespace Microsoft.Maui.AI.Controls.Chat;

/// <summary>
/// Catch-all view for content types that have no specific template.
/// Displays <c>Content.ToString()</c> via <c>{TemplateBinding DisplayText}</c>.
/// </summary>
public class DefaultMessageView : ContentContextView
{
    public static readonly BindableProperty DisplayTextProperty =
        BindableProperty.Create(nameof(DisplayText), typeof(string), typeof(DefaultMessageView));

    public string? DisplayText
    {
        get => (string?)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        DisplayText = ContentContext?.Content?.ToString();
    }
}
