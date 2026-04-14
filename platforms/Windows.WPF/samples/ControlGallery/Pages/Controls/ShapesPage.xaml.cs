using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace ControlGallery.Pages
{
    public partial class ShapesPage : ContentPage
    {
        public ShapesPage()
        {
            InitializeComponent();
            AddPolygonsManually();
        }

        void AddPolygonsManually()
        {
            try
            {
                var primaryBrush = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#512BD4"));

                // Octagon
                var octagon = new Microsoft.Maui.Controls.Shapes.Polygon
                {
                    Fill = primaryBrush, WidthRequest = 50, HeightRequest = 50,
                    Points = new PointCollection
                    {
                        new(0, 36), new(0, 13.5), new(13.5, 0), new(36, 0),
                        new(50, 13.5), new(50, 36), new(36, 50), new(13.5, 50),
                    }
                };
                shapesLayout.Children.Insert(2, octagon);

                // Arrow/Pentagon
                var arrow = new Microsoft.Maui.Controls.Shapes.Polygon
                {
                    Fill = primaryBrush, WidthRequest = 50, HeightRequest = 50,
                    Points = new PointCollection
                    {
                        new(0, 14), new(12, 0), new(38, 0), new(50, 14), new(25, 50),
                    }
                };
                shapesLayout.Children.Insert(3, arrow);

                // Pentagon
                var pentagon = new Microsoft.Maui.Controls.Shapes.Polygon
                {
                    Fill = primaryBrush, WidthRequest = 50, HeightRequest = 50, Margin = new Thickness(0, 10),
                    Points = new PointCollection
                    {
                        new(24, 0), new(47.776, 17.275), new(38.695, 45.225),
                        new(9.305, 45.225), new(0.224, 17.275),
                    }
                };
                shapesLayout.Children.Insert(4, pentagon);

                // Grid with 4 triangles (diamond pattern) — already in XAML but polygons missing
                // Find the grid and add polygons to it
                var grid = shapesLayout.Children.OfType<Grid>().FirstOrDefault();
                if (grid != null)
                {
                    grid.Children.Clear();
                    grid.Children.Add(new Microsoft.Maui.Controls.Shapes.Polygon
                    {
                        Fill = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#512BD4")),
                        Points = new PointCollection { new(0, 0), new(0, 50), new(25, 25) }
                    });
                    grid.Children.Add(new Microsoft.Maui.Controls.Shapes.Polygon
                    {
                        Fill = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#DFD8F7")),
                        Points = new PointCollection { new(0, 0), new(50, 0), new(25, 25) }
                    });
                    grid.Children.Add(new Microsoft.Maui.Controls.Shapes.Polygon
                    {
                        Fill = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#2B0B98")),
                        Points = new PointCollection { new(50, 0), new(50, 50), new(25, 25) }
                    });
                    grid.Children.Add(new Microsoft.Maui.Controls.Shapes.Polygon
                    {
                        Fill = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#E5E5E1")),
                        Points = new PointCollection { new(0, 50), new(25, 25), new(50, 50) }
                    });
                }

                // Diamond
                var diamond = new Microsoft.Maui.Controls.Shapes.Polygon
                {
                    Fill = primaryBrush, WidthRequest = 50, HeightRequest = 50, Margin = new Thickness(0, 10),
                    Points = new PointCollection
                    {
                        new(8, 25), new(25, 0), new(42, 25), new(25, 50),
                    }
                };
                // Insert before the Path (which is the last item)
                var pathIndex = shapesLayout.Children.Count - 1;
                shapesLayout.Children.Insert(pathIndex, diamond);
            }
            catch (Exception ex)
            {
                shapesLayout.Children.Insert(2, new Label { Text = $"Polygon error: {ex.Message}", TextColor = Microsoft.Maui.Graphics.Colors.Red });
            }
        }
    }
}