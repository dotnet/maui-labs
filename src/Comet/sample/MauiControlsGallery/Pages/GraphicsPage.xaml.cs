namespace MauiControlsGallery.Pages;

public partial class GraphicsPage : ContentPage
{
	public GraphicsPage()
	{
		InitializeComponent();
		BasicShapesView.Drawable = new BasicShapesDrawable();
		ColorPaletteView.Drawable = new ColorPaletteDrawable();
		ChartView.Drawable = new ChartDrawable();
	}
}

class BasicShapesDrawable : IDrawable
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

class ColorPaletteDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF rect)
	{
		var colors = new (string hex, string name)[]
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
		var barColors = new[] {
			Colors.DodgerBlue, Colors.Coral, Colors.MediumSeaGreen, Colors.Orange,
			Colors.MediumPurple, Colors.Teal, Colors.Salmon, Colors.SkyBlue, Colors.Gold, Colors.SlateBlue
		};

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
			float bx = chartLeft + i * barWidth + 4;
			float by = chartBottom - barH;

			canvas.FillColor = barColors[i];
			canvas.FillRoundedRectangle(bx, by, barWidth - 8, barH, 3);

			canvas.FontSize = 10;
			canvas.FontColor = Colors.Gray;
			canvas.DrawString(data[i].ToString(), bx, by - 14, barWidth - 8, 14, HorizontalAlignment.Center, VerticalAlignment.Center);

			canvas.FontSize = 9;
			canvas.DrawString(labels[i], bx, chartBottom + 4, barWidth - 8, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
		}

		canvas.FontSize = 13;
		canvas.FontColor = Colors.Gray;
		canvas.DrawString("Monthly Performance", rect.Width / 2, 8, HorizontalAlignment.Center);
	}
}
