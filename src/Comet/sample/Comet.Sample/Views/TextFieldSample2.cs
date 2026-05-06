using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class TextFieldSample2 : Component
	{
		readonly Reactive<string> _textValue = "Edit Me";

				public override View Render() => VStack(
			TextField(_textValue, "Name"),
			HStack(
				Text("Current Value:")
					.Color(Colors.Grey),
				Text(() => _textValue.Value),
				Spacer()
			)
		).FillHorizontal();
	}

}
