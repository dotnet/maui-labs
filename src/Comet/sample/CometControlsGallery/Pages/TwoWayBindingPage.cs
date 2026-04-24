using System;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	/// <summary>
	/// Validates two-way binding: a Reactive&lt;string&gt; is bound to two TextFields
	/// and a display label. Typing in either field updates all three.
	/// Also tests Reactive&lt;double&gt; two-way with a Slider ↔ Text label.
	/// </summary>
	public class TwoWayBindingPage : View
	{
		readonly Reactive<string> sharedText = "Hello Comet";
		readonly Reactive<double> sliderVal = 50;
		readonly Reactive<bool> toggleVal = false;

		[Body]
		View body() =>
			GalleryPageHelpers.Scaffold("Two-Way Binding",
				GalleryPageHelpers.Section("Shared Text Signal",
					GalleryPageHelpers.Caption("TextField A → writes to sharedText:"),
					TextField(() => sharedText.Value, () => "Type here...")
						.OnTextChanged(v => sharedText.Value = v ?? ""),
					GalleryPageHelpers.Caption("TextField B → also bound to sharedText:"),
					TextField(() => sharedText.Value, () => "Or type here...")
						.OnTextChanged(v => sharedText.Value = v ?? ""),
					Text(() => $"Live value: \"{sharedText.Value}\"")
						.FontSize(16)
						.FontWeight(FontWeight.Bold)
						.Color(Colors.DodgerBlue),
					Text(() => $"Length: {sharedText.Value.Length} characters")
						.FontSize(14)
						.Color(Colors.Grey)
				),
				GalleryPageHelpers.Section("Slider ↔ Label",
					Slider(() => sliderVal.Value, () => 0.0, () => 100.0)
						.OnValueChanged(v => sliderVal.Value = v),
					Text(() => $"Value: {sliderVal.Value:F1}")
						.FontSize(16)
						.HorizontalTextAlignment(TextAlignment.Center),
					HStack(12,
						Button("Set 0", () => sliderVal.Value = 0),
						Button("Set 50", () => sliderVal.Value = 50),
						Button("Set 100", () => sliderVal.Value = 100)
					)
				),
				GalleryPageHelpers.Section("Toggle ↔ Label",
					HStack(12,
						Toggle(() => toggleVal.Value)
							.OnToggled(v => toggleVal.Value = v),
						Text(() => toggleVal.Value ? "ON" : "OFF")
							.FontSize(16)
					),
					Button("Toggle programmatically", () => toggleVal.Value = !toggleVal.Value)
				),
				GalleryPageHelpers.Section("Validation Notes",
					GalleryPageHelpers.BodyText("Two TextFields bound to same Reactive<string>"),
					GalleryPageHelpers.BodyText("Programmatic signal writes update UI controls"),
					GalleryPageHelpers.BodyText("Slider, Toggle two-way binding verified"),
					GalleryPageHelpers.BodyText("UI → Signal → UI round-trip")
				)
			);
	}
}
