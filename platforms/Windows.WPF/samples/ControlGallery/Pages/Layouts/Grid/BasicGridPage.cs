using System;
using ControlGallery.Common;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace ControlGallery.Pages.Layouts
{
    public class BasicGridPage : ContentPage
    {
        public BasicGridPage()
        {
            BackgroundColor = Colors.White;
            Title = "Basic Grid";

            //Grid grid = new Grid
            //         {
            //             RowDefinitions =
            //             {
            //                 new RowDefinition { Height = new GridLength(2, GridUnitType.Star) },
            //                 new RowDefinition(),
            //                 new RowDefinition { Height = new GridLength(100) }
            //             },
            //             ColumnDefinitions =
            //             {
            //                 new ColumnDefinition(), // { Width = new GridLength(300)},
            //                 new ColumnDefinition() // {Width = new GridLength(300)}
            //             }
            //         };

            //         // Row 0
            //         // The BoxView and Label are in row 0 and column 0, and so only needs to be added to the
            //         // Grid.Children collection to get default row and column settings.
            //         grid.Children.Add(new BoxView
            //         {
            //             Color = Colors.Green
            //         });
            //         grid.Children.Add(new Label
            //         {
            //             Text = "Row 0, Column 0",
            //             HorizontalOptions = LayoutOptions.Center,
            //             VerticalOptions = LayoutOptions.Center
            //         });

            //         // This BoxView and Label are in row 0 and column 1, which are specified as arguments
            //         // to the Add method. 
            //         grid.Add(new BoxView
            //         {
            //             Color = Colors.Blue
            //         }, 1, 0);
            //         grid.Add(new Label
            //         {
            //             Text = "Row 0, Column 1",
            //             HorizontalOptions = LayoutOptions.Center,
            //             VerticalOptions = LayoutOptions.Center
            //         }, 1, 0);

            //         // Row 1
            //         // This BoxView and Label are in row 1 and column 0, which are specified as arguments
            //         // to the Add method overload.
            //         grid.AddWithSpan(new BoxView
            //         {
            //             Color = Colors.Teal
            //         }, 0, 1, 1, 2);
            //         grid.AddWithSpan(new Label
            //         {
            //             Text = "Row 1, Column 0",
            //             HorizontalOptions = LayoutOptions.Center,
            //             VerticalOptions = LayoutOptions.Center
            //         }, 0, 1, 1, 2); // These arguments indicate that that the child element goes in the column starting at 0 but ending before 1.
            // They also indicate that the child element goes in the row starting at 1 but ending before 2.

            //         grid.AddWithSpan(new BoxView
            //         {
            //             Color = Colors.Purple
            //         }, 1, 2, 1, 2);
            //         grid.AddWithSpan(new Label
            //         {
            //             Text = "Row1, Column 1",
            //             HorizontalOptions = LayoutOptions.Center,
            //             VerticalOptions = LayoutOptions.Center
            //         }, 1, 2, 1, 2);

            //         // Row 2
            //         // Alternatively, the BoxView and Label can be positioned in cells with the Grid.SetRow
            //         // and Grid.SetColumn methods.
            //         BoxView boxView = new BoxView { Color = Colors.Red };
            //         Grid.SetRow(boxView, 2);
            //         Grid.SetColumnSpan(boxView, 2);
            //         Label label = new Label
            //         {
            //             Text = "Row 2, Column 0 and 1",
            //             HorizontalOptions = LayoutOptions.Center,
            //             VerticalOptions = LayoutOptions.Center
            //         };
            //         Grid.SetRow(label, 2);
            //         Grid.SetColumnSpan(label, 2);

            //         grid.Children.Add(boxView);
            //         grid.Children.Add(label);

            //         // var grid = new Grid
            //         //     {
            //         //         WidthRequest = 300,
            //         //         HeightRequest = 300,
            //         //         BackgroundColor = Colors.Red,
            //         //         HorizontalOptions = LayoutOptions.Start,
            //         //         VerticalOptions = LayoutOptions.Start
            //         //     };
            //         //     grid.Children.Add(new Grid(){
            //         //         BackgroundColor = Colors.Blue,
            //         //         HorizontalOptions = LayoutOptions.Fill,
            //         //         VerticalOptions = LayoutOptions.Fill
            //         //     });

            //         this.Content = grid;

            var frame = new Border
            {
                BackgroundColor = Colors.Red,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HeightRequest = 60,
                WidthRequest = 60,
                StrokeShape = new RoundRectangle { CornerRadius = 30 },
                Margin = 10
            };

            Grid.SetRow(frame, 1);
            Grid.SetColumn(frame, 1);

            var grid = new Grid()
            {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.LightGray,
                ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
                RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },

            },
                Children =
            {
                frame
            }
            };

            Content = grid;
        }
    }
}
