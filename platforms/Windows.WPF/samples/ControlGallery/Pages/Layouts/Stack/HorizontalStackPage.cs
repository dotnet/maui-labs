using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ControlGallery.Pages.Layouts;
public class HorizontalStackPage : ContentPage
{
    public HorizontalStackPage()
    {
        this.Title = "Horizontal";
        this.Padding = 0;

        this.Content = new ScrollView { 
            Orientation = ScrollOrientation.Horizontal, 
            Content = new StackLayout
            {
                Orientation = StackOrientation.Horizontal,
                Spacing = 25,
                Children =
                {
                    ColorView(Colors.Red),
                    ColorView(Colors.Yellow),
                    ColorView(Colors.Blue),
                    ColorView(Colors.Green),
                    ColorView(Colors.Orange),
                    ColorView(Colors.Purple),
                    ColorView(Colors.Black),
                    ColorView(Colors.Red),
                    ColorView(Colors.Yellow),
                    ColorView(Colors.Blue),
                    ColorView(Colors.Green),
                    ColorView(Colors.Orange),
                    ColorView(Colors.Purple),
                    ColorView(Colors.Black),
                }
            }
            .Top()
        };
    }

    BoxView ColorView(Color c)
    {
        return new BoxView { Color = c }.Size(50);
    }
}