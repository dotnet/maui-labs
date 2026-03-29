using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

class ShapesPage : ContentPage
{
	public ShapesPage()
	{
		Title = "Shapes";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 16,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "Shapes (Cairo)", FontSize = 22, FontAttributes = FontAttributes.Bold },

					// Rectangle
					new Label { Text = "Rectangle", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Microsoft.Maui.Controls.Shapes.Rectangle
					{
						Fill = new SolidColorBrush(Colors.CornflowerBlue),
						Stroke = new SolidColorBrush(Colors.DarkBlue),
						StrokeThickness = 2,
						WidthRequest = 200,
						HeightRequest = 60,
						HorizontalOptions = LayoutOptions.Start,
					},

					// Rounded Rectangle
					new Label { Text = "Rounded Rectangle", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Microsoft.Maui.Controls.Shapes.Rectangle
					{
						Fill = new SolidColorBrush(Colors.MediumSeaGreen),
						Stroke = new SolidColorBrush(Colors.DarkGreen),
						StrokeThickness = 2,
						RadiusX = 15,
						RadiusY = 15,
						WidthRequest = 200,
						HeightRequest = 60,
						HorizontalOptions = LayoutOptions.Start,
					},

					// Ellipse
					new Label { Text = "Ellipse", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Ellipse
					{
						Fill = new SolidColorBrush(Colors.Orchid),
						Stroke = new SolidColorBrush(Colors.DarkMagenta),
						StrokeThickness = 2,
						WidthRequest = 180,
						HeightRequest = 80,
						HorizontalOptions = LayoutOptions.Start,
					},

					// Circle (equal width/height ellipse)
					new Label { Text = "Circle", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Ellipse
					{
						Fill = new SolidColorBrush(Colors.Gold),
						Stroke = new SolidColorBrush(Colors.DarkOrange),
						StrokeThickness = 3,
						WidthRequest = 80,
						HeightRequest = 80,
						HorizontalOptions = LayoutOptions.Start,
					},

					// Line
					new Label { Text = "Line", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Line
					{
						X1 = 0, Y1 = 0,
						X2 = 200, Y2 = 40,
						Stroke = new SolidColorBrush(Colors.Tomato),
						StrokeThickness = 3,
						WidthRequest = 200,
						HeightRequest = 40,
						HorizontalOptions = LayoutOptions.Start,
					},

					// Polygon (triangle)
					new Label { Text = "Polygon (Triangle)", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Polygon
					{
						Points = new PointCollection
						{
							new Point(60, 0),
							new Point(120, 100),
							new Point(0, 100),
						},
						Fill = new SolidColorBrush(Colors.SteelBlue),
						Stroke = new SolidColorBrush(Colors.Navy),
						StrokeThickness = 2,
						WidthRequest = 120,
						HeightRequest = 100,
						HorizontalOptions = LayoutOptions.Start,
					},

					// Polyline (zigzag)
					new Label { Text = "Polyline (Zigzag)", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Polyline
					{
						Points = new PointCollection
						{
							new Point(0, 40), new Point(30, 0), new Point(60, 40),
							new Point(90, 0), new Point(120, 40), new Point(150, 0),
							new Point(180, 40),
						},
						Stroke = new SolidColorBrush(Colors.DeepPink),
						StrokeThickness = 3,
						WidthRequest = 180,
						HeightRequest = 40,
						HorizontalOptions = LayoutOptions.Start,
					},

					// Path (heart shape via bezier curves)
					new Label { Text = "Path (Heart)", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Microsoft.Maui.Controls.Shapes.Path
					{
						Data = (Geometry)new PathGeometryConverter().ConvertFromString(
							"M 50,20 C 50,0 25,0 25,20 C 25,40 50,60 50,60 C 50,60 75,40 75,20 C 75,0 50,0 50,20 Z")!,
						Fill = new SolidColorBrush(Colors.Red),
						Stroke = new SolidColorBrush(Colors.DarkRed),
						StrokeThickness = 2,
						WidthRequest = 100,
						HeightRequest = 80,
						HorizontalOptions = LayoutOptions.Start,
					},

					// Dashed rectangle
					new Label { Text = "Dashed Stroke", FontSize = 14, FontAttributes = FontAttributes.Bold },
					new Microsoft.Maui.Controls.Shapes.Rectangle
					{
						Stroke = new SolidColorBrush(Colors.Gray),
						StrokeThickness = 2,
						StrokeDashArray = new DoubleCollection { 4, 2 },
						WidthRequest = 200,
						HeightRequest = 50,
						HorizontalOptions = LayoutOptions.Start,
					},
				}
			}
		};
	}
}
