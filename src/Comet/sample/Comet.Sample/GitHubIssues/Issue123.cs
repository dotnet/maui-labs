using System;
using System.Collections.ObjectModel;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class Issue123 : Component
	{
		private readonly Reactive<int> count = 0;

		public override View Render() => VStack(
			Text(() => $"Value: {count.Value}")
				.Color(Colors.Black)
				.FontSize(32),
			Button("Increment", () => count.Value++)
				.Frame(width: 320, height: 44)
				.Background(Colors.Black)
				.Color(Colors.White)
				.Margin(20)
		);
	}
}
