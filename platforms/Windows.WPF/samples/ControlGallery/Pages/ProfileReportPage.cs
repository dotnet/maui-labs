using Microsoft.Maui.Controls.Internals;

namespace ControlGallery.Pages;

public class ProfileReportPage : ContentPage
{
	public ProfileReportPage()
	{
		this.LoadProfile();
		//Content = new VerticalStackLayout
		//{
		//	Children = {
		//		new Label { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Text = "Welcome to .NET MAUI!"
		//		}
		//	}
		//};
	}
}
