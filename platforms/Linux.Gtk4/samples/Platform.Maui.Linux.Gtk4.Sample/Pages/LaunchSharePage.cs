using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class LaunchSharePage : ContentPage
{
	public LaunchSharePage()
	{
		Title = "Launch & Share";

		var statusLabel = new Label { FontSize = 14, TextColor = Colors.Gray };

		// --- Launcher / Browser ---
		var urlEntry = new Entry { Placeholder = "URL to open", Text = "https://github.com/nickvdyck/maui-linux-gtk4" };

		var openUrlButton = new Button { Text = "🌐 Open in Browser" };
		openUrlButton.Clicked += async (s, e) =>
		{
			var browser = IPlatformApplication.Current?.Services.GetService<IBrowser>();
			if (browser is not null && !string.IsNullOrEmpty(urlEntry.Text))
			{
				var result = await browser.OpenAsync(new Uri(urlEntry.Text), new BrowserLaunchOptions());
				statusLabel.Text = result ? "Browser opened!" : "Failed to open browser.";
				statusLabel.TextColor = result ? Colors.Green : Colors.Red;
			}
		};

		var launchFileButton = new Button { Text = "📂 Open File with Default App" };
		launchFileButton.Clicked += async (s, e) =>
		{
			var picker = IPlatformApplication.Current?.Services.GetService<IFilePicker>();
			if (picker is null) return;
			var file = await picker.PickAsync(null);
			if (file is not null)
			{
				var launcher = IPlatformApplication.Current?.Services.GetService<ILauncher>();
				if (launcher is not null)
				{
					await launcher.OpenAsync(new OpenFileRequest("Open", new ReadOnlyFile(file.FullPath)));
					statusLabel.Text = $"Launched: {file.FileName}";
					statusLabel.TextColor = Colors.Green;
				}
			}
		};

		// --- Map ---
		var mapButton = new Button { Text = "🗺️ Open Map (Seattle)" };
		mapButton.Clicked += async (s, e) =>
		{
			var map = IPlatformApplication.Current?.Services.GetService<IMap>();
			if (map is not null)
			{
				await map.OpenAsync(47.6062, -122.3321, new MapLaunchOptions { Name = "Seattle" });
				statusLabel.Text = "Map opened!";
				statusLabel.TextColor = Colors.Green;
			}
		};

		// --- Email ---
		var emailButton = new Button { Text = "✉️ Compose Email" };
		emailButton.Clicked += async (s, e) =>
		{
			var email = IPlatformApplication.Current?.Services.GetService<IEmail>();
			if (email is not null)
			{
				var msg = new EmailMessage
				{
					Subject = "Hello from Platform.Maui.Linux.Gtk4!",
					Body = "This email was composed using MAUI Essentials on Linux.",
					To = new List<string> { "test@example.com" },
				};
				await email.ComposeAsync(msg);
				statusLabel.Text = "Email composer opened!";
				statusLabel.TextColor = Colors.Green;
			}
		};

		// --- Share ---
		var shareTextButton = new Button { Text = "📤 Share Text" };
		shareTextButton.Clicked += async (s, e) =>
		{
			var share = IPlatformApplication.Current?.Services.GetService<IShare>();
			if (share is not null)
			{
				await share.RequestAsync(new ShareTextRequest
				{
					Title = "Share from MAUI",
					Text = "Hello from .NET MAUI on Linux! 🐧",
				});
				statusLabel.Text = "Shared!";
				statusLabel.TextColor = Colors.Green;
			}
		};

		// --- File Picker ---
		var pickedFileLabel = new Label { Text = "(no file picked)", FontSize = 14, TextColor = Colors.Gray };
		var pickFileButton = new Button { Text = "📎 Pick a File" };
		pickFileButton.Clicked += async (s, e) =>
		{
			var picker = IPlatformApplication.Current?.Services.GetService<IFilePicker>();
			if (picker is not null)
			{
				var result = await picker.PickAsync(new PickOptions { PickerTitle = "Select any file" });
				if (result is not null)
				{
					pickedFileLabel.Text = $"Picked: {result.FileName}\nPath: {result.FullPath}";
					pickedFileLabel.TextColor = Colors.DodgerBlue;
				}
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
					new Label { Text = "Launch, Share & Files", FontSize = 24, FontAttributes = FontAttributes.Bold },
					new Label { Text = "Open URLs, share content, pick files, send email", FontSize = 14, TextColor = Colors.Gray },
					new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },

					statusLabel,

					SectionHeader("🌐 Browser & Launcher"),
					urlEntry,
					new HorizontalStackLayout { Spacing = 8, Children = { openUrlButton, launchFileButton } },

					Separator(),

					SectionHeader("🗺️ Maps"),
					mapButton,

					Separator(),

					SectionHeader("✉️ Email"),
					emailButton,

					Separator(),

					SectionHeader("📤 Share"),
					shareTextButton,

					Separator(),

					SectionHeader("📎 File Picker"),
					pickFileButton,
					pickedFileLabel,
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
