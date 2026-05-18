namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Displays an error message.
/// Styled via a ControlTemplate that uses <c>{TemplateBinding ErrorMessageText}</c>.
/// </summary>
public class ErrorMessageView : ContentContextView
{
    public static readonly BindableProperty ErrorMessageTextProperty =
        BindableProperty.Create(nameof(ErrorMessageText), typeof(string), typeof(ErrorMessageView));

    public string? ErrorMessageText
    {
        get => (string?)GetValue(ErrorMessageTextProperty);
        set => SetValue(ErrorMessageTextProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        ErrorMessageText = "An error occurred.";
    }
}
