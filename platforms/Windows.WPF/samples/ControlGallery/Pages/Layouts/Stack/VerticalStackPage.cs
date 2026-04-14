using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ControlGallery.Pages.Layouts;
public class VerticalStackPage : ContentPage
{
    public VerticalStackPage()
    {
        this.Content = new VerticalStackLayout
        {
            Margin = new Thickness(20),
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
        };
    }

    BoxView ColorView(Color c)
    {
        return new BoxView { Color = c }.Size(50).Start();
    }
}