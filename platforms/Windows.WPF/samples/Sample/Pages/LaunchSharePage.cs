using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Sample.Pages;

public class LaunchSharePage : ContentPage
{
	public LaunchSharePage()
	{
		Title = "Launch & Share";

		var statusLabel = new Label { FontSize = 14, TextColor = Colors.Gray };

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

		var shareTextButton = new Button { Text = "📤 Share Text" };
		shareTextButton.Clicked += async (s, e) =>
		{
			var share = IPlatformApplication.Current?.Services.GetService<IShare>();
			if (share is not null)
			{
				await share.RequestAsync(new ShareTextRequest
				{
					Title = "Share from MAUI",
					Text = "Hello from .NET MAUI on macOS! 🍎",
				});
				statusLabel.Text = "Shared!";
				statusLabel.TextColor = Colors.Green;
			}
		};

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
					statusLabel,

					SectionHeader("🌐 Browser & Launcher"),
					urlEntry,
					new HorizontalStackLayout { Spacing = 8, Children = { openUrlButton, launchFileButton } },

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
	static Border Separator() => new() { HeightRequest = 1, BackgroundColor = Colors.LightGray, StrokeThickness = 0, Margin = new Thickness(0, 4) };
}
