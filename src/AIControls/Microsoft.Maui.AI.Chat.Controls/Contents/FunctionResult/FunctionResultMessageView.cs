using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Fallback view for function results that don't have a custom template.
/// Displays the result as plain text via <c>{TemplateBinding ResultText}</c>.
/// </summary>
public class FunctionResultMessageView : ContentContextView
{
    public static readonly BindableProperty ResultTextProperty =
        BindableProperty.Create(nameof(ResultText), typeof(string), typeof(FunctionResultMessageView));

    public string? ResultText
    {
        get => (string?)GetValue(ResultTextProperty);
        set => SetValue(ResultTextProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        ResultText = (ContentContext?.Content as FunctionResultContent)?.Result?.ToString();
    }
}
