using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using Platform.Maui.Linux.Gtk4.Essentials.Storage;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class ClipboardPrefsPage : ContentPage
{
	public ClipboardPrefsPage()
	{
		Title = "Clipboard & Preferences";

		// --- Clipboard section ---
		var clipboardContent = new Label { Text = "(empty)", FontSize = 14, TextColor = Colors.Gray };
		var clipboardEntry = new Entry { Placeholder = "Text to copy..." };

		var copyButton = new Button { Text = "📋 Copy to Clipboard" };
		copyButton.Clicked += async (s, e) =>
		{
			var clipboard = IPlatformApplication.Current?.Services.GetService<IClipboard>();
			if (clipboard is not null && !string.IsNullOrEmpty(clipboardEntry.Text))
			{
				await clipboard.SetTextAsync(clipboardEntry.Text);
				clipboardContent.Text = $"Copied: \"{clipboardEntry.Text}\"";
				clipboardContent.TextColor = Colors.Green;
			}
		};

		var pasteButton = new Button { Text = "📄 Paste from Clipboard" };
		pasteButton.Clicked += async (s, e) =>
		{
			var clipboard = IPlatformApplication.Current?.Services.GetService<IClipboard>();
			if (clipboard is not null)
			{
				var text = await clipboard.GetTextAsync();
				clipboardContent.Text = text is not null ? $"Pasted: \"{text}\"" : "(clipboard empty)";
				clipboardContent.TextColor = text is not null ? Colors.DodgerBlue : Colors.Gray;
			}
		};

		// --- Preferences section ---
		var prefsKey = new Entry { Placeholder = "Key", Text = "demo_key" };
		var prefsValue = new Entry { Placeholder = "Value", Text = "Hello from Linux!" };
		var prefsResult = new Label { Text = "(no value)", FontSize = 14, TextColor = Colors.Gray };

		var saveButton = new Button { Text = "💾 Save Preference" };
		saveButton.Clicked += (s, e) =>
		{
			var prefs = IPlatformApplication.Current?.Services.GetService<IPreferences>();
			if (prefs is not null && !string.IsNullOrEmpty(prefsKey.Text))
			{
				prefs.Set(prefsKey.Text, prefsValue.Text ?? "", null);
				prefsResult.Text = $"Saved: {prefsKey.Text} = \"{prefsValue.Text}\"";
				prefsResult.TextColor = Colors.Green;
			}
		};

		var loadButton = new Button { Text = "📂 Load Preference" };
		loadButton.Clicked += (s, e) =>
		{
			var prefs = IPlatformApplication.Current?.Services.GetService<IPreferences>();
			if (prefs is not null && !string.IsNullOrEmpty(prefsKey.Text))
			{
				var value = prefs.Get(prefsKey.Text, "(not found)", null);
				prefsResult.Text = $"Loaded: {prefsKey.Text} = \"{value}\"";
				prefsResult.TextColor = Colors.DodgerBlue;
			}
		};

		var clearButton = new Button { Text = "🗑️ Clear All Preferences" };
		clearButton.Clicked += (s, e) =>
		{
			var prefs = IPlatformApplication.Current?.Services.GetService<IPreferences>();
			prefs?.Clear(null);
			prefsResult.Text = "All preferences cleared";
			prefsResult.TextColor = Colors.OrangeRed;
		};

		// --- Secure Storage section ---
		var secureKey = new Entry { Placeholder = "Key", Text = "api_token" };
		var secureValue = new Entry { Placeholder = "Secret value", Text = "sk-12345-secret" };
		var secureResult = new Label { Text = "(no value)", FontSize = 14, TextColor = Colors.Gray };

		var secureBackendLabel = new Label { Text = "Backend: probing...", FontSize = 12, TextColor = Colors.Gray };
		// Resolve backend on load
		var secureStorage = IPlatformApplication.Current?.Services.GetService<ISecureStorage>();
		if (secureStorage is LinuxSecureStorage linuxSecure)
		{
			var backend = linuxSecure.Backend;
			secureBackendLabel.Text = $"Backend: {backend}";
			secureBackendLabel.TextColor = backend == "libsecret" ? Colors.Green : Colors.Orange;
		}

		var secSaveButton = new Button { Text = "🔐 Store Secret" };
		secSaveButton.Clicked += async (s, e) =>
		{
			var secure = IPlatformApplication.Current?.Services.GetService<ISecureStorage>();
			if (secure is not null && !string.IsNullOrEmpty(secureKey.Text))
			{
				await secure.SetAsync(secureKey.Text, secureValue.Text ?? "");
				secureResult.Text = $"Stored secret for key: {secureKey.Text}";
				secureResult.TextColor = Colors.Green;
			}
		};

		var secLoadButton = new Button { Text = "🔓 Retrieve Secret" };
		secLoadButton.Clicked += async (s, e) =>
		{
			var secure = IPlatformApplication.Current?.Services.GetService<ISecureStorage>();
			if (secure is not null && !string.IsNullOrEmpty(secureKey.Text))
			{
				var value = await secure.GetAsync(secureKey.Text);
				secureResult.Text = value is not null ? $"Retrieved: \"{value}\"" : "(not found)";
				secureResult.TextColor = value is not null ? Colors.DodgerBlue : Colors.Gray;
			}
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "Clipboard & Storage", FontSize = 24, FontAttributes = FontAttributes.Bold },
					new Label { Text = "Data transfer, preferences, and secure storage", FontSize = 14, TextColor = Colors.Gray },
					new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },

					SectionHeader("📋 Clipboard"),
					clipboardEntry,
					new HorizontalStackLayout { Spacing = 8, Children = { copyButton, pasteButton } },
					clipboardContent,

					Separator(),

					SectionHeader("⚙️ Preferences (JSON file)"),
					new HorizontalStackLayout { Spacing = 8, Children = { prefsKey, prefsValue } },
					new HorizontalStackLayout { Spacing = 8, Children = { saveButton, loadButton, clearButton } },
					prefsResult,

					Separator(),

					SectionHeader("🔐 Secure Storage"),
				secureBackendLabel,
					new HorizontalStackLayout { Spacing = 8, Children = { secureKey, secureValue } },
					new HorizontalStackLayout { Spacing = 8, Children = { secSaveButton, secLoadButton } },
					secureResult,
				}
			}
		};
	}

	static Label SectionHeader(string text) => new()
	{
		Text = text, FontSize = 18, FontAttributes = FontAttributes.Bold,
		Margin = new Thickness(0, 8, 0, 4),
	};
	static BoxView Separator() => new() { HeightRequest = 1, Color = Colors.LightGray, Margin = new Thickness(0, 4) };
}
