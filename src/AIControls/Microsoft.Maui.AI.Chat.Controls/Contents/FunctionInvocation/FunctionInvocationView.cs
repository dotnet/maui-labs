using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Default view for a <see cref="Microsoft.Maui.AI.Chat.FunctionInvocationContentBlock"/>. Renders the
/// pending tool call (e.g. "Calling search_plants...") and, once the block updates with a result,
/// switches to display the result text in place.
/// </summary>
/// <remarks>
/// A single tool invocation is one block whose <see cref="Microsoft.Maui.AI.Chat.FunctionInvocationContentBlock.Result"/>
/// is populated on a later update. This view reflects both phases so there is no need for separate
/// call and result templates. The bound <see cref="ControlTemplate"/> (see the theme) toggles between
/// the pending and result labels using <see cref="IsPending"/> and <see cref="HasResult"/>.
/// </remarks>
public class FunctionInvocationView : ContentContextView
{
    public static readonly BindableProperty FunctionNameProperty =
        BindableProperty.Create(nameof(FunctionName), typeof(string), typeof(FunctionInvocationView));

    public static readonly BindableProperty ResultTextProperty =
        BindableProperty.Create(nameof(ResultText), typeof(string), typeof(FunctionInvocationView));

    public static readonly BindableProperty HasResultProperty =
        BindableProperty.Create(nameof(HasResult), typeof(bool), typeof(FunctionInvocationView));

    public static readonly BindableProperty IsPendingProperty =
        BindableProperty.Create(nameof(IsPending), typeof(bool), typeof(FunctionInvocationView), true);

    public string? FunctionName
    {
        get => (string?)GetValue(FunctionNameProperty);
        set => SetValue(FunctionNameProperty, value);
    }

    public string? ResultText
    {
        get => (string?)GetValue(ResultTextProperty);
        set => SetValue(ResultTextProperty, value);
    }

    public bool HasResult
    {
        get => (bool)GetValue(HasResultProperty);
        set => SetValue(HasResultProperty, value);
    }

    public bool IsPending
    {
        get => (bool)GetValue(IsPendingProperty);
        set => SetValue(IsPendingProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        var block = ContentContext?.Block as FunctionInvocationContentBlock;
        FunctionName = block?.Call?.Name;

        var hasResult = block?.Result is not null;
        HasResult = hasResult;
        IsPending = !hasResult;
        ResultText = block?.Result?.Result?.ToString();
    }
}
