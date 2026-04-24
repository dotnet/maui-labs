using System;
using System.Linq;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class PickersPage : View
	{
		static readonly string[] ColorOptions = { "Red", "Green", "Blue", "Orange", "Purple" };
		static readonly string[] Fruits =
		{
			"Apple", "Banana", "Cherry", "Date", "Elderberry",
			"Fig", "Grape", "Honeydew", "Kiwi", "Lemon", "Mango"
		};

		readonly Reactive<DateTime> selectedDate = DateTime.Today;
		readonly Reactive<TimeSpan> selectedTime = DateTime.Now.TimeOfDay;
		readonly Reactive<int> pickerIndex = -1;
		readonly Reactive<string> searchText = "";

		[Body]
		View body()
		{
			var pickerText = pickerIndex.Value >= 0 && pickerIndex.Value < ColorOptions.Length
				? $"Selected: {ColorOptions[pickerIndex.Value]}"
				: "Selected: (none)";

			return GalleryPageHelpers.Scaffold("Pickers",
				GalleryPageHelpers.Section("DatePicker",
					DatePicker(
						() => selectedDate.Value,
						() => new DateTime(2020, 1, 1),
						() => new DateTime(2030, 12, 31)
					),
					Text(() => $"Selected date: {selectedDate.Value:D}")
						.FontSize(14)
				),
				GalleryPageHelpers.Section("TimePicker",
					TimePicker(
						() => selectedTime.Value
					),
					Text(() => $"Selected time: {DateTime.Today.Add(selectedTime.Value):hh\\:mm tt}")
						.FontSize(14)
				),
				GalleryPageHelpers.Section("Picker (Dropdown)",
					new Picker(pickerIndex.Value, ColorOptions)
					{
						Title = "Pick a color"
					}.OnSelectedIndexChanged(index => pickerIndex.Value = index)
					.VerticalTextAlignment(TextAlignment.Center),
					GalleryPageHelpers.BodyText(pickerText)
				),
				GalleryPageHelpers.Section("SearchBar",
					SearchBar(() => searchText.Value, () => { })
						.Placeholder("Search fruits...")
						.OnTextChanged(v => searchText.Value = v ?? ""),
					Text(() =>
					{
						if (string.IsNullOrWhiteSpace(searchText.Value))
							return "Search results will appear here...";
						var matches = Fruits.Where(f => f.Contains(searchText.Value, StringComparison.OrdinalIgnoreCase)).ToArray();
						return matches.Length > 0
							? $"Found: {string.Join(", ", matches)}"
							: "No matches found.";
					})
						.FontSize(14)
						.Color(Colors.Grey)
				)
			);
		}
	}
}
