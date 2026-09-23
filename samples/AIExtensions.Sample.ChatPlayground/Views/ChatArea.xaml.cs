using System.Collections.Specialized;
using System.ComponentModel;
using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground.Views;

/// <summary>Displays dynamically measured chat messages and the composer.</summary>
public partial class ChatArea : ContentView
{
    private readonly HashSet<ChatMessageViewModel> _observedMessages = [];
    private ChatAreaViewModel? _viewModel;
    private bool _scrollPending;

    /// <summary>Initializes the chat content.</summary>
    public ChatArea() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        UnsubscribeFromMessages();
        base.OnBindingContextChanged();

        _viewModel = BindingContext as ChatAreaViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Messages.CollectionChanged += MessagesCollectionChanged;
            foreach (var message in _viewModel.Messages)
                ObserveMessage(message);
        }

        QueueScrollToEnd();
    }

    private void MessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var message in _observedMessages.ToArray())
                StopObservingMessage(message);
            if (_viewModel is not null)
                foreach (var message in _viewModel.Messages)
                    ObserveMessage(message);
        }
        else
        {
            if (e.OldItems is not null)
                foreach (ChatMessageViewModel message in e.OldItems)
                    StopObservingMessage(message);
            if (e.NewItems is not null)
                foreach (ChatMessageViewModel message in e.NewItems)
                    ObserveMessage(message);
        }

        QueueScrollToEnd();
    }

    private void ObserveMessage(ChatMessageViewModel message)
    {
        if (_observedMessages.Add(message))
            message.PropertyChanged += MessagePropertyChanged;
    }

    private void StopObservingMessage(ChatMessageViewModel message)
    {
        if (_observedMessages.Remove(message))
            message.PropertyChanged -= MessagePropertyChanged;
    }

    private void MessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatMessageViewModel.Text) or
            nameof(ChatMessageViewModel.DetailText) or
            nameof(ChatMessageViewModel.ImageSource))
        {
            MessagesView.InvalidateMeasure();
            QueueScrollToEnd();
        }
    }

    private void QueueScrollToEnd()
    {
        if (_scrollPending)
            return;

        // Streaming grows an existing bubble; wait for its new measure before scrolling by index.
        _scrollPending = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(75), () =>
        {
            _scrollPending = false;
            if (_viewModel is not { Messages.Count: > 0 })
                return;

            MessagesView.InvalidateMeasure();
            MessagesView.ScrollTo(_viewModel.Messages.Count - 1, position: ScrollToPosition.End, animate: false);
        });
    }

    private void UnsubscribeFromMessages()
    {
        if (_viewModel is not null)
            _viewModel.Messages.CollectionChanged -= MessagesCollectionChanged;
        foreach (var message in _observedMessages.ToArray())
            StopObservingMessage(message);
        _viewModel = null;
    }
}
