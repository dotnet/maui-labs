using MauiReference.Models;
using MauiReference.PageModels;

namespace MauiReference.Pages;

public partial class MainPage : ContentPage
{
	public MainPage(MainPageModel model)
	{
		InitializeComponent();
		BindingContext = model;
	}
}