namespace AIExtensions.Sample.ChatPlayground;

public partial class DocumentPage : ContentPage
{
    public DocumentPage(DocumentPlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ((DocumentPlaygroundViewModel)BindingContext).TextRequested += OnTextRequested;
    }

    protected override void OnDisappearing()
    {
        ((DocumentPlaygroundViewModel)BindingContext).TextRequested -= OnTextRequested;
        base.OnDisappearing();
    }

    private async void OnTextRequested(
        object? sender,
        DocumentTextRequestedEventArgs e)
    {
        await Navigation.PushModalAsync(new DocumentTextViewerPage(e.Title, e.Content));
    }
}
