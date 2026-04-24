using System;
using Comet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class ClipboardStorageState
	{
		public string ClipboardEntry { get; set; } = "";
		public string ClipboardResult { get; set; } = "(empty)";
		public Color ClipboardResultColor { get; set; } = Colors.Grey;

		public string PrefsKey { get; set; } = "demo_key";
		public string PrefsValue { get; set; } = DeviceInfo.Platform == DevicePlatform.iOS ? "Hello from iOS!" : "Hello from macOS!";
		public string PrefsResult { get; set; } = "(no value)";
		public Color PrefsResultColor { get; set; } = Colors.Grey;

		public string SecureKey { get; set; } = "api_token";
		public string SecureValue { get; set; } = "sk-12345-secret";
		public string SecureResult { get; set; } = "(no value)";
		public Color SecureResultColor { get; set; } = Colors.Grey;
	}

	public class ClipboardStoragePage : Component<ClipboardStorageState>
	{
		public override View Render() => GalleryPageHelpers.Scaffold("Clipboard & Storage",
			// Clipboard section
			GalleryPageHelpers.Section("Clipboard",
				TextField(() => State.ClipboardEntry, () => "Text to copy...")
					.OnTextChanged(v => SetState(s => s.ClipboardEntry = v)),
				GalleryPageHelpers.ButtonRow(8,
					Button("Copy to Clipboard", CopyToClipboard),
					Button("Paste from Clipboard", PasteFromClipboard)
				),
				Text(() => State.ClipboardResult)
					.FontSize(14)
					.Color(() => State.ClipboardResultColor)
			),

			// Preferences section
			GalleryPageHelpers.Section("Preferences",
				HStack(8,
					TextField(() => State.PrefsKey, () => "Key")
						.OnTextChanged(v => SetState(s => s.PrefsKey = v)),
					TextField(() => State.PrefsValue, () => "Value")
						.OnTextChanged(v => SetState(s => s.PrefsValue = v))
				),
				GalleryPageHelpers.ButtonRow(8,
					Button("Save Preference", SavePreference),
					Button("Load Preference", LoadPreference),
					Button("Clear All Preferences", ClearPreferences)
				),
				Text(() => State.PrefsResult)
					.FontSize(14)
					.Color(() => State.PrefsResultColor)
			),

			// Secure Storage section
			GalleryPageHelpers.Section("Secure Storage (Keychain)",
				HStack(8,
					TextField(() => State.SecureKey, () => "Key")
						.OnTextChanged(v => SetState(s => s.SecureKey = v)),
					TextField(() => State.SecureValue, () => "Secret value")
						.OnTextChanged(v => SetState(s => s.SecureValue = v))
				),
				GalleryPageHelpers.ButtonRow(8,
					Button("Store Secret", StoreSecret),
					Button("Retrieve Secret", RetrieveSecret)
				),
				Text(() => State.SecureResult)
					.FontSize(14)
					.Color(() => State.SecureResultColor)
			)
		);

		async void CopyToClipboard()
		{
			var clipboard = IPlatformApplication.Current?.Services.GetService<IClipboard>();
			if (clipboard is not null && !string.IsNullOrEmpty(State.ClipboardEntry))
			{
				await clipboard.SetTextAsync(State.ClipboardEntry);
				SetState(s =>
				{
					s.ClipboardResult = $"Copied: \"{s.ClipboardEntry}\"";
					s.ClipboardResultColor = Colors.Green;
				});
			}
		}

		async void PasteFromClipboard()
		{
			var clipboard = IPlatformApplication.Current?.Services.GetService<IClipboard>();
			if (clipboard is not null)
			{
				var text = await clipboard.GetTextAsync();
				SetState(s =>
				{
					s.ClipboardResult = text is not null ? $"Pasted: \"{text}\"" : "(clipboard empty)";
					s.ClipboardResultColor = text is not null ? Colors.DodgerBlue : Colors.Grey;
				});
			}
		}

		void SavePreference()
		{
			var prefs = IPlatformApplication.Current?.Services.GetService<IPreferences>();
			if (prefs is not null && !string.IsNullOrEmpty(State.PrefsKey))
			{
				prefs.Set(State.PrefsKey, State.PrefsValue ?? "", null);
				SetState(s =>
				{
					s.PrefsResult = $"Saved: {s.PrefsKey} = \"{s.PrefsValue}\"";
					s.PrefsResultColor = Colors.Green;
				});
			}
		}

		void LoadPreference()
		{
			var prefs = IPlatformApplication.Current?.Services.GetService<IPreferences>();
			if (prefs is not null && !string.IsNullOrEmpty(State.PrefsKey))
			{
				var value = prefs.Get(State.PrefsKey, "(not found)", null);
				SetState(s =>
				{
					s.PrefsResult = $"Loaded: {s.PrefsKey} = \"{value}\"";
					s.PrefsResultColor = Colors.DodgerBlue;
				});
			}
		}

		void ClearPreferences()
		{
			var prefs = IPlatformApplication.Current?.Services.GetService<IPreferences>();
			prefs?.Clear(null);
			SetState(s =>
			{
				s.PrefsResult = "All preferences cleared";
				s.PrefsResultColor = Colors.OrangeRed;
			});
		}

		async void StoreSecret()
		{
			var secure = IPlatformApplication.Current?.Services.GetService<ISecureStorage>();
			if (secure is not null && !string.IsNullOrEmpty(State.SecureKey))
			{
				await secure.SetAsync(State.SecureKey, State.SecureValue ?? "");
				SetState(s =>
				{
					s.SecureResult = $"Stored secret for key: {s.SecureKey}";
					s.SecureResultColor = Colors.Green;
				});
			}
		}

		async void RetrieveSecret()
		{
			var secure = IPlatformApplication.Current?.Services.GetService<ISecureStorage>();
			if (secure is not null && !string.IsNullOrEmpty(State.SecureKey))
			{
				var value = await secure.GetAsync(State.SecureKey);
				SetState(s =>
				{
					s.SecureResult = value is not null ? $"Retrieved: \"{value}\"" : "(not found)";
					s.SecureResultColor = value is not null ? Colors.DodgerBlue : Colors.Grey;
				});
			}
		}
	}
}
