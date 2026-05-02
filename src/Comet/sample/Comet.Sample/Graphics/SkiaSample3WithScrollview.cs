using System;
using System.Collections.Generic;
using System.Text;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class SkiaSample3WithScrollView : Component
	{
		readonly Reactive<double> _strokeSize = 2;
		readonly Reactive<Color> _strokeColor = Colors.Black;

				public override View Render() => VStack(
			VStack(
				HStack(
					Text("Stroke Width:"),
					Slider(_strokeSize, 1d, 10d).FillHorizontal()
				),
				HStack(
					Text("Stroke Color!:")
				),
				ScrollView(Orientation.Horizontal,
					HStack(8,
						Button("Black", () =>
						{
							_strokeColor.Value = Colors.Black;
						}),
						Button("Blue", () =>
						{
							_strokeColor.Value = Colors.Blue;
						}),
						Button("Red", () =>
						{
							_strokeColor.Value = Colors.Red;
						}),
						Button("Green", () =>
						{
							_strokeColor.Value = Colors.Green;
						}),
						Button("Orange", () =>
						{
							_strokeColor.Value = Colors.Orange;
						}),
						Button("Yellow", () =>
						{
							_strokeColor.Value = Colors.Yellow;
						}),
						Button("Brown", () =>
						{
							_strokeColor.Value = Colors.Brown;
						}),
						Button("Salmon", () =>
						{
							_strokeColor.Value = Colors.Salmon;
						}),
						Button("Magenta", () =>
						{
							_strokeColor.Value = Colors.Magenta;
						})
					)
				),
				new BindableFingerPaint(
					strokeSize:_strokeSize,
					strokeColor:_strokeColor).Frame(height:400).FillHorizontal().Border(new Rectangle().Stroke(Colors.White,2))
			)
		);
	}
}
