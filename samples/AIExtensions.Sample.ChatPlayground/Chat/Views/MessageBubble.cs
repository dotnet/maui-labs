using System.ComponentModel;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Remeasures a recycled message container when its dynamic content changes.</summary>
public sealed class MessageBubble : Border
{
    private ChatMessageViewModel? _message;

    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        if (_message is not null)
            _message.PropertyChanged -= MessagePropertyChanged;

        base.OnBindingContextChanged();
        _message = BindingContext as ChatMessageViewModel;
        if (_message is not null)
            _message.PropertyChanged += MessagePropertyChanged;
    }

    private void MessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatMessageViewModel.Text) or
            nameof(ChatMessageViewModel.DetailText) or
            nameof(ChatMessageViewModel.ImageSource))
            InvalidateMeasure();
    }
}
