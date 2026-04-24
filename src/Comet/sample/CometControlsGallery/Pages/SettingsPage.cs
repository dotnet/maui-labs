using Comet;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class SettingsPageState
	{
		public bool DarkMode { get; set; } = false;
		public bool Notifications { get; set; } = true;
		public bool AutoUpdate { get; set; } = true;
	}

	public class SettingsPage : Component<SettingsPageState>
	{
		public override View Render()
		{
			return GalleryPageHelpers.Scaffold("Settings",
				BuildAccountSection(),
				BuildPreferencesSection(),
				BuildAboutSection()
			);
		}

		View BuildAccountSection()
		{
			return GalleryPageHelpers.Section("Account", "User profile and account details.",
				BuildSettingsRow("Username", "john.doe@example.com"),
				BuildSeparator(),
				BuildSettingsRow("Email", "john.doe@example.com")
			);
		}

		View BuildPreferencesSection()
		{
			return GalleryPageHelpers.Section("Preferences", "Customize your app experience.",
				BuildToggleRow("Dark Mode", "Use dark color scheme", State.DarkMode, value => SetState(s => s.DarkMode = value)),
				BuildSeparator(),
				BuildToggleRow("Notifications", "Receive push notifications", State.Notifications, value => SetState(s => s.Notifications = value)),
				BuildSeparator(),
				BuildToggleRow("Auto-update", "Automatically check for updates", State.AutoUpdate, value => SetState(s => s.AutoUpdate = value))
			);
		}

		View BuildAboutSection()
		{
			return GalleryPageHelpers.Section("About", "App information and legal.",
				BuildSettingsRow("Version", "1.0.0"),
				BuildSeparator(),
				BuildSettingsRow("Build", "2025.03.08"),
				BuildSeparator(),
				BuildSettingsRow("License", "MIT License")
			);
		}

		View BuildSettingsRow(string label, string value)
		{
			return HStack(12,
				VStack(2,
					Text(label)
						.FontSize(14)
						.FontWeight(FontWeight.Bold)
						.Color(ColorTokens.OnSurface.Resolve(ThemeManager.Current())),
					Text(value)
						.FontSize(12)
						.Color(ColorTokens.OnSurfaceVariant.Resolve(ThemeManager.Current()))
				)
			)
			.Padding(new Thickness(0, 8));
		}

		View BuildToggleRow(string label, string description, bool isOn, System.Action<bool> onToggled)
		{
			return HStack(12,
				VStack(2,
					Text(label)
						.FontSize(14)
						.FontWeight(FontWeight.Bold)
						.Color(ColorTokens.OnSurface.Resolve(ThemeManager.Current())),
					Text(description)
						.FontSize(12)
						.Color(ColorTokens.OnSurfaceVariant.Resolve(ThemeManager.Current()))
				),
				Spacer(),
				Toggle(() => isOn)
					.OnToggled(onToggled)
			)
			.Padding(new Thickness(0, 8));
		}

		View BuildSeparator()
		{
			return new ShapeView(new Rectangle())
				.Background(new Color(128, 128, 128, 0.3f))
				.Frame(height: 1);
		}
	}
}
