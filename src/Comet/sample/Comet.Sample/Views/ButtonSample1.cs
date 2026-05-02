using System;
using System.Collections.Generic;
using static Comet.CometControls;
using Comet.Reactive;

namespace Comet.Samples
{
	public class ButtonSample1 : Component
	{
		readonly Signal<int> count = new(0);

				public override View Render() => VStack(
			Button("Increment Value", () => count.Value ++ ),
			Text(() => $"Value: {count.Value}")
		);

	}
}
