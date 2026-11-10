using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Pages;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Initialize();
    }

    private async void OnProductsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//main/products");

    private async void OnOrdersClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//main/orders");
}
