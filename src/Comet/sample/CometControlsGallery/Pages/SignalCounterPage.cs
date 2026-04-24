using System;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	/// <summary>
	/// Validates basic Signal/Reactive read/write with increment, decrement, and reset.
	/// Also shows a derived text label auto-updating on each state change.
	/// </summary>
	public class SignalCounterPage : View
	{
		readonly Reactive<int> count = 0;
		readonly Reactive<string> label = "Ready";

		[Body]
		View body() =>
			GalleryPageHelpers.Scaffold("Signal Counter",
				GalleryPageHelpers.Section("Basic Counter",
					Text(() => $"Count: {count.Value}")
						.FontSize(48)
						.FontWeight(FontWeight.Bold)
						.HorizontalTextAlignment(TextAlignment.Center)
						.Color(count.Value >= 0 ? Colors.DodgerBlue : Colors.Crimson),
					HStack(16,
						Button("−", () =>
						{
							count.Value--;
							label.Value = $"Decremented to {count.Value}";
						})
						.Frame(width: 60, height: 44),
						Button("Reset", () =>
						{
							count.Value = 0;
							label.Value = "Reset to zero";
						})
						.Frame(width: 80, height: 44),
						Button("+", () =>
						{
							count.Value++;
							label.Value = $"Incremented to {count.Value}";
						})
						.Frame(width: 60, height: 44)
					),
					Text(() => label.Value)
						.FontSize(14)
						.Color(Colors.Grey)
						.HorizontalTextAlignment(TextAlignment.Center)
				),
				GalleryPageHelpers.Section("Rapid Update Test",
					GalleryPageHelpers.Caption("Tap to add 10 in a tight loop — should see final value, not intermediate flicker."),
					Button("Add 10 rapidly", () =>
					{
						for (int i = 0; i < 10; i++)
							count.Value++;
						label.Value = $"Added 10, now at {count.Value}";
					})
				),
				GalleryPageHelpers.Section("Validation Notes",
					GalleryPageHelpers.BodyText("Signal<int> read/write via Reactive<T>"),
					GalleryPageHelpers.BodyText("Negative values change text color"),
					GalleryPageHelpers.BodyText("Multiple signals updated in same handler"),
					GalleryPageHelpers.BodyText("Rapid loop writes coalesce to single UI update")
				)
			);
	}
}
