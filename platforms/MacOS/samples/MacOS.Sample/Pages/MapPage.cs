using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
#if MACAPP
using Microsoft.Maui.Platforms.MacOS.Controls;
#elif TVAPP
using Microsoft.Maui.Platform.TvOS.Controls;
#endif

namespace MacOS.Sample.Pages;

public class MapPage : ContentPage
{
    readonly MapView _mapView;
    readonly Label _coordsLabel;

    static readonly (string Name, double Lat, double Lon)[] Locations =
    [
        ("Seattle", 47.6062, -122.3321),
        ("San Francisco", 37.7749, -122.4194),
        ("New York", 40.7128, -74.0060),
        ("London", 51.5074, -0.1278),
        ("Tokyo", 35.6762, 139.6503),
        ("Sydney", -33.8688, 151.2093),
    ];

    public MapPage()
    {
        Title = "Map";

        _coordsLabel = new Label
        {
            Text = "Seattle (47.61, -122.33)",
            FontSize = 14,
            TextColor = Colors.Gray,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        _mapView = new MapView
        {
            HeightRequest = 400,
            Latitude = Locations[0].Lat,
            Longitude = Locations[0].Lon,
            LatitudeDelta = 0.05,
            LongitudeDelta = 0.05,
        };

        // Add pins for all locations
        foreach (var loc in Locations)
        {
            _mapView.Pins.Add(new MapPin
            {
                Latitude = loc.Lat,
                Longitude = loc.Lon,
                Label = loc.Name,
                Address = $"{loc.Lat:F4}, {loc.Lon:F4}",
            });
        }

        // Add a circle around Seattle
        _mapView.Circles.Add(new MapCircle
        {
            CenterLatitude = 47.6062,
            CenterLongitude = -122.3321,
            Radius = 2000,
            StrokeColor = Colors.DodgerBlue,
            FillColor = Color.FromRgba(30, 144, 255, 50),
            StrokeWidth = 2,
        });

        // Add a polyline route: Seattle → San Francisco
        _mapView.Polylines.Add(new MapPolyline
        {
            Positions =
            [
                (47.6062, -122.3321),
                (45.5152, -122.6784),
                (42.3601, -122.9120),
                (39.8283, -121.9780),
                (37.7749, -122.4194),
            ],
            StrokeColor = Colors.OrangeRed,
            StrokeWidth = 3,
        });

        // Add a polygon around downtown Seattle
        _mapView.Polygons.Add(new MapPolygon
        {
            Positions =
            [
                (47.6130, -122.3470),
                (47.6130, -122.3280),
                (47.6030, -122.3280),
                (47.6030, -122.3470),
            ],
            StrokeColor = Colors.Green,
            FillColor = Color.FromRgba(0, 128, 0, 40),
            StrokeWidth = 2,
        });

        var locationPicker = new Picker { Title = "Jump to location..." };
        foreach (var loc in Locations)
            locationPicker.Items.Add(loc.Name);

        locationPicker.SelectedIndexChanged += (s, e) =>
        {
            if (locationPicker.SelectedIndex < 0) return;
            var loc = Locations[locationPicker.SelectedIndex];
            _mapView.Latitude = loc.Lat;
            _mapView.Longitude = loc.Lon;
            _coordsLabel.Text = $"{loc.Name} ({loc.Lat:F2}, {loc.Lon:F2})";
        };

        var addPinButton = new Button { Text = "Add Pin at Center" };
        addPinButton.Clicked += (s, e) =>
        {
            _mapView.Pins.Add(new MapPin
            {
                Latitude = _mapView.Latitude,
                Longitude = _mapView.Longitude,
                Label = $"Pin #{_mapView.Pins.Count + 1}",
                Address = $"{_mapView.Latitude:F4}, {_mapView.Longitude:F4}",
            });
        };

        var clearButton = new Button { Text = "Clear All" };
        clearButton.Clicked += (s, e) =>
        {
            _mapView.Pins.Clear();
            _mapView.Circles.Clear();
            _mapView.Polylines.Clear();
            _mapView.Polygons.Clear();
        };

#if MACAPP
        var mapTypePicker = new Picker { Title = "Map type..." };
        mapTypePicker.Items.Add("Standard");
        mapTypePicker.Items.Add("Satellite");
        mapTypePicker.Items.Add("Hybrid");
        mapTypePicker.SelectedIndex = 0;
        mapTypePicker.SelectedIndexChanged += (s, e) =>
        {
            _mapView.MapType = mapTypePicker.SelectedIndex switch
            {
                1 => MapType.Satellite,
                2 => MapType.Hybrid,
                _ => MapType.Standard,
            };
        };

        var zoomInButton = new Button { Text = "Zoom In" };
        zoomInButton.Clicked += (s, e) =>
        {
            _mapView.LatitudeDelta = Math.Max(0.001, _mapView.LatitudeDelta / 2);
            _mapView.LongitudeDelta = Math.Max(0.001, _mapView.LongitudeDelta / 2);
        };

        var zoomOutButton = new Button { Text = "Zoom Out" };
        zoomOutButton.Clicked += (s, e) =>
        {
            _mapView.LatitudeDelta = Math.Min(90, _mapView.LatitudeDelta * 2);
            _mapView.LongitudeDelta = Math.Min(180, _mapView.LongitudeDelta * 2);
        };
#endif

        var stack = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(24),
            Children =
            {
                new Label
                {
                    Text = "🗺️ MapView",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                },
                locationPicker,
#if MACAPP
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children = { mapTypePicker, zoomInButton, zoomOutButton },
                },
#endif
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children = { addPinButton, clearButton },
                },
                _coordsLabel,
                _mapView,
                new Label
                {
                    Text = "📍 Pins at all cities • 🔵 Circle around Seattle • 🟠 Route to SF • 🟩 Downtown polygon",
                    FontSize = 12,
                    TextColor = Colors.Gray,
                },
            },
        };

        Content = new ScrollView { Content = stack };
    }
}
