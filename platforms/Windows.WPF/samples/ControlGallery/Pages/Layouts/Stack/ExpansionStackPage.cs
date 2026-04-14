using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ControlGallery.Pages.Layouts
{	public class ExpansionStackPage : ContentPage
	{
		public ExpansionStackPage()
		{
			BackgroundColor = Colors.White;
			this.Content = new StackLayout
			{
				Margin = new Thickness(20),
				Children = {
					new BoxView { BackgroundColor = Colors.Red, HeightRequest = 1 },
					new Label { Text = "Start", BackgroundColor = Colors.Gray, VerticalOptions = LayoutOptions.Start },
					new BoxView { BackgroundColor = Colors.Red, HeightRequest = 1 },
					new Label { Text = "Center", BackgroundColor = Colors.Gray, VerticalOptions = LayoutOptions.Center },
					new BoxView { BackgroundColor = Colors.Red, HeightRequest = 1 },
					new Label { Text = "End", BackgroundColor = Colors.Gray, VerticalOptions = LayoutOptions.End },
					new BoxView { BackgroundColor = Colors.Red, HeightRequest = 1 },
					new Label { Text = "Fill", BackgroundColor = Colors.Gray, VerticalOptions = LayoutOptions.Fill },
					new BoxView { BackgroundColor = Colors.Red, HeightRequest = 1 }
				}
			};
		}
	}
}
