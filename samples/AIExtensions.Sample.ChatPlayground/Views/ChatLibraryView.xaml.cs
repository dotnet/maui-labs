using System.Collections.Specialized;
using System.ComponentModel;
using AIExtensions.Sample.ChatPlayground.Controls;
using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground.Views;

public partial class ChatLibraryView : ContentView
{
    private ChatLibraryViewModel? _viewModel;

    public ChatLibraryView()
    {
        InitializeComponent();
        Unloaded += (_, _) => _viewModel?.Close();
    }

    protected override void OnBindingContextChanged()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
            _viewModel.Results.CollectionChanged -= ResultsChanged;
        }
        base.OnBindingContextChanged();
        _viewModel = BindingContext as ChatLibraryViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
            _viewModel.Results.CollectionChanged += ResultsChanged;
        }
        UpdateHeight();
    }

    private void ResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateHeight();

    private void UpdateHeight() => HeightRequest = Math.Min(420, 160 + (_viewModel?.Results.Count ?? 0) * 65);

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatLibraryViewModel.IsOpen) && _viewModel?.IsOpen == false &&
            Handler is not null)
            PopupMenu.Dismiss(this);
    }
}
