using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using CommunityToolkit.Maui.Markup;

namespace ControlGallery.Pages.Layouts
{	public class CombinedStackPage : ContentPage
	{
		public CombinedStackPage()
		{
            this.Title = "Combined Stacks";

            BackgroundColor = Colors.White;
			this.Content = new StackLayout
            {
                Margin = new Thickness(20),
                Spacing = 8,
                Children =
                {
                    new Label { Text = "Primary colors" },
                    ColorView(Colors.Red),
                    ColorView(Colors.Yellow),
                    ColorView(Colors.Blue),
                    new Label { Text = "Secondary colors" },
                    ColorView(Colors.Green),
                    ColorView(Colors.Orange),
                    ColorView(Colors.Purple),
                }
            };
		}

        Border ColorView(Color c)
        {
            return new Border
                    {
                        Stroke = Colors.Black,
                        StrokeThickness = 1,
                        Content = new StackLayout
                        {
                            Orientation = StackOrientation.Horizontal,
                            Spacing = 15,
                            Children =
                            {
                                new BoxView{ Color = c }
                                    .Size(40)
                                    .Center() ,
                                new Label()
                                    .Text(c.ToHex())
                                    .FontSize(16)
                                    .Center()
                            }
                        }
                    }.Padding(5);
        }
	}
}
