using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class GraphicsPage : ContentPage
{
	public GraphicsPage()
	{
		Title = "Graphics";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 16,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "Basic Shapes", FontSize = 16, FontAttributes = FontAttributes.Bold },
					new GraphicsView { HeightRequest = 140, Drawable = new ShapesDrawable() },

					new Label { Text = "Color Palette", FontSize = 16, FontAttributes = FontAttributes.Bold },
					new GraphicsView { HeightRequest = 100, Drawable = new PaletteDrawable() },

					new Label { Text = "Chart Demo", FontSize = 16, FontAttributes = FontAttributes.Bold },
					new GraphicsView { HeightRequest = 200, Drawable = new ChartDrawable() },

					new Label { Text = "Nested Shapes & Text", FontSize = 16, FontAttributes = FontAttributes.Bold },
					new GraphicsView { HeightRequest = 160, Drawable = new CompositeDrawable() },
				}
			}
		};
	}

	class ShapesDrawable : IDrawable
	{
		public void Draw(ICanvas canvas, RectF rect)
		{
			canvas.FillColor = Colors.Gray.WithAlpha(0.1f);
			canvas.FillRectangle(0, 0, rect.Width, rect.Height);

			canvas.StrokeColor = Colors.DodgerBlue;
			canvas.StrokeSize = 3;
			canvas.DrawCircle(60, 70, 45);
			canvas.FontSize = 11;
			canvas.FontColor = Colors.DodgerBlue;
			canvas.DrawString("Circle", 15, 115, 90, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

			canvas.FillColor = Colors.Coral;
			canvas.FillEllipse(140, 35, 90, 60);
			canvas.FontColor = Colors.Coral;
			canvas.DrawString("Ellipse", 140, 115, 90, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

			canvas.StrokeColor = Colors.MediumSeaGreen;
			canvas.StrokeSize = 2;
			canvas.DrawRectangle(270, 30, 80, 70);
			canvas.FontColor = Colors.MediumSeaGreen;
			canvas.DrawString("Rectangle", 260, 115, 100, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

			canvas.FillColor = Colors.MediumPurple;
			canvas.FillRoundedRectangle(390, 30, 90, 70, 12);
			canvas.FontColor = Colors.MediumPurple;
			canvas.DrawString("Rounded", 390, 115, 90, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

			canvas.StrokeColor = Colors.Crimson;
			canvas.StrokeSize = 3;
			canvas.DrawLine(520, 30, 580, 100);
			canvas.FontColor = Colors.Crimson;
			canvas.DrawString("Line", 520, 115, 60, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
		}
	}

	class PaletteDrawable : IDrawable
	{
		public void Draw(ICanvas canvas, RectF rect)
		{
			var colors = new[]
			{
				("#e74c3c", "Red"), ("#e67e22", "Orange"), ("#f1c40f", "Yellow"),
				("#2ecc71", "Green"), ("#3498db", "Blue"), ("#9b59b6", "Purple"),
				("#1abc9c", "Teal"), ("#34495e", "Dark"), ("#95a5a6", "Gray"),
			};

			float blockW = Math.Min(60, (rect.Width - 20) / colors.Length);
			float x = 10;

			foreach (var (hex, name) in colors)
			{
				canvas.FillColor = Color.FromArgb(hex);
				canvas.FillRoundedRectangle(x, 10, blockW - 4, 50, 6);

				canvas.FontSize = 9;
				canvas.FontColor = Colors.Gray;
				canvas.DrawString(name, x, 70, blockW - 4, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

				x += blockW;
			}
		}
	}

	class ChartDrawable : IDrawable
	{
		public void Draw(ICanvas canvas, RectF rect)
		{
			canvas.FillColor = Colors.Gray.WithAlpha(0.1f);
			canvas.FillRectangle(0, 0, rect.Width, rect.Height);

			var data = new[] { 35, 65, 45, 80, 55, 70, 40, 90, 60, 75 };
			var labels = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct" };
			var barColors = new[] { Colors.DodgerBlue, Colors.Coral, Colors.MediumSeaGreen, Colors.Orange,
				Colors.MediumPurple, Colors.Teal, Colors.Salmon, Colors.SkyBlue, Colors.Gold, Colors.SlateBlue };

			float chartLeft = 40, chartBottom = rect.Height - 30;
			float chartHeight = chartBottom - 20;
			float barWidth = (rect.Width - chartLeft - 20) / data.Length;

			canvas.StrokeColor = Colors.Gray;
			canvas.StrokeSize = 1;
			canvas.DrawLine(chartLeft, 10, chartLeft, chartBottom);
			canvas.DrawLine(chartLeft, chartBottom, rect.Width - 10, chartBottom);

			for (int i = 0; i < data.Length; i++)
			{
				float barH = (data[i] / 100f) * chartHeight;
				float x = chartLeft + i * barWidth + 4;
				float y = chartBottom - barH;

				canvas.FillColor = barColors[i];
				canvas.FillRoundedRectangle(x, y, barWidth - 8, barH, 3);

				canvas.FontSize = 10;
				canvas.FontColor = Colors.Gray;
				canvas.DrawString(data[i].ToString(), x, y - 14, barWidth - 8, 14, HorizontalAlignment.Center, VerticalAlignment.Center);

				canvas.FontSize = 9;
				canvas.DrawString(labels[i], x, chartBottom + 4, barWidth - 8, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
			}

			canvas.FontSize = 13;
			canvas.FontColor = Colors.Gray;
			canvas.DrawString("Monthly Performance", rect.Width / 2, 8, HorizontalAlignment.Center);
		}
	}

	class CompositeDrawable : IDrawable
	{
		public void Draw(ICanvas canvas, RectF rect)
		{
			canvas.FillColor = Color.FromArgb("#1a1a2e");
			canvas.FillRectangle(0, 0, rect.Width, rect.Height);

			canvas.FillColor = Color.FromRgba(231, 76, 60, 100);
			canvas.FillCircle(120, 70, 50);
			canvas.FillColor = Color.FromRgba(46, 204, 113, 100);
			canvas.FillCircle(160, 70, 50);
			canvas.FillColor = Color.FromRgba(52, 152, 219, 100);
			canvas.FillCircle(140, 40, 50);

			canvas.FontSize = 20;
			canvas.FontColor = Colors.White;
			canvas.DrawString("CoreGraphics Rendering", 250, 30, 300, 30, HorizontalAlignment.Left, VerticalAlignment.Center);

			canvas.FontSize = 13;
			canvas.FontColor = Color.FromArgb("#bdc3c7");
			canvas.DrawString("Shapes · Text · Colors · Transforms", 250, 60, 350, 20, HorizontalAlignment.Left, VerticalAlignment.Center);

			canvas.StrokeColor = Color.FromArgb("#e74c3c");
			canvas.StrokeSize = 2;
			canvas.DrawRoundedRectangle(250, 90, 120, 40, 8);
			canvas.FontColor = Color.FromArgb("#e74c3c");
			canvas.FontSize = 12;
			canvas.DrawString("DrawRoundedRect", 255, 95, 110, 30, HorizontalAlignment.Center, VerticalAlignment.Center);

			canvas.StrokeColor = Color.FromArgb("#3498db");
			canvas.DrawRoundedRectangle(390, 90, 100, 40, 8);
			canvas.FontColor = Color.FromArgb("#3498db");
			canvas.DrawString("DrawCircle", 395, 95, 90, 30, HorizontalAlignment.Center, VerticalAlignment.Center);
		}
	}
}
