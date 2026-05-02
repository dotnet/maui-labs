using System;
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class TextFieldSample4 : Component
	{
		readonly Signal<string> _text = new("Edit Me");

		public override View Render() => VStack(
			TextField(_text, "Name"),
			HStack(
				Text("Current Value:")
					.Color(Colors.Grey),
				Text(() => _text.Value),
				Spacer()
			)
		).FillHorizontal();
	}

}
