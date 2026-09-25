using AIExtensions.Sample.ChatPlayground.Features.Embeddings.ViewModels;

namespace AIExtensions.Sample.ChatPlayground.Features.Embeddings.Views;

public partial class EmbeddingPage : ContentPage
{
    public EmbeddingPage(EmbeddingPlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadDocumentsAsync();
    }

    private async void ImportDocumentClicked(object? sender, EventArgs e)
    {
        var viewModel = (EmbeddingPlaygroundViewModel)BindingContext;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Import Markdown or plain text",
            });
            if (file is null)
            {
                viewModel.StatusMessage = "Document import cancelled.";
                return;
            }
            await using var stream = await file.OpenReadAsync();
            await viewModel.ImportDocumentAsync(file.FileName, stream);
        }
        catch (Exception exception)
        {
            viewModel.StatusMessage = $"Could not open document: {exception.Message}";
        }
    }

    private async void ImportSampleDocumentClicked(object? sender, EventArgs e)
    {
        var viewModel = (EmbeddingPlaygroundViewModel)BindingContext;
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("playground_notes.md");
            await viewModel.ImportDocumentAsync("playground_notes.md", stream);
        }
        catch (Exception exception)
        {
            viewModel.StatusMessage = $"Could not open sample document: {exception.Message}";
        }
    }
}
