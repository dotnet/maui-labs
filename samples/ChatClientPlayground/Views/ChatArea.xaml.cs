using System.Collections.Specialized;
using System.ComponentModel;
using ChatClientPlayground.ViewModels;

namespace ChatClientPlayground.Views;

/// <summary>Displays dynamically measured chat messages and the responsive composer.</summary>
public partial class ChatArea : ContentView
{
    private readonly HashSet<ChatMessageViewModel> _observedMessages = [];
    private ChatAreaViewModel? _viewModel;
    private bool _scrollPending;

    /// <summary>Identifies whether the composer should use its compact arrangement.</summary>
    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact),
        typeof(bool),
        typeof(ChatArea),
        false,
        propertyChanged: static (bindable, _, value) => ((ChatArea)bindable).ApplyComposerLayout((bool)value));

    /// <summary>Gets or sets whether the composer uses its compact arrangement.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>Initializes the chat content.</summary>
    public ChatArea()
    {
        InitializeComponent();
        ApplyComposerLayout(IsCompact);
    }

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

    private void ApplyComposerLayout(bool isCompact)
    {
        ComposerGrid.ColumnDefinitions.Clear();
        ComposerGrid.RowDefinitions.Clear();

        if (isCompact)
        {
            for (var column = 0; column < 4; column++)
                ComposerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ComposerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            ComposerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Position(PromptEntry, row: 0, column: 0, columnSpan: 4);
            Position(AddAttachmentButton, row: 1, column: 0);
            Position(SendOrStopSlot, row: 1, column: 3);
            return;
        }

        ComposerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ComposerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ComposerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ComposerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Position(AddAttachmentButton, row: 0, column: 0);
        Position(PromptEntry, row: 0, column: 1);
        Position(SendOrStopSlot, row: 0, column: 2);
    }

    private static void Position(View? view, int row, int column, int columnSpan = 1)
    {
        if (view is null)
            return;
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetColumnSpan(view, columnSpan);
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
        if (e.PropertyName is nameof(ChatMessageViewModel.Text) or nameof(ChatMessageViewModel.DetailText) or nameof(ChatMessageViewModel.ImageSource))
        {
            MessagesView.InvalidateMeasure();
            QueueScrollToEnd();
        }
    }

    private void QueueScrollToEnd()
    {
        if (_scrollPending)
            return;

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
