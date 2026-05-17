using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Controls.Chat;

/// <summary>
/// Displays a function call indicator (e.g. "Calling search_plants...").
/// Styled via a ControlTemplate that uses <c>{TemplateBinding FunctionName}</c>.
/// </summary>
public class FunctionCallMessageView : ContentContextView
{
    public static readonly BindableProperty FunctionNameProperty =
        BindableProperty.Create(nameof(FunctionName), typeof(string), typeof(FunctionCallMessageView));

    public string? FunctionName
    {
        get => (string?)GetValue(FunctionNameProperty);
        set => SetValue(FunctionNameProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        FunctionName = (ContentContext?.Content as FunctionCallContent)?.Name;
    }
}
