using GenerativeUI.Sample.Garden.ViewModels;
using System.Collections.Specialized;

namespace GenerativeUI.Sample.Garden;

public partial class MainPage : ContentPage
{
    private readonly ChatViewModel _viewModel;

    public MainPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.Messages.CollectionChanged += OnMessagesChanged;
        Unloaded += (_, _) => viewModel.Messages.CollectionChanged -= OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel.Messages.Count == 0)
            return;

        Dispatcher.Dispatch(async () =>
        {
            // Wait for CollectionView to realize the newly-added row before scrolling.
            await Task.Yield();
            Transcript.ScrollTo(_viewModel.Messages[^1], position: ScrollToPosition.End, animate: true);
        });
    }
}
