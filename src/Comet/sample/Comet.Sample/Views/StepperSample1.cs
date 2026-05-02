using System;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class StepperSample1 : Component
	{
		readonly Reactive<double> min = 0;
		readonly Reactive<double> max = 10;
		readonly Reactive<double> increment = 1;
		readonly Reactive<double> number1 = 0;
		//private double currentValue;

				public override View Render() => VStack(
			Text($"{number1.Value}"),
			Stepper(number1, max, min, increment)
		);
	}
}
