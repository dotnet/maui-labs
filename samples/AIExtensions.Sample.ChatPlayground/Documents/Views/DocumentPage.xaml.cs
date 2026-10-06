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
        await viewModel.ReadDocument.ExecuteAsync(null);
    }
}
