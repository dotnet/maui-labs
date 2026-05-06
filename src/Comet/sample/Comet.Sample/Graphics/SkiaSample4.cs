using System;
using System.Collections.Generic;
using System.Text;
using static Comet.CometControls;
namespace Comet.Samples
{
	public class SkiaSample4 : Component
	{
		readonly Reactive<double> _strokeSize = 2;
		readonly Reactive<Color> _strokeColor = Colors.White ;

				public override View Render()
		{
			var fingerPaint = new BindableFingerPaint(
				strokeSize: _strokeSize,
				strokeColor: _strokeColor);

			return VStack(
				VStack(
					HStack(
						Text("Stroke Width:"),
						Slider(_strokeSize, 1d, 10d).FillHorizontal()
					),
					HStack(
						Text("Stroke Color:"),
						TextField(() => _strokeColor.Value.ToArgbHex()).OnTextChanged(s => _strokeColor.Value = Color.FromArgb(s))
					),
					Button("Reset", () => fingerPaint.Reset()),
					fingerPaint.Frame(height: 400)
				)
			);
		}
	}
}
