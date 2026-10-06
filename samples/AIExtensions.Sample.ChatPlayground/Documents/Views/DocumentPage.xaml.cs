namespace AIExtensions.Sample.ChatPlayground;

public partial class DocumentPage : ContentPage
{
    public DocumentPage(DocumentPlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void ReadClicked(object? sender, EventArgs e)
    {
        var viewModel = (DocumentPlaygroundViewModel)BindingContext;
        if (!viewModel.ReadDocument.CanExecute(null))
            return;
        var selectionVersion = viewModel.SelectionVersion;
        var selectedOption = viewModel.Settings.SelectedOption;
        if (viewModel.IsCloudSelected)
        {
            var consent = await DisplayAlertAsync(
                "Upload document to Azure?",
                "The entire selected document will be uploaded to Azure AI Document Intelligence for prebuilt-layout analysis. This is optional and may incur charges.",
                "Upload and read", "Cancel");
            if (!consent)
                return;
        }
        if (viewModel.SelectionVersion == selectionVersion &&
            viewModel.Settings.SelectedOption == selectedOption &&
            viewModel.ReadDocument.CanExecute(null))
            await viewModel.ReadDocument.ExecuteAsync(null);
    }
}
