using System.Collections.Specialized;
using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground.Views;

/// <summary>Sizes the saved-chat dialog within phones and desktop windows.</summary>
public partial class ChatLibraryView : ContentView
{
    private ChatLibraryViewModel? _viewModel;

    public ChatLibraryView()
    {
        InitializeComponent();
        DialogHost.SizeChanged += (_, _) => UpdateDialogSize();
    }

    protected override void OnBindingContextChanged()
    {
        if (_viewModel is not null)
            _viewModel.Results.CollectionChanged -= ResultsChanged;
        base.OnBindingContextChanged();
        _viewModel = BindingContext as ChatLibraryViewModel;
        if (_viewModel is not null)
            _viewModel.Results.CollectionChanged += ResultsChanged;
        UpdateDialogSize();
    }

    private void ResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateDialogSize();

    private void UpdateDialogSize()
    {
        if (DialogHost.Height > 0)
            DialogPanel.HeightRequest = Math.Min(620,
                Math.Min(Math.Max(0, DialogHost.Height - 24),
                    315 + Math.Max(1, Math.Min(_viewModel?.Results.Count ?? 0, 3)) * 96));
    }
}
