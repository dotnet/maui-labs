using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	// Note: The TS MapPage uses a custom MapView control (macOS-specific MKMapView wrapper)
	// that is not available in Comet. This demo shows the map concept with location data
	// and documents the approach for when map support is added.
	public class MapPageState
	{
		public int SelectedLocation { get; set; }
		public string CoordsText { get; set; } = "Seattle (47.61, -122.33)";
		public int PinCount { get; set; } = 6;
	}

	public class MapPage : Component<MapPageState>
	{
		static readonly (string Name, double Lat, double Lon)[] Locations = new[]
		{
			("Seattle", 47.6062, -122.3321),
			("San Francisco", 37.7749, -122.4194),
			("New York", 40.7128, -74.0060),
			("London", 51.5074, -0.1278),
			("Tokyo", 35.6762, 139.6503),
			("Sydney", -33.8688, 151.2093),
		};

		public override View Render() => GalleryPageHelpers.Scaffold("Map",
			// Title
			Text("MapView")
				.FontSize(24)
				.FontWeight(FontWeight.Bold),

			Text("The TS uses a custom MapView backed by MKMapView. Comet does not yet wrap MKMapView natively. "
				+ "Below is a location selector showing the data that would drive the map.")
				.FontSize(13)
				.Color(Colors.Grey),

			// Location selector
			GalleryPageHelpers.Section("Locations",
				VStack(4, BuildLocationButtons()),
				Text(() => State.CoordsText)
					.FontSize(14)
					.Color(Colors.Grey)
			),

			// Map placeholder
			GalleryPageHelpers.Section("Map Area",
				Border(
					VStack(12,
						Text("Map View Placeholder")
							.FontSize(18)
							.FontWeight(FontWeight.Bold)
							.Color(Colors.White)
							.HorizontalTextAlignment(TextAlignment.Center),
						Text(() => $"{Locations[State.SelectedLocation].Name}")
							.FontSize(14)
							.Color(Colors.White)
							.HorizontalTextAlignment(TextAlignment.Center),
						Text(() => $"Lat: {Locations[State.SelectedLocation].Lat:F4}, Lon: {Locations[State.SelectedLocation].Lon:F4}")
							.FontSize(12)
							.Color(new Color(200, 200, 200))
							.HorizontalTextAlignment(TextAlignment.Center)
					)
					.Padding(new Thickness(24))
				)
				.Background(new Color(74, 144, 226))
				.CornerRadius(8)
				.Frame(height: 300)
			),

			// Pin info
			GalleryPageHelpers.Section("Map Features",
				Text(() => $"Pins at all {Locations.Length} cities")
					.FontSize(14),
				Text("Circle overlay around Seattle")
					.FontSize(14),
				Text("Polyline route: Seattle to San Francisco")
					.FontSize(14),
				Text("Polygon around downtown Seattle")
					.FontSize(14),
				Button("Add Pin at Center", () =>
					SetState(s => s.PinCount++)),
				Text(() => $"Total pins: {State.PinCount}")
					.FontSize(12)
					.Color(Colors.Grey)
			)
		);

		View[] BuildLocationButtons()
		{
			var buttons = new View[Locations.Length];
			for (int i = 0; i < Locations.Length; i++)
			{
				var index = i;
				var loc = Locations[i];
				var isSelected = State.SelectedLocation == index;
				buttons[i] = Button($"{loc.Name} ({loc.Lat:F2}, {loc.Lon:F2})", () =>
				{
					SetState(s =>
					{
						s.SelectedLocation = index;
						s.CoordsText = $"{loc.Name} ({loc.Lat:F2}, {loc.Lon:F2})";
					});
				})
				.Color(isSelected ? Colors.White : Colors.CornflowerBlue)
				.Background(isSelected ? Colors.CornflowerBlue : Colors.Transparent)
				.FontSize(13);
			}
			return buttons;
		}
	}
}
