using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample;

public class GraphicsViewPage : ContentPage
{
    public GraphicsViewPage()
    {
        Title = "GraphicsView";
        this.WithPageBackground();

        var backButton = new Button
        {
            Text = "← Back",
            BackgroundColor = AppColors.AccentBlue,
            TextColor = Colors.White,
        };
        backButton.Clicked += async (s, e) => await Navigation.PopAsync();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(40, 20),
                Spacing = 20,
                Children =
                {
                    backButton,

                    new Label
                    {
                        Text = "GraphicsView Demo",
                        FontSize = 32,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                    }.WithPrimaryText(),
                    new Label
                    {
                        Text = "Custom drawing via IDrawable + CoreGraphics",
                        FontSize = 16,
                        HorizontalTextAlignment = TextAlignment.Center,
                    }.WithSecondaryText(),

                    new Label { Text = "Bar Chart", FontSize = 22, FontAttributes = FontAttributes.Bold }.WithSectionStyle(),
                    new GraphicsView
                    {
                        Drawable = new BarChartDrawable(),
                        HeightRequest = 220,
                    }.WithSurfaceBackground(),

                    new Label { Text = "Shapes", FontSize = 22, FontAttributes = FontAttributes.Bold }.WithSectionStyle(),
                    new GraphicsView
                    {
                        Drawable = new ShapesDrawable(),
                        HeightRequest = 200,
                    }.WithSurfaceBackground(),

                    new Label { Text = "Gradient Rings", FontSize = 22, FontAttributes = FontAttributes.Bold }.WithSectionStyle(),
                    new GraphicsView
                    {
                        Drawable = new RingsDrawable(),
                        HeightRequest = 200,
                    }.WithSurfaceBackground(),
                },
            },
        };
    }

    class BarChartDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var data = new[] { 0.7f, 0.4f, 0.9f, 0.55f, 0.8f, 0.3f };
            var colors = new[] { "#E74C3C", "#3498DB", "#2ECC71", "#9B59B6", "#F39C12", "#1ABC9C" };
            var labels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

            float padding = 40;
            float barWidth = (dirtyRect.Width - padding * 2) / data.Length;
            float maxBarHeight = dirtyRect.Height - 60;

            for (int i = 0; i < data.Length; i++)
            {
                float barHeight = data[i] * maxBarHeight;
                float x = padding + i * barWidth + 4;
                float y = dirtyRect.Height - 30 - barHeight;

                canvas.FillColor = Color.FromArgb(colors[i]);
                canvas.FillRoundedRectangle(x, y, barWidth - 8, barHeight, 4);

                canvas.FontColor = Colors.White;
                canvas.FontSize = 12;
                canvas.DrawString(labels[i], x, dirtyRect.Height - 20, barWidth - 8, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            canvas.FontColor = Colors.White;
            canvas.FontSize = 14;
            canvas.DrawString("Weekly Activity", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }

    class ShapesDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float cx = dirtyRect.Width / 2;
            float cy = dirtyRect.Height / 2;

            // Filled circle
            canvas.FillColor = Color.FromArgb("#4A90E2");
            canvas.FillCircle(cx - 120, cy, 50);

            // Stroked rectangle
            canvas.StrokeColor = Color.FromArgb("#E74C3C");
            canvas.StrokeSize = 3;
            canvas.DrawRoundedRectangle(cx - 40, cy - 40, 80, 80, 12);

            // Filled triangle via path
            canvas.FillColor = Color.FromArgb("#2ECC71");
            var path = new PathF();
            path.MoveTo(cx + 120, cy - 45);
            path.LineTo(cx + 165, cy + 40);
            path.LineTo(cx + 75, cy + 40);
            path.Close();
            canvas.FillPath(path);

            // Labels
            canvas.FontColor = Colors.White;
            canvas.FontSize = 12;
            canvas.DrawString("Circle", cx - 170, cy + 55, 100, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
            canvas.DrawString("Rectangle", cx - 50, cy + 55, 100, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
            canvas.DrawString("Triangle", cx + 70, cy + 55, 100, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }

    class RingsDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float cx = dirtyRect.Width / 2;
            float cy = dirtyRect.Height / 2;

            var rings = new (string color, float radius, float progress)[]
            {
                ("#E74C3C", 70, 0.75f),
                ("#2ECC71", 50, 0.60f),
                ("#4A90E2", 30, 0.90f),
            };

            foreach (var (color, radius, progress) in rings)
            {
                // Background ring
                canvas.StrokeColor = Color.FromArgb("#333333");
                canvas.StrokeSize = 10;
                canvas.DrawCircle(cx, cy, radius);

                // Progress arc
                canvas.StrokeColor = Color.FromArgb(color);
                canvas.StrokeSize = 10;
                canvas.StrokeLineCap = LineCap.Round;
                float sweepAngle = 360 * progress;
                canvas.DrawArc(cx - radius, cy - radius, radius * 2, radius * 2, 90, 90 - sweepAngle, false, false);
            }

            canvas.FontColor = Colors.White;
            canvas.FontSize = 14;
            canvas.DrawString("Activity Rings", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Bottom);
        }
    }
}
