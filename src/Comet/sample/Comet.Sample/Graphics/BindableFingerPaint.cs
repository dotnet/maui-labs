using System.Collections.Generic;
using Comet.Reactive;
namespace Comet.Samples
{
	public class BindableFingerPaint : SimpleFingerPaint, IDrawable, IGraphicsView
	{
		private PropertySubscription<double> _strokeWidth = 2;
		private PropertySubscription<Color> _strokeColor = Colors.Black;

		public BindableFingerPaint(
			PropertySubscription<double> strokeSize = null,
			PropertySubscription<Color> strokeColor = null)
		{
			StrokeWidth = strokeSize;
			StrokeColor = strokeColor;
		}

		public PropertySubscription<double> StrokeWidth
		{
			get => _strokeWidth;
			private set => this.SetPropertySubscription(ref _strokeWidth, value);
		}

		public PropertySubscription<Color> StrokeColor
		{
			get => _strokeColor;
			private set => this.SetPropertySubscription(ref _strokeColor, value);
		}

		public override void ViewPropertyChanged(string property, object value)
		{
			base.ViewPropertyChanged(property, value);
			Invalidate();
		}			

		void IDrawable.Draw(ICanvas canvas, RectF dirtyRect)
		{
			canvas.StrokeColor = _strokeColor;
			canvas.StrokeSize = (float)_strokeWidth.CurrentValue;

			foreach (var pointsList in _pointsLists)
			{
				for (var i = 0; i < pointsList.Count; i++)
				{
					var point = pointsList[i];
					if (i > 0)
					{
						var lastPoint = pointsList[i - 1];
						canvas.DrawLine(lastPoint.X, lastPoint.Y, point.X, point.Y);
					}
				}
			}
		}


		public override void Reset()
		{
			_pointsLists.Clear();
			Invalidate();
		}
	}
}
