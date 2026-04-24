using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class GraphicsPage : View
	{
		[Body]
		View body() => GalleryPageHelpers.Scaffold("Graphics",
			GalleryPageHelpers.Section("Basic Shapes",
				new GraphicsView
				{
					Draw = (canvas, rect) =>
					{
						canvas.FillColor = Colors.Gray.WithAlpha(0.1f);
						canvas.FillRectangle(0, 0, rect.Width, rect.Height);

						// Adaptive: divide available width into 5 columns
						float col = rect.Width / 5f;
						float r = Math.Min(col * 0.4f, 45);

						canvas.StrokeColor = Colors.DodgerBlue;
						canvas.StrokeSize = 3;
						canvas.DrawCircle(col * 0.5f, 70, r);
						canvas.FontSize = 11;
						canvas.FontColor = Colors.DodgerBlue;
						canvas.DrawString("Circle", col * 0, 115, col, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

						canvas.FillColor = Colors.Coral;
						canvas.FillEllipse(col * 1 + 5, 35, col - 10, 60);
						canvas.FontColor = Colors.Coral;
						canvas.DrawString("Ellipse", col * 1, 115, col, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

						canvas.StrokeColor = Colors.MediumSeaGreen;
						canvas.StrokeSize = 2;
						canvas.DrawRectangle(col * 2 + 5, 30, col - 10, 70);
						canvas.FontColor = Colors.MediumSeaGreen;
						canvas.DrawString("Rectangle", col * 2, 115, col, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

						canvas.FillColor = Colors.MediumPurple;
						canvas.FillRoundedRectangle(col * 3 + 5, 30, col - 10, 70, 12);
						canvas.FontColor = Colors.MediumPurple;
						canvas.DrawString("Rounded", col * 3, 115, col, 20, HorizontalAlignment.Center, VerticalAlignment.Top);

						canvas.StrokeColor = Colors.Crimson;
						canvas.StrokeSize = 3;
						canvas.DrawLine(col * 4 + 10, 30, col * 5 - 10, 100);
						canvas.FontColor = Colors.Crimson;
						canvas.DrawString("Line", col * 4, 115, col, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
					}
				}.Frame(height: 140)
			),
			GalleryPageHelpers.Section("Color Palette",
				new GraphicsView
				{
					Draw = (canvas, rect) =>
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
				}.Frame(height: 100)
			),
			GalleryPageHelpers.Section("Chart Demo",
				new GraphicsView
				{
					Draw = (canvas, rect) =>
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
				}.Frame(height: 200)
			),
			GalleryPageHelpers.Section("Nested Shapes & Text",
				new GraphicsView
				{
					Draw = (canvas, rect) =>
					{
						canvas.FillColor = Color.FromArgb("#1a1a2e");
						canvas.FillRectangle(0, 0, rect.Width, rect.Height);

						// Adaptive: use proportional positions
						float cx = Math.Min(rect.Width * 0.25f, 140);
						canvas.FillColor = Color.FromRgba(231, 76, 60, 100);
						canvas.FillCircle(cx - 20, 70, 40);
						canvas.FillColor = Color.FromRgba(46, 204, 113, 100);
						canvas.FillCircle(cx + 20, 70, 40);
						canvas.FillColor = Color.FromRgba(52, 152, 219, 100);
						canvas.FillCircle(cx, 40, 40);

						float textX = Math.Min(rect.Width * 0.45f, 250);
						float textW = rect.Width - textX - 10;
						canvas.FontSize = Math.Min(20, rect.Width / 25);
						canvas.FontColor = Colors.White;
						canvas.DrawString("CoreGraphics Rendering", textX, 30, textW, 30, HorizontalAlignment.Left, VerticalAlignment.Center);

						canvas.FontSize = Math.Min(13, rect.Width / 35);
						canvas.FontColor = Color.FromArgb("#bdc3c7");
						canvas.DrawString("Shapes . Text . Colors . Transforms", textX, 60, textW, 20, HorizontalAlignment.Left, VerticalAlignment.Center);

						float btnW = Math.Min(120, (textW - 20) / 2);
						canvas.StrokeColor = Color.FromArgb("#e74c3c");
						canvas.StrokeSize = 2;
						canvas.DrawRoundedRectangle(textX, 90, btnW, 40, 8);
						canvas.FontColor = Color.FromArgb("#e74c3c");
						canvas.FontSize = 12;
						canvas.DrawString("DrawRoundedRect", textX + 5, 95, btnW - 10, 30, HorizontalAlignment.Center, VerticalAlignment.Center);

						canvas.StrokeColor = Color.FromArgb("#3498db");
						canvas.DrawRoundedRectangle(textX + btnW + 10, 90, btnW, 40, 8);
						canvas.FontColor = Color.FromArgb("#3498db");
						canvas.DrawString("DrawCircle", textX + btnW + 15, 95, btnW - 10, 30, HorizontalAlignment.Center, VerticalAlignment.Center);
					}
				}.Frame(height: 160)
			)
		);
	}
}
