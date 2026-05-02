using System;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class ControlsPage : View
	{
		readonly Reactive<int> clickCount = 0;
		readonly Reactive<string> entryText = "";
		readonly Reactive<double> sliderValue = 50;
		readonly Reactive<bool> toggleValue = false;
		readonly Reactive<bool> checkValue = false;
		readonly Reactive<double> stepperValue = 0;
		readonly Reactive<int> radioIndex = 0;

		[Body]
		View Body() =>
			GalleryPageHelpers.Scaffold("Controls",
				GalleryPageHelpers.Section("Button & ProgressBar",
					Button("Click me!", () => clickCount.Value++),
					Button("Gradient Button", () => clickCount.Value++)
						.Color(Colors.White)
						.Background(new LinearGradientPaint(
							new[] { new PaintGradientStop(0, Colors.OrangeRed), new PaintGradientStop(1, Colors.DodgerBlue) },
							new Point(0, 0.5), new Point(1, 0.5))),
					Text(() => $"Clicks: {clickCount.Value}")
						.FontSize(14),
					Text("Progress (click 20x to fill):")
						.FontSize(12)
						.Color(Colors.Grey),
					ProgressBar(() => Math.Min(1.0, clickCount.Value / 20.0))
				),
				GalleryPageHelpers.Section("Button with Image",
					StarButton("*", "Image Left (default)", "left"),
					StarButton("*", "Image Right", "right"),
					StarButton("*", "Image Top", "top"),
					StarButton("*", "Image Bottom", "bottom")
				),
				GalleryPageHelpers.Section("ImageButton",
					Image(() => new FontImageSource(null, "Bell", 24, Colors.CornflowerBlue))
						.Frame(width: 44, height: 44)
						.HorizontalLayoutAlignment(LayoutAlignment.Center)
				),
				GalleryPageHelpers.Section("Entry",
					TextField(() => entryText.Value, () => "Type here...")
						.OnTextChanged(value => entryText.Value = value ?? ""),
					Text(() => $"Echo: {entryText.Value}")
						.FontSize(14)
						.Color(Colors.Grey)
				),
				GalleryPageHelpers.Section("Editor",
					TextEditor(() => "")
						.Placeholder("Multi-line text editor...")
						.Frame(height: 80)
				),
				GalleryPageHelpers.Section("Slider",
					Slider(() => sliderValue.Value, () => 0.0, () => 100.0)
						.OnValueChanged(v => sliderValue.Value = v),
					Text(() => $"Slider: {sliderValue.Value:F0}")
						.FontSize(14)
				),
				GalleryPageHelpers.Section("Switch",
					HStack(12,
						Toggle(() => toggleValue.Value)
							.OnToggled(value => toggleValue.Value = value),
						Text(() => toggleValue.Value ? "On" : "Off")
							.FontSize(14)
					)
				),
				GalleryPageHelpers.Section("CheckBox",
					HStack(12,
						CheckBox(() => checkValue.Value)
							.OnCheckedChanged(value => checkValue.Value = value),
						Text(() => checkValue.Value ? "Checked \u2713" : "Unchecked")
							.FontSize(14)
					)
				),
				GalleryPageHelpers.Section("Stepper (increment by 5)",
					Stepper(() => stepperValue.Value, () => 0.0, () => 50.0, () => 5.0)
						.OnValueChanged(value => stepperValue.Value = value),
					Text(() => $"Stepper: {stepperValue.Value:F0}")
						.FontSize(14)
				),
				GalleryPageHelpers.Section("RadioButton",
					BuildRadioGroup(),
					Text(() => $"Selected: Option {(char)('A' + radioIndex.Value)}")
						.FontSize(14)
						.Color(Colors.DodgerBlue)
				)
			);

		View BuildRadioGroup()
		{
			var group = new RadioGroup(Orientation.Vertical) { GroupName = "options" };
			var options = new[] { "Option A", "Option B", "Option C" };
			for (int i = 0; i < options.Length; i++)
			{
				var idx = i;
				group.Add(new RadioButton(
					() => options[idx],
					() => radioIndex.Value == idx,
					() => radioIndex.Value = idx));
			}
			return group;
		}

		static View StarButton(string glyph, string label, string position)
		{
			var icon = Image(() => new FontImageSource(null, glyph, 12, Colors.Orange))
				.Frame(width: 16, height: 16);
			var text = Text(label).FontSize(14);

			View content = position switch
			{
				"right" => HStack(8, text, icon),
				"top" => VStack(4, icon, text),
				"bottom" => VStack(4, text, icon),
				_ => HStack(8, icon, text)
			};

			return content
				.Padding(new Thickness(4, 6))
				.FitHorizontal();
		}
	}
}
