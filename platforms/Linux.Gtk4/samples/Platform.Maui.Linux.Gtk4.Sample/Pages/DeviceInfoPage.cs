using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class DeviceInfoPage : ContentPage
{
	public DeviceInfoPage()
	{
		Title = "Device Info";

		var infoStack = new VerticalStackLayout { Spacing = 6 };

		// IDeviceInfo
		var deviceInfo = IPlatformApplication.Current?.Services.GetService<IDeviceInfo>();
		if (deviceInfo is not null)
		{
			infoStack.Children.Add(SectionHeader("🖥️ Device Info"));
			infoStack.Children.Add(InfoRow("Name", deviceInfo.Name));
			infoStack.Children.Add(InfoRow("Model", deviceInfo.Model));
			infoStack.Children.Add(InfoRow("Manufacturer", deviceInfo.Manufacturer));
			infoStack.Children.Add(InfoRow("Platform", deviceInfo.Platform.ToString()));
			infoStack.Children.Add(InfoRow("Idiom", deviceInfo.Idiom.ToString()));
			infoStack.Children.Add(InfoRow("Device Type", deviceInfo.DeviceType.ToString()));
			infoStack.Children.Add(InfoRow("OS Version", deviceInfo.VersionString));
			infoStack.Children.Add(Separator());
		}

		// IAppInfo
		var appInfo = IPlatformApplication.Current?.Services.GetService<IAppInfo>();
		if (appInfo is not null)
		{
			infoStack.Children.Add(SectionHeader("📦 App Info"));
			infoStack.Children.Add(InfoRow("Package Name", appInfo.PackageName));
			infoStack.Children.Add(InfoRow("App Name", appInfo.Name));
			infoStack.Children.Add(InfoRow("Version", appInfo.VersionString));
			infoStack.Children.Add(InfoRow("Build", appInfo.BuildString));
			infoStack.Children.Add(InfoRow("Theme", appInfo.RequestedTheme.ToString()));
			infoStack.Children.Add(InfoRow("Packaging Model", appInfo.PackagingModel.ToString()));
			infoStack.Children.Add(Separator());
		}

		// IDeviceDisplay
		var display = IPlatformApplication.Current?.Services.GetService<IDeviceDisplay>();
		if (display is not null)
		{
			var displayInfo = display.MainDisplayInfo;
			infoStack.Children.Add(SectionHeader("🖥️ Display"));
			infoStack.Children.Add(InfoRow("Resolution", $"{displayInfo.Width} × {displayInfo.Height}"));
			infoStack.Children.Add(InfoRow("Density", $"{displayInfo.Density:F1}"));
			infoStack.Children.Add(InfoRow("Orientation", displayInfo.Orientation.ToString()));
			infoStack.Children.Add(InfoRow("Refresh Rate", $"{displayInfo.RefreshRate:F0} Hz"));
			infoStack.Children.Add(Separator());
		}

		// IFileSystem
		var fileSystem = IPlatformApplication.Current?.Services.GetService<IFileSystem>();
		if (fileSystem is not null)
		{
			infoStack.Children.Add(SectionHeader("📁 File System"));
			infoStack.Children.Add(InfoRow("Cache Dir", fileSystem.CacheDirectory));
			infoStack.Children.Add(InfoRow("App Data Dir", fileSystem.AppDataDirectory));
		}

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "Device & App Info", FontSize = 24, FontAttributes = FontAttributes.Bold },
					new Label { Text = "Information from MAUI Essentials interfaces", FontSize = 14, TextColor = Colors.Gray },
					new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },
					infoStack,
				}
			}
		};
	}

	static Label SectionHeader(string text) => new()
	{
		Text = text, FontSize = 18, FontAttributes = FontAttributes.Bold,
		Margin = new Thickness(0, 8, 0, 4),
	};

	static Label InfoRow(string label, string? value) => new()
	{
		Text = $"  {label}: {value ?? "N/A"}",
		FontSize = 14, FontFamily = "monospace",
	};

	static BoxView Separator() => new() { HeightRequest = 1, Color = Colors.LightGray, Margin = new Thickness(0, 4) };
}
