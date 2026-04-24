using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class TableViewPageState
	{
		public bool DarkMode { get; set; }
		public bool Notifications { get; set; } = true;
		public bool AutoUpdate { get; set; } = true;
		public string NameValue { get; set; } = "John";
		public string BioValue { get; set; } = "";
	}

	public class TableViewPage : Component<TableViewPageState>
	{
		public override View Render()
		{
			return GalleryPageHelpers.Scaffold("TableView",
				Text("Settings-style grouped table with various cell types:")
					.FontSize(14)
					.Color(Colors.Gray),

				// Account section
				GalleryPageHelpers.Section("Account",
					BuildTextRow("Username", "john.appleseed"),
					BuildTextRow("Email", "john@example.com"),
					BuildTextRow("Profile Photo", "Tap to change")
				),

				// Preferences section
				GalleryPageHelpers.Section("Preferences",
					BuildSwitchRow("Dark Mode", () => State.DarkMode, v => SetState(s => s.DarkMode = v)),
					BuildSwitchRow("Notifications", () => State.Notifications, v => SetState(s => s.Notifications = v)),
					BuildSwitchRow("Auto-update", () => State.AutoUpdate, v => SetState(s => s.AutoUpdate = v))
				),

				// Input section
				GalleryPageHelpers.Section("Input",
					BuildEntryRow("Name", () => State.NameValue, "Enter your name", v => SetState(s => s.NameValue = v)),
					BuildEntryRow("Bio", () => State.BioValue, "Write something...", v => SetState(s => s.BioValue = v))
				),

				// About section
				GalleryPageHelpers.Section("About",
					BuildTextRow("Version", "1.0.0"),
					BuildTextRow("Build", "2024.02.20"),
					BuildTextRow("License", "MIT")
				)
			);
		}

		View BuildTextRow(string label, string detail) =>
			HStack(
				Text(label)
					.FontSize(14),
				Spacer(),
				Text(detail)
					.FontSize(14)
					.Color(Colors.Gray)
			)
			.Padding(new Thickness(0, 8));

		View BuildSwitchRow(string label, Func<bool> value, Action<bool> onChanged) =>
			HStack(
				Text(label)
					.FontSize(14),
				Spacer(),
				Toggle(value)
					.OnToggled(onChanged)
			)
			.Padding(new Thickness(0, 4));

		View BuildEntryRow(string label, Func<string> value, string placeholder, Action<string> onChanged) =>
			HStack(12,
				Text(label)
					.FontSize(14)
					.Frame(width: 60),
				TextField(value, () => placeholder)
					.OnTextChanged(v => onChanged(v ?? ""))
					.FontSize(14)
			)
			.Padding(new Thickness(0, 4));
	}
}
