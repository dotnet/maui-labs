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

    private async void EnableAzureClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not ChatLibraryViewModel library)
            return;

        try
        {
            var page = Window?.Page
                ?? throw new InvalidOperationException("The saved-chat dialog is not attached to an app window.");
            var approved = await page.DisplayAlertAsync(
                "Enable Azure semantic search?",
                "This sends text from saved conversations and your search terms to your configured Azure embedding deployment. Images and tool payloads are not sent.",
                "Enable", "Keep private");
            if (approved)
            {
                Preferences.Default.Set(ChatLibraryViewModel.AzureConsentPreferenceKey, true);
                await library.SelectAzureAsync(true);
            }
        }
        catch (Exception exception)
        {
            library.StatusMessage = $"Could not enable Azure search: {exception.Message}";
        }
    }

    private async void DisableAzureClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not ChatLibraryViewModel library)
            return;

        try
        {
            Preferences.Default.Set(ChatLibraryViewModel.AzureConsentPreferenceKey, false);
            await library.SelectAzureAsync(false);
        }
        catch (Exception exception)
        {
            library.StatusMessage = $"Could not disable Azure search: {exception.Message}";
        }
    }
}
