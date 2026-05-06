using System;
using System.Collections.Generic;
using Comet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class DeviceInfoPage : View
	{
		[Body]
		View body()
		{
			var sections = new List<View>();

			var deviceInfo = IPlatformApplication.Current?.Services.GetService<IDeviceInfo>();
			if (deviceInfo is not null)
			{
				sections.Add(GalleryPageHelpers.Section("Device Info",
					InfoRow("Name", deviceInfo.Name),
					InfoRow("Model", deviceInfo.Model),
					InfoRow("Manufacturer", deviceInfo.Manufacturer),
					InfoRow("Platform", deviceInfo.Platform.ToString()),
					InfoRow("Idiom", deviceInfo.Idiom.ToString()),
					InfoRow("Device Type", deviceInfo.DeviceType.ToString()),
					InfoRow("OS Version", deviceInfo.VersionString)
				));
			}

			var appInfo = IPlatformApplication.Current?.Services.GetService<IAppInfo>();
			if (appInfo is not null)
			{
				sections.Add(GalleryPageHelpers.Section("App Info",
					InfoRow("Package Name", appInfo.PackageName),
					InfoRow("App Name", appInfo.Name),
					InfoRow("Version", appInfo.VersionString),
					InfoRow("Build", appInfo.BuildString),
					InfoRow("Theme", appInfo.RequestedTheme.ToString()),
					InfoRow("Packaging Model", appInfo.PackagingModel.ToString())
				));
			}

			var display = IPlatformApplication.Current?.Services.GetService<IDeviceDisplay>();
			if (display is not null)
			{
				var displayInfo = display.MainDisplayInfo;
				sections.Add(GalleryPageHelpers.Section("Display",
					InfoRow("Resolution", $"{displayInfo.Width} x {displayInfo.Height}"),
					InfoRow("Density", $"{displayInfo.Density:F1}"),
					InfoRow("Orientation", displayInfo.Orientation.ToString()),
					InfoRow("Refresh Rate", $"{displayInfo.RefreshRate:F0} Hz")
				));
			}

			var fileSystem = IPlatformApplication.Current?.Services.GetService<IFileSystem>();
			if (fileSystem is not null)
			{
				sections.Add(GalleryPageHelpers.Section("File System",
					InfoRow("Cache Dir", fileSystem.CacheDirectory),
					InfoRow("App Data Dir", fileSystem.AppDataDirectory)
				));
			}

			return GalleryPageHelpers.Scaffold("Device & App Info",
				sections.ToArray()
			);
		}

		static View InfoRow(string label, string value) =>
			Text($"  {label}: {value ?? "N/A"}")
				.FontSize(14)
				.FontFamily("monospace");
	}
}
