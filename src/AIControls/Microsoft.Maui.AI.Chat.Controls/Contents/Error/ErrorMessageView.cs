using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Displays an error message from an <see cref="ErrorContent"/> item.
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
        ErrorMessageText = (ContentContext?.Content as ErrorContent)?.Message;
    }
}
