using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ControlGallery.Pages.Layouts
{	public class AlignmentStackPage : ContentPage
	{
		public AlignmentStackPage()
		{
			Padding = new Thickness(0);
			BackgroundColor = Colors.White;

			this.Content = new ScrollView(){
				Content = new StackLayout
				{
					Margin = new Thickness(20),
					Children =
					{
						new Label { Text = "Start", BackgroundColor = Colors.Gray, HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(10, 8) },
						new Label { Text = "Center", BackgroundColor = Colors.Gray, HorizontalOptions = LayoutOptions.Center, Padding = new Thickness(10, 8) },
						new Label { Text = "End", BackgroundColor = Colors.Gray, HorizontalOptions = LayoutOptions.End, Padding = new Thickness(10, 8) },
						new Label { Text = "Fill", BackgroundColor = Colors.Gray, HorizontalOptions = LayoutOptions.Fill, Padding = new Thickness(10, 8) }
					}
				}
			};
		}
	}
}
