using System;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	/// <summary>
	/// Validates Computed&lt;T&gt; — derived state that auto-recalculates when dependencies change.
	/// Three independent signals feed one Computed&lt;string&gt; summary.
	/// </summary>
	public class ComputedDemoPage : View
	{
		readonly Reactive<string> firstName = "Jane";
		readonly Reactive<string> lastName = "Doe";
		readonly Reactive<double> age = 30;

		readonly Computed<string> summary;
		readonly Computed<string> greeting;

		public ComputedDemoPage()
		{
			summary = new Computed<string>(() =>
				$"{firstName.Value} {lastName.Value}, age {(int)age.Value}");

			greeting = new Computed<string>(() =>
			{
				var name = firstName.Value;
				var ageVal = (int)age.Value;
				if (ageVal < 18)
					return $"Hey {name}!";
				else if (ageVal < 65)
					return $"Hello, {name}.";
				else
					return $"Good day, {name}.";
			});
		}

		[Body]
		View body() =>
			GalleryPageHelpers.Scaffold("Computed Demo",
				GalleryPageHelpers.Section("Input Signals",
					GalleryPageHelpers.Caption("First Name:"),
					TextField(() => firstName.Value, () => "First name...")
						.OnTextChanged(v => firstName.Value = v ?? ""),
					GalleryPageHelpers.Caption("Last Name:"),
					TextField(() => lastName.Value, () => "Last name...")
						.OnTextChanged(v => lastName.Value = v ?? ""),
					Text(() => $"Age: {(int)age.Value}")
						.FontSize(12)
						.Color(Colors.Grey),
					Slider(() => age.Value, () => 0.0, () => 100.0)
						.OnValueChanged(v => age.Value = v)
				),
				GalleryPageHelpers.Section("Computed Outputs",
					Text(() => $"Summary: {summary.Value}")
						.FontSize(18)
						.FontWeight(FontWeight.Bold),
					Text(() => greeting.Value)
						.FontSize(16)
						.Color(Colors.MediumSeaGreen)
				),
				GalleryPageHelpers.Section("Validation Notes",
					GalleryPageHelpers.BodyText("Computed<string> re-evaluates when any dependency changes"),
					GalleryPageHelpers.BodyText("Two Computed values from overlapping dependencies"),
					GalleryPageHelpers.BodyText("Conditional logic inside Computed (greeting changes by age)"),
					GalleryPageHelpers.BodyText("No manual subscription — automatic dependency tracking")
				)
			);
	}
}
