using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class RadioButtonPageState
	{
		public string SelectedLabel { get; set; } = "None";
		public int SizeIndex { get; set; }
		public int ColorIndex { get; set; }
		public int PlanIndex { get; set; }
	}

	public class RadioButtonPage : Component<RadioButtonPageState>
	{
		static readonly string[] Sizes = { "Small", "Medium", "Large" };
		static readonly string[] ColorOptions = { "Red", "Green", "Blue", "Yellow" };
		static readonly string[] Plans = { "Free", "Pro", "Enterprise" };

		public override View Render() =>
			ScrollView(Orientation.Vertical,
				VStack(16,
					Text("RadioButton Demos")
						.FontSize(24)
						.FontWeight(FontWeight.Bold),
					Text(() => $"Selected: {State.SelectedLabel}")
						.FontSize(16)
						.FontWeight(FontWeight.Bold)
						.Color(Colors.DodgerBlue)
						.Margin(new Thickness(0, 0, 0, 10)),

					SectionHeader(DeviceInfo.Platform == DevicePlatform.iOS ? "Native RadioButton (UIButton)" : "Native RadioButton (NSButton)"),
					Text(DeviceInfo.Platform == DevicePlatform.iOS ? "Standard native iOS radio buttons:" : "Standard native macOS radio buttons:")
						.FontSize(13).Color(Colors.Grey),
					BuildGroup("size", Sizes, State.SizeIndex,
						(index, label) => SetState(s => { s.SizeIndex = index; s.SelectedLabel = label; })),

					SectionHeader("Color Selection"),
					Text("Choose a color:")
						.FontSize(13).Color(Colors.Grey),
					BuildGroup("color", ColorOptions, State.ColorIndex,
						(index, label) => SetState(s => { s.ColorIndex = index; s.SelectedLabel = label; })),

					SectionHeader("Plan Selection"),
					Text("Select a plan:")
						.FontSize(13).Color(Colors.Grey),
					BuildGroup("plan", Plans, State.PlanIndex,
						(index, label) => SetState(s => { s.PlanIndex = index; s.SelectedLabel = label; }))
				).Padding(new Thickness(20))
			).Title("RadioButton");

		static View BuildGroup(string groupName, string[] options, int selectedIndex, Action<int, string> onSelected)
		{
			var group = new RadioGroup(Orientation.Vertical)
			{
				GroupName = groupName
			};

			for (var i = 0; i < options.Length; i++)
			{
				var capturedIndex = i;
				group.Add(new RadioButton(
					() => options[capturedIndex],
					() => selectedIndex == capturedIndex,
					() => onSelected(capturedIndex, options[capturedIndex])));
			}

			return group.Margin(new Thickness(8, 0));
		}

		static View SectionHeader(string title) =>
			Text(title)
				.FontSize(18)
				.FontWeight(FontWeight.Bold)
				.Margin(new Thickness(0, 12, 0, 4));
	}
}
