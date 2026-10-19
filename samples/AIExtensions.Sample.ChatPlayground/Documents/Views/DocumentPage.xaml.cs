namespace AIExtensions.Sample.ChatPlayground;

public partial class DocumentPage : ContentPage
{
    public DocumentPage(DocumentPlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (BindingContext is DocumentPlaygroundViewModel viewModel)
            viewModel.ReleasePageResult();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null && BindingContext is DocumentPlaygroundViewModel viewModel)
            viewModel.ReleasePageResult();
    }
}
