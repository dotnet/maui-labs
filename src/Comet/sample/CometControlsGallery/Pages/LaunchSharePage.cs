using System;
using Comet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class LaunchShareState
	{
		public string Url { get; set; } = "https://github.com/nickvdyck/maui-linux-gtk4";
		public string StatusText { get; set; } = "";
		public Color StatusColor { get; set; } = Colors.Grey;
		public string PickedFileText { get; set; } = "(no file picked)";
		public Color PickedFileColor { get; set; } = Colors.Grey;
	}

	public class LaunchSharePage : Component<LaunchShareState>
	{
		public override View Render() => GalleryPageHelpers.Scaffold("Launch & Share",
			// Status
			Text(() => State.StatusText)
				.FontSize(14)
				.Color(() => State.StatusColor),

			// Browser & Launcher section
			GalleryPageHelpers.Section("Browser & Launcher",
				TextField(() => State.Url, () => "URL to open")
					.OnTextChanged(v => SetState(s => s.Url = v)),
				GalleryPageHelpers.ButtonRow(8,
					Button("Open in Browser", OpenInBrowser),
					Button("Open File with Default App", LaunchFile)
				)
			),

			// Share section
			GalleryPageHelpers.Section("Share",
				Button("Share Text", ShareText)
			),

			// File Picker section
			GalleryPageHelpers.Section("File Picker",
				Button("Pick a File", PickFile),
				Text(() => State.PickedFileText)
					.FontSize(14)
					.Color(() => State.PickedFileColor)
			)
		);

		async void OpenInBrowser()
		{
			var browser = IPlatformApplication.Current?.Services.GetService<IBrowser>();
			if (browser is not null && !string.IsNullOrEmpty(State.Url))
			{
				var result = await browser.OpenAsync(new Uri(State.Url), new BrowserLaunchOptions());
				SetState(s =>
				{
					s.StatusText = result ? "Browser opened!" : "Failed to open browser.";
					s.StatusColor = result ? Colors.Green : Colors.Red;
				});
			}
		}

		async void LaunchFile()
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
					SetState(s =>
					{
						s.StatusText = $"Launched: {file.FileName}";
						s.StatusColor = Colors.Green;
					});
				}
			}
		}

		async void ShareText()
		{
			var share = IPlatformApplication.Current?.Services.GetService<IShare>();
			if (share is not null)
			{
				await share.RequestAsync(new ShareTextRequest
				{
					Title = "Share from MAUI",
					Text = $"Hello from .NET MAUI on {DeviceInfo.Platform}!",
				});
				SetState(s =>
				{
					s.StatusText = "Shared!";
					s.StatusColor = Colors.Green;
				});
			}
		}

		async void PickFile()
		{
			var picker = IPlatformApplication.Current?.Services.GetService<IFilePicker>();
			if (picker is not null)
			{
				var result = await picker.PickAsync(new PickOptions { PickerTitle = "Select any file" });
				if (result is not null)
				{
					SetState(s =>
					{
						s.PickedFileText = $"Picked: {result.FileName}\nPath: {result.FullPath}";
						s.PickedFileColor = Colors.DodgerBlue;
					});
				}
			}
		}
	}
}
