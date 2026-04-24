using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class ShapesPage : View
	{
		[Body]
		View body() => GalleryPageHelpers.Scaffold("Shapes",
			GalleryPageHelpers.SectionHeader("Rectangle"),
			new ShapeView(new RoundedRectangle(8)
				.Fill(Colors.DodgerBlue)
				.Stroke(Colors.Navy, 3))
				.Frame(width: 160, height: 80)
				.FitHorizontal(),

			GalleryPageHelpers.SectionHeader("Ellipse"),
			new ShapeView(new Ellipse()
				.Fill(Colors.Coral)
				.Stroke(Colors.DarkRed, 2))
				.Frame(width: 160, height: 90)
				.FitHorizontal(),

			GalleryPageHelpers.Separator(),

			GalleryPageHelpers.SectionHeader("Line"),
			new ShapeView(new Line(0, 0, 200, 40)
				.Stroke(Colors.MediumSeaGreen, 3))
				.Frame(width: 210, height: 50)
				.FitHorizontal(),

			GalleryPageHelpers.SectionHeader("Line (Dashed)"),
			new ShapeView(WithDash(new Line(0, 0, 250, 0)
				.Stroke(Colors.MediumPurple, 3), 4f, 2f))
				.Frame(width: 260, height: 10)
				.FitHorizontal(),

			GalleryPageHelpers.Separator(),

			GalleryPageHelpers.SectionHeader("Polyline"),
			new ShapeView(new Polyline(
				new PointF(0, 40), new PointF(30, 0), new PointF(60, 40),
				new PointF(90, 10), new PointF(120, 40), new PointF(150, 5),
				new PointF(180, 40))
				.Stroke(Colors.Orange, 3))
				.Frame(width: 190, height: 50)
				.FitHorizontal(),

			GalleryPageHelpers.SectionHeader("Polygon"),
			new ShapeView(new Polygon(
				new PointF(60, 0), new PointF(120, 40),
				new PointF(100, 100), new PointF(20, 100),
				new PointF(0, 40))
				.Fill(Color.FromArgb("#882ecc71"))
				.Stroke(Colors.MediumSeaGreen, 2))
				.Frame(width: 130, height: 110)
				.FitHorizontal(),

			GalleryPageHelpers.Separator(),

			GalleryPageHelpers.SectionHeader("Path"),
			new ShapeView(new Path(BuildCurvePath())
				.Stroke(Colors.Crimson, 3))
				.Frame(width: 360, height: 120)
				.FitHorizontal(),

			GalleryPageHelpers.Separator(),

			GalleryPageHelpers.SectionHeader("Dash Patterns"),
			new ShapeView(WithDash(new Line(0, 0, 300, 0)
				.Stroke(Colors.SteelBlue, 3), 1f, 1f))
				.Frame(width: 310, height: 10)
				.FitHorizontal(),
			Text("Dots: {1, 1}")
				.FontSize(11)
				.Color(Colors.Grey),
			new ShapeView(WithDash(new Line(0, 0, 300, 0)
				.Stroke(Colors.SteelBlue, 3), 6f, 2f))
				.Frame(width: 310, height: 10)
				.FitHorizontal(),
			Text("Dash: {6, 2}")
				.FontSize(11)
				.Color(Colors.Grey),
			new ShapeView(WithDash(new Line(0, 0, 300, 0)
				.Stroke(Colors.SteelBlue, 3), 6f, 2f, 1f, 2f))
				.Frame(width: 310, height: 10)
				.FitHorizontal(),
			Text("Dash-Dot: {6, 2, 1, 2}")
				.FontSize(11)
				.Color(Colors.Grey)
		);

		static PathF BuildCurvePath()
		{
			var path = new PathF();
			path.MoveTo(10, 100);
			path.CurveTo(50, 0, 150, 0, 200, 80);
			path.CurveTo(250, 160, 300, 150, 350, 50);
			return path;
		}

		static TShape WithDash<TShape>(TShape shape, params float[] pattern) where TShape : Shape
		{
			shape.SetEnvironment("StrokeDashPattern", pattern, false);
			return shape;
		}
	}
}
