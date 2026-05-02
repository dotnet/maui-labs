using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A line shape from (X1,Y1) to (X2,Y2).
	/// Matches MAUI's Microsoft.Maui.Controls.Shapes.Line.
	/// </summary>
	public class Line : Shape
	{
		public double X1 { get; set; }
		public double Y1 { get; set; }
		public double X2 { get; set; }
		public double Y2 { get; set; }

		public Line() { }

		public Line(double x1, double y1, double x2, double y2)
		{
			X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
		}

		public override PathF PathForBounds(Rect rect)
		{
			var path = new PathF();
			path.MoveTo((float)X1, (float)Y1);
			path.LineTo((float)X2, (float)Y2);
			return path;
		}
	}

	/// <summary>
	/// A closed polygon shape defined by a series of points.
	/// Matches MAUI's Microsoft.Maui.Controls.Shapes.Polygon.
	/// </summary>
	public class Polygon : Shape
	{
		public PointF[] Points { get; set; } = System.Array.Empty<PointF>();

		public Polygon() { }

		public Polygon(params PointF[] points)
		{
			Points = points;
		}

		public override PathF PathForBounds(Rect rect)
		{
			var path = new PathF();
			if (Points.Length == 0) return path;

			path.MoveTo(Points[0]);
			for (int i = 1; i < Points.Length; i++)
				path.LineTo(Points[i]);
			path.Close();
			return path;
		}
	}

	/// <summary>
	/// An open polyline shape defined by a series of points.
	/// Matches MAUI's Microsoft.Maui.Controls.Shapes.Polyline.
	/// </summary>
	public class Polyline : Shape
	{
		public PointF[] Points { get; set; } = System.Array.Empty<PointF>();

		public Polyline() { }

		public Polyline(params PointF[] points)
		{
			Points = points;
		}

		public override PathF PathForBounds(Rect rect)
		{
			var path = new PathF();
			if (Points.Length == 0) return path;

			path.MoveTo(Points[0]);
			for (int i = 1; i < Points.Length; i++)
				path.LineTo(Points[i]);
			return path;
		}
	}
}
