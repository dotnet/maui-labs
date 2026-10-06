namespace AIExtensions.Sample.ChatPlayground;

public partial class DocumentPage : ContentPage
{
    public DocumentPage(DocumentPlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
