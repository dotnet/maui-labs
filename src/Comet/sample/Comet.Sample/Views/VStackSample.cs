using System;
using System.Collections.Generic;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class VStackSample : Component
	{
		readonly Reactive<string> _textValue = "Edit Me";
		readonly Reactive<double> _sliderValue = 50;

				public override View Render() => VStack(
			Text(() => _textValue.Value),
			TextField(_textValue, "Name"),
			SecureField(_textValue, "Name"),
			Slider(_sliderValue),
			ProgressBar(_sliderValue)
		).FillHorizontal();
	}

}
