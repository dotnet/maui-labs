using System;
using System.Linq;
using Comet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class BatteryNetworkState
	{
		public double ChargeLevel { get; set; }
		public string BatteryLevelText { get; set; } = "  Battery service not available";
		public string BatteryStateText { get; set; } = "";
		public string BatterySourceText { get; set; } = "";
		public string NetworkAccessText { get; set; } = "  Connectivity service not available";
		public string ConnectionProfilesText { get; set; } = "";
	}

	public class BatteryNetworkPage : Component<BatteryNetworkState>
	{
		public BatteryNetworkPage()
		{
			Refresh();
		}

		void Refresh()
		{
			SetState(s =>
			{
				var battery = IPlatformApplication.Current?.Services.GetService<IBattery>();
				if (battery is not null)
				{
					s.ChargeLevel = battery.ChargeLevel;
					s.BatteryLevelText = $"  Charge Level: {battery.ChargeLevel:P0}";
					s.BatteryStateText = $"  State: {battery.State}";
					s.BatterySourceText = $"  Power Source: {battery.PowerSource}";
				}
				else
				{
					s.ChargeLevel = 0;
					s.BatteryLevelText = "  Battery service not available";
					s.BatteryStateText = "";
					s.BatterySourceText = "";
				}

				var connectivity = IPlatformApplication.Current?.Services.GetService<IConnectivity>();
				if (connectivity is not null)
				{
					s.NetworkAccessText = $"  Network Access: {connectivity.NetworkAccess}";
					var profiles = string.Join(", ", connectivity.ConnectionProfiles);
					s.ConnectionProfilesText = $"  Profiles: {(string.IsNullOrEmpty(profiles) ? "None" : profiles)}";
				}
				else
				{
					s.NetworkAccessText = "  Connectivity service not available";
					s.ConnectionProfilesText = "";
				}
			});
		}

		public override View Render()
		{
			return GalleryPageHelpers.Scaffold("Battery & Network",
				Button("Refresh", () => Refresh()),

				GalleryPageHelpers.Section("Battery",
					ProgressBar(() => State.ChargeLevel),
					Text(() => State.BatteryLevelText)
						.FontSize(14)
						.FontFamily("monospace"),
					Text(() => State.BatteryStateText)
						.FontSize(14)
						.FontFamily("monospace"),
					Text(() => State.BatterySourceText)
						.FontSize(14)
						.FontFamily("monospace")
				),

				GalleryPageHelpers.Section("Network",
					Text(() => State.NetworkAccessText)
						.FontSize(14)
						.FontFamily("monospace"),
					Text(() => State.ConnectionProfilesText)
						.FontSize(14)
						.FontFamily("monospace")
				)
			);
		}
	}
}
