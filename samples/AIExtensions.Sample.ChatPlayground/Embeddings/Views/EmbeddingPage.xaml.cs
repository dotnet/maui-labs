
namespace AIExtensions.Sample.ChatPlayground;

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
                viewModel.ReportImportProblem("Document import cancelled.");
                return;
            }
            await using var stream = await file.OpenReadAsync();
            await viewModel.ImportDocumentAsync(file.FileName, stream);
        }
        catch (Exception exception)
        {
            viewModel.ReportImportProblem($"Could not open document: {exception.Message}");
        }
    }
}
