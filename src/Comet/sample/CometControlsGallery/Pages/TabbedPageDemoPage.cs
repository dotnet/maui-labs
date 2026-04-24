using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class TabbedPageState
	{
		public int SelectedTab { get; set; }
		public bool DarkMode { get; set; }
		public bool Notifications { get; set; } = true;
		public double Volume { get; set; } = 75;
	}

	public class TabbedPageDemoPage : Component<TabbedPageState>
	{
		public override View Render() => GalleryPageHelpers.Scaffold("TabbedPage Demo",
			// Tab bar
			HStack((float?)0,
				TabButton("Overview", 0),
				TabButton("Settings", 1),
				TabButton("Stats", 2)
			),

			GalleryPageHelpers.Separator(),

			// Tab content
			State.SelectedTab switch
			{
				0 => BuildOverviewTab(),
				1 => BuildSettingsTab(),
				2 => BuildStatsTab(),
				_ => BuildOverviewTab()
			}
		);

		View TabButton(string title, int index)
		{
			var isSelected = State.SelectedTab == index;
			return Button(title, () => SetState(s => s.SelectedTab = index))
				.Color(isSelected ? Colors.White : Colors.CornflowerBlue)
				.Background(isSelected ? Colors.CornflowerBlue : Colors.Transparent)
				.FontSize(14)
				.Padding(new Thickness(16, 8));
		}

		View BuildOverviewTab() => VStack(12,
			Text(DeviceInfo.Platform == DevicePlatform.iOS ? "This is a TabbedPage rendered with native UITabBarController." : "This is a TabbedPage rendered with native NSTabView.")
				.FontSize(13)
				.Color(Colors.Grey),
			GalleryPageHelpers.Separator(),
			Text(DeviceInfo.Platform == DevicePlatform.iOS ? "Native UITabBarController tab rendering" : "Native NSTabView tab rendering")
				.FontSize(14),
			Text("Automatic tab label from Page.Title")
				.FontSize(14),
			Text("Tab switching syncs with MAUI CurrentPage")
				.FontSize(14),
			Text("Dynamic page collection support")
				.FontSize(14)
		);

		View BuildSettingsTab() => VStack(12,
			Text("Settings")
				.FontSize(20)
				.FontWeight(FontWeight.Bold),
			GalleryPageHelpers.Separator(),
			BuildSettingRow("Dark Mode",
				Toggle(() => State.DarkMode)
					.OnToggled(v => SetState(s => s.DarkMode = v))),
			BuildSettingRow("Notifications",
				Toggle(() => State.Notifications)
					.OnToggled(v => SetState(s => s.Notifications = v))),
			BuildSettingRow("Volume",
				Slider(() => State.Volume, () => 0.0, () => 100.0)
					.OnValueChanged(v => SetState(s => s.Volume = v))
					.Frame(width: 200))
		);

		View BuildStatsTab() => VStack(12,
			Text("Statistics")
				.FontSize(20)
				.FontWeight(FontWeight.Bold),
			GalleryPageHelpers.Separator(),
			BuildStatRow("Projects", "12", Colors.DodgerBlue),
			BuildStatRow("Tasks Done", "847", Colors.MediumSeaGreen),
			BuildStatRow("Open Issues", "23", Colors.Orange),
			BuildStatRow("Contributors", "6", Colors.MediumOrchid),
			GalleryPageHelpers.Separator(),
			new ProgressBar(() => 0.73),
			Text("73% sprint completion")
				.FontSize(12)
				.Color(Colors.Grey)
		);

		static View BuildSettingRow(string label, View control) =>
			HStack(12,
				Text(label)
					.FontSize(15)
					.Frame(width: 120),
				control
			);

		static View BuildStatRow(string label, string value, Color color) =>
			HStack(12,
				Border(
					Text(value)
						.FontSize(18)
						.FontWeight(FontWeight.Bold)
						.Color(Colors.White)
				)
				.Background(color)
				.CornerRadius(6)
				.Padding(new Thickness(12, 8)),
				Text(label)
					.FontSize(15)
			);
	}
}
