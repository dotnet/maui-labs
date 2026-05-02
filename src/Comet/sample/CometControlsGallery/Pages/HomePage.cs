using System;
using System.Runtime.InteropServices;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class HomePage : View
	{
		static string PlatformName =>
			DeviceInfo.Platform == DevicePlatform.iOS ? "iOS"
			: DeviceInfo.Platform == DevicePlatform.Android ? "Android"
			: DeviceInfo.Platform == DevicePlatform.WinUI ? "Windows"
			: DeviceInfo.Platform == DevicePlatform.macOS ? "macOS"
			: DeviceInfo.Platform == DevicePlatform.MacCatalyst ? "Mac Catalyst"
			: "Unknown";

		static string NativeKit =>
			DeviceInfo.Platform == DevicePlatform.iOS ? "UIKit"
			: DeviceInfo.Platform == DevicePlatform.MacCatalyst ? "AppKit"
			: DeviceInfo.Platform == DevicePlatform.Android ? "Android Views"
			: DeviceInfo.Platform == DevicePlatform.WinUI ? "WinUI"
			: "native views";

		static string PlatformTitle => $"\ud83c\udf4e Comet on {PlatformName}";
		static string PlatformSubtitle => $"Rendered natively with {NativeKit}";
		static string PlatformDescription =>
			$"This sample app demonstrates Comet on {PlatformName} \u2014 " +
			"a minimal MVU framework that uses .NET MAUI core " +
			"to map native controls. No MAUI Controls required!";

		[Body]
		View Body() =>
			ScrollView(Orientation.Vertical,
				VStack(16,
					Text(PlatformTitle)
						.FontSize(32)
						.FontWeight(FontWeight.Bold)
						.HorizontalTextAlignment(TextAlignment.Center),
					Text(PlatformSubtitle)
						.FontSize(16)
						.Color(Colors.Grey)
						.HorizontalTextAlignment(TextAlignment.Center),
					Text(PlatformDescription)
						.FontSize(14),
					Border(
						VStack(8,
							Text("Platform Details")
								.FontSize(18)
								.FontWeight(FontWeight.Bold),
							Text("\u2022 Comet control handlers mapped to UIKit")
								.FontSize(14),
							Text("\u2022 Native rendering")
								.FontSize(14),
							Text("\u2022 WebKit for BlazorWebView")
								.FontSize(14),
							Text("\u2022 CoreGraphics-backed ICanvas for GraphicsView")
								.FontSize(14),
							Text("\u2022 .NET 10 / MAUI 10")
								.FontSize(14),
							Text($"\u2022 Runtime: {RuntimeInformation.FrameworkDescription}")
								.FontSize(14)
								.Color(Colors.Grey),
							Text($"\u2022 OS: {RuntimeInformation.OSDescription}")
								.FontSize(14)
								.Color(Colors.Grey)
						)
						.Padding(new Thickness(16))
					)
					.CornerRadius(8)
					.StrokeColor(Colors.DodgerBlue)
					.StrokeThickness(1),
					Text(DeviceInfo.Idiom == DeviceIdiom.Phone
						? "Tap the back button to browse control demos."
						: "Use the menu on the left to explore different control demos.")
						.FontSize(14)
						.Color(Colors.Grey)
						.HorizontalTextAlignment(TextAlignment.Center)
				)
				.Padding(new Thickness(24))
			)
			.Title("Home");
	}
}
