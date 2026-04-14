using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class ShapesPage : ContentPage
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
					SectionHeader("Rectangle"),
					new Rectangle
					{
						Fill = new SolidColorBrush(Colors.DodgerBlue),
						Stroke = new SolidColorBrush(Colors.Navy),
						StrokeThickness = 3,
						WidthRequest = 160,
						HeightRequest = 80,
						RadiusX = 8,
						RadiusY = 8,
						HorizontalOptions = LayoutOptions.Start,
					},

					SectionHeader("Ellipse"),
					new Ellipse
					{
						Fill = new SolidColorBrush(Colors.Coral),
						Stroke = new SolidColorBrush(Colors.DarkRed),
						StrokeThickness = 2,
						WidthRequest = 160,
						HeightRequest = 90,
						HorizontalOptions = LayoutOptions.Start,
					},

					Separator(),

					SectionHeader("Line"),
					new Line
					{
						X1 = 0, Y1 = 0,
						X2 = 200, Y2 = 40,
						Stroke = new SolidColorBrush(Colors.MediumSeaGreen),
						StrokeThickness = 3,
						HorizontalOptions = LayoutOptions.Start,
					},

					SectionHeader("Line (Dashed)"),
					new Line
					{
						X1 = 0, Y1 = 0,
						X2 = 250, Y2 = 0,
						Stroke = new SolidColorBrush(Colors.MediumPurple),
						StrokeThickness = 3,
						StrokeDashArray = new DoubleCollection { 4, 2 },
						HeightRequest = 10,
						HorizontalOptions = LayoutOptions.Start,
					},

					Separator(),

					SectionHeader("Polyline"),
					new Polyline
					{
						Points = new PointCollection
						{
							new Point(0, 40), new Point(30, 0), new Point(60, 40),
							new Point(90, 10), new Point(120, 40), new Point(150, 5),
							new Point(180, 40),
						},
						Stroke = new SolidColorBrush(Colors.Orange),
						StrokeThickness = 3,
						HorizontalOptions = LayoutOptions.Start,
						HeightRequest = 50,
					},

					SectionHeader("Polygon"),
					new Polygon
					{
						Points = new PointCollection
						{
							new Point(60, 0), new Point(120, 40),
							new Point(100, 100), new Point(20, 100),
							new Point(0, 40),
						},
						Fill = new SolidColorBrush(Color.FromArgb("#882ecc71")),
						Stroke = new SolidColorBrush(Colors.MediumSeaGreen),
						StrokeThickness = 2,
						HorizontalOptions = LayoutOptions.Start,
						HeightRequest = 110,
					},

					Separator(),

					SectionHeader("Path"),
					new Microsoft.Maui.Controls.Shapes.Path
					{
						Data = (Geometry)new PathGeometryConverter().ConvertFromString(
							"M 10,100 C 50,0 150,0 200,80 S 300,150 350,50"),
						Stroke = new SolidColorBrush(Colors.Crimson),
						StrokeThickness = 3,
						HorizontalOptions = LayoutOptions.Start,
						HeightRequest = 120,
					},

					Separator(),

					SectionHeader("Dash Patterns"),
					new Line
					{
						X1 = 0, Y1 = 0, X2 = 300, Y2 = 0,
						Stroke = new SolidColorBrush(Colors.SteelBlue),
						StrokeThickness = 3,
						StrokeDashArray = new DoubleCollection { 1, 1 },
						HeightRequest = 10,
						HorizontalOptions = LayoutOptions.Start,
					},
					new Label { Text = "Dots: {1, 1}", FontSize = 11, TextColor = Colors.Gray },
					new Line
					{
						X1 = 0, Y1 = 0, X2 = 300, Y2 = 0,
						Stroke = new SolidColorBrush(Colors.SteelBlue),
						StrokeThickness = 3,
						StrokeDashArray = new DoubleCollection { 6, 2 },
						HeightRequest = 10,
						HorizontalOptions = LayoutOptions.Start,
					},
					new Label { Text = "Dash: {6, 2}", FontSize = 11, TextColor = Colors.Gray },
					new Line
					{
						X1 = 0, Y1 = 0, X2 = 300, Y2 = 0,
						Stroke = new SolidColorBrush(Colors.SteelBlue),
						StrokeThickness = 3,
						StrokeDashArray = new DoubleCollection { 6, 2, 1, 2 },
						HeightRequest = 10,
						HorizontalOptions = LayoutOptions.Start,
					},
					new Label { Text = "Dash-Dot: {6, 2, 1, 2}", FontSize = 11, TextColor = Colors.Gray },
				}
			}
		};
	}

	static Label SectionHeader(string text) => new()
	{
		Text = text,
		FontSize = 16,
		FontAttributes = FontAttributes.Bold,
		TextColor = Colors.CornflowerBlue,
	};

	static Border Separator() => new() { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 };
}
