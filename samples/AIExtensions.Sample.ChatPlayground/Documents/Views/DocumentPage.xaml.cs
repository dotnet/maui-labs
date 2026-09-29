namespace AIExtensions.Sample.ChatPlayground;

public partial class DocumentPage : ContentPage
{
    public DocumentPage(DocumentPlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ((DocumentPlaygroundViewModel)BindingContext).ApplyLaunchOptionsAsync();
    }
}
