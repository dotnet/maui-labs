using System;
using ControlGallery.Common;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ControlGallery.Pages.Layouts
{
   public class SpacingGridPage : ContentPage
   {
      public SpacingGridPage()
      {
         //BackgroundColor = Colors.White;
         Title = "Spacing";

         Grid grid = new Grid
         {
            RowSpacing = 8,
            ColumnSpacing = 8,
            RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(2, GridUnitType.Star) },
                    new RowDefinition(),
                    new RowDefinition { Height = new GridLength(100) }
                },
            ColumnDefinitions =
                {
                    new ColumnDefinition(),
                    new ColumnDefinition()
                }
         };

         // Row 0
         grid.Children.Add(new BoxView
         {
            Color = Colors.Green
         });
         grid.Children.Add(new Label
         {
            Text = "Row 0, Column 0",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
         });
         grid.AddWithSpan(new BoxView
         {
            Color = Colors.Blue
         }, 1, 0);
         grid.AddWithSpan(new Label
         {
            Text = "Row 0, Column 1",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
         }, 1, 0);

         // Row 1
         grid.AddWithSpan(new BoxView
         {
            Color = Colors.Teal
         }, 0, 1, 1, 2);
         grid.AddWithSpan(new Label
         {
            Text = "Row 1, Column 0",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
         }, 0, 1, 1, 2);
         grid.AddWithSpan(new BoxView
         {
            Color = Colors.Purple
         }, 1, 2, 1, 2);
         grid.AddWithSpan(new Label
         {
            Text = "Row1, Column 1",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
         }, 1, 2, 1, 2);

         //// Row 2
         BoxView boxView = new BoxView { Color = Colors.Red };
         Grid.SetRow(boxView, 2);
         Grid.SetColumnSpan(boxView, 2);
         Label label = new Label
         {
            Text = "Row 2, Column 0 and 1",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
         };
         Grid.SetRow(label, 2);
         Grid.SetColumnSpan(label, 2);

         grid.Children.Add(boxView);
         grid.Children.Add(label);

         this.Content = grid;
      }
   }
}
