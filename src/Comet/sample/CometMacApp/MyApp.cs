using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometMacApp;

public class MyApp : CometApp
{
	[Body]
	View Body() => new MainPage();
}

public class MainPage : View
{
	readonly State<int> count = 0;

	[Body]
	View Body() =>
		VStack(
			Text("Comet on AppKit!")
				.FontSize(28)
				.Color(Colors.DarkBlue),
			Text(() => $"Count: {count.Value}")
				.FontSize(20)
				.Margin(new Thickness(0, 12)),
			Button("Click Me", () => count.Value++)
				.Frame(width: 200, height: 44)
		)
		.FillHorizontal();
}
