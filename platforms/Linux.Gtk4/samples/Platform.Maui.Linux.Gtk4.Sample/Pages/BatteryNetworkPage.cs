using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class BatteryNetworkPage : ContentPage
{
	private readonly Label _batteryLevel;
	private readonly Label _batteryState;
	private readonly Label _batterySource;
	private readonly ProgressBar _batteryBar;
	private readonly Label _networkAccess;
	private readonly Label _connectionProfiles;

	public BatteryNetworkPage()
	{
		Title = "Battery & Network";

		_batteryLevel = new Label { FontSize = 14, FontFamily = "monospace" };
		_batteryState = new Label { FontSize = 14, FontFamily = "monospace" };
		_batterySource = new Label { FontSize = 14, FontFamily = "monospace" };
		_batteryBar = new ProgressBar();
		_networkAccess = new Label { FontSize = 14, FontFamily = "monospace" };
		_connectionProfiles = new Label { FontSize = 14, FontFamily = "monospace" };

		var refreshButton = new Button { Text = "🔄 Refresh" };
		refreshButton.Clicked += (s, e) => Refresh();

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "Battery & Network", FontSize = 24, FontAttributes = FontAttributes.Bold },
					new Label { Text = "Live system status from /sys and /proc", FontSize = 14, TextColor = Colors.Gray },
					new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },

					refreshButton,

					SectionHeader("🔋 Battery"),
					_batteryBar,
					_batteryLevel,
					_batteryState,
					_batterySource,

					Separator(),

					SectionHeader("🌐 Network"),
					_networkAccess,
					_connectionProfiles,
				}
			}
		};

		Refresh();
	}

	private void Refresh()
	{
		var battery = IPlatformApplication.Current?.Services.GetService<IBattery>();
		if (battery is not null)
		{
			_batteryBar.Progress = battery.ChargeLevel;
			_batteryLevel.Text = $"  Charge Level: {battery.ChargeLevel:P0}";
			_batteryState.Text = $"  State: {battery.State}";
			_batterySource.Text = $"  Power Source: {battery.PowerSource}";
		}
		else
		{
			_batteryLevel.Text = "  Battery service not available";
		}

		var connectivity = IPlatformApplication.Current?.Services.GetService<IConnectivity>();
		if (connectivity is not null)
		{
			_networkAccess.Text = $"  Network Access: {connectivity.NetworkAccess}";
			var profiles = string.Join(", ", connectivity.ConnectionProfiles);
			_connectionProfiles.Text = $"  Profiles: {(string.IsNullOrEmpty(profiles) ? "None" : profiles)}";
		}
		else
		{
			_networkAccess.Text = "  Connectivity service not available";
		}
	}

	static Label SectionHeader(string text) => new()
	{
		Text = text, FontSize = 18, FontAttributes = FontAttributes.Bold,
		Margin = new Thickness(0, 8, 0, 4),
	};
	static BoxView Separator() => new() { HeightRequest = 1, Color = Colors.LightGray, Margin = new Thickness(0, 4) };
}
