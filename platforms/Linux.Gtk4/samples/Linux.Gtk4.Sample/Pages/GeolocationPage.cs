using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Sample.Pages;

class GeolocationPage : ContentPage
{
    static readonly TimeSpan GeocodingTimeout = TimeSpan.FromSeconds(15);

    // Geolocation
    readonly Picker _accuracyPicker;
    readonly Switch _fullAccuracySwitch;
    readonly Entry _timeoutEntry;
    readonly Entry _minimumTimeEntry;
    readonly Label _resultLabel;
    readonly Label _listeningLabel;
    readonly Label _updatesLabel;
    readonly Button _cancelButton;
    readonly Button _startListeningButton;
    readonly Button _stopListeningButton;

    // Geocoding
    readonly Entry _addressEntry;
    readonly Entry _latitudeEntry;
    readonly Entry _longitudeEntry;
    readonly Label _forwardResultLabel;
    readonly Label _reverseResultLabel;
    readonly ActivityIndicator _geocodingBusyIndicator;
    readonly Button _forwardButton;
    readonly Button _reverseButton;
    readonly Button _useLocationButton;

    CancellationTokenSource? _requestCts;
    Location? _currentLocation;
    int _updateCount;

    public GeolocationPage()
    {
        Title = "Geolocation & Geocoding";

        // ---------- Geolocation controls ----------

        _accuracyPicker = new Picker
        {
            ItemsSource = Enum.GetNames<GeolocationAccuracy>(),
            SelectedItem = nameof(GeolocationAccuracy.Medium),
            HorizontalOptions = LayoutOptions.Fill
        };

        _fullAccuracySwitch = new Switch { IsToggled = false, VerticalOptions = LayoutOptions.Center };

        _timeoutEntry = new Entry { Keyboard = Keyboard.Numeric, Text = "30" };
        _minimumTimeEntry = new Entry { Keyboard = Keyboard.Numeric, Text = "1" };

        _resultLabel = CreateResultLabel("No location yet.");
        _updatesLabel = CreateResultLabel();
        _listeningLabel = CreateInfoLabel("Not listening.");

        _cancelButton = CreateButton("Cancel", Colors.Gray, () => _requestCts?.Cancel());
        _cancelButton.IsEnabled = false;

        _startListeningButton = CreateButton("Start Listening", Colors.MediumSeaGreen, async () => await StartListeningAsync());
        _stopListeningButton = CreateButton("Stop", Colors.Tomato, StopListening);
        _stopListeningButton.IsEnabled = false;

        // ---------- Geocoding controls ----------

        _addressEntry = new Entry
        {
            Placeholder = "Address, place or city",
            Text = "Place Royale, Nantes, France"
        };

        // No Keyboard.Numeric here: the values are set from code after geocoding,
        // and the GTK numeric filter does not cope with '-' / '.' being assigned programmatically.
        _latitudeEntry = new Entry { Placeholder = "Latitude", Text = "47.2184" };
        _longitudeEntry = new Entry { Placeholder = "Longitude", Text = "-1.5536" };

        _forwardResultLabel = CreateResultLabel();
        _reverseResultLabel = CreateResultLabel();

        _geocodingBusyIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            Color = Colors.DodgerBlue,
            HeightRequest = 24,
            WidthRequest = 24,
            VerticalOptions = LayoutOptions.Center
        };

        _forwardButton = CreateButton("Get Locations", Colors.DodgerBlue, async () => await ForwardGeocodeAsync());
        _useLocationButton = CreateButton("Use Current Location", Colors.SlateGray, async () => await FillWithCurrentLocationAsync());
        _reverseButton = CreateButton("Get Placemarks", Colors.MediumSeaGreen, async () => await ReverseGeocodeAsync());

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Padding = new Thickness(16, 12),
                Children =
                {
                    new Label
                    {
                        Text = "Geolocation & Geocoding APIs",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold
                    },

                    CreateInfoLabel("Geolocation via GeoClue2 (D-Bus system bus), forward and reverse Geocoding."),

                    // ---------- Status ----------
                    BuildSectionHeader("Feature Support"),
                    BuildSupportStatus(),

                    // ---------- Settings ----------
                    BuildSectionHeader("Request Settings"),
                    FormRow("Accuracy", _accuracyPicker),
                    FormRow("Full accuracy", _fullAccuracySwitch),
                    FormRow("Timeout (s)", _timeoutEntry, "Min. interval (s)", _minimumTimeEntry),

                    // ---------- Single location ----------
                    BuildSectionHeader("Location"),
                    ButtonRow(
                        CreateButton("Last Known", Colors.SlateGray, async () => await GetLastKnownLocationAsync()),
                        CreateButton("Current", Colors.DodgerBlue, async () => await GetCurrentLocationAsync()),
                        _cancelButton,
                        CreateButton("OpenStreetMap", Colors.Orchid, async () => await OpenInMapAsync())),
                    _resultLabel,

                    // ---------- Listening ----------
                    BuildSectionHeader("Foreground Listening"),
                    ButtonRow(_startListeningButton, _stopListeningButton),
                    _listeningLabel,
                    _updatesLabel,

                    // ---------- Geocoding ----------
                    BuildSectionHeader("Geocoding: Address → Coordinates"),
                    _addressEntry,
                    ButtonRow(_forwardButton, _geocodingBusyIndicator),
                    _forwardResultLabel,

                    BuildSectionHeader("Geocoding: Coordinates → Address"),
                    FormRow("Latitude", _latitudeEntry, "Longitude", _longitudeEntry),
                    ButtonRow(_useLocationButton, _reverseButton),
                    _reverseResultLabel,

                    // ---------- Notes ----------
                    BuildSectionHeader("Notes"),
                    CreateInfoLabel(
                        "• Requires the geoclue-2.0 service; the agent authorizes the app by desktop id " +
                        $"'{LinuxGeolocation.DesktopId}' (needs a matching .desktop file).\n" +
                        "• Diagnose with: systemctl status geoclue, /usr/libexec/geoclue-2.0/demos/where-am-i\n" +
                        "• Geocoding needs network access; Nominatim allows 1 request/s and requires a User-Agent.\n" +
                        "• Coordinates accept '.' or ',' as decimal separator.")
                }
            }
        };
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _requestCts?.Cancel();
        StopListening();
    }

    // ---------- UI helpers ----------

    static Label BuildSectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        Margin = new Thickness(0, 10, 0, 0)
    };

    static Label CreateInfoLabel(string text) => new()
    {
        Text = text,
        TextColor = Colors.Gray,
        FontSize = 12,
        LineBreakMode = LineBreakMode.WordWrap
    };

    static Label CreateResultLabel(string? text = null) => new()
    {
        Text = text,
        FontFamily = "monospace",
        FontSize = 12,
        LineBreakMode = LineBreakMode.WordWrap
    };

    static Button CreateButton(string text, Color color, Action action) => new()
    {
        Text = text,
        BackgroundColor = color,
        TextColor = Colors.White,
        FontSize = 13,
        Padding = new Thickness(12, 4),
        Command = new Command(action)
    };

    // Buttons wrap onto the next line when the window is narrow.
    static FlexLayout ButtonRow(params View[] views)
    {
        var row = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            JustifyContent = FlexJustify.Start
        };

        foreach (var view in views)
        {
            view.Margin = new Thickness(0, 0, 8, 4);
            row.Children.Add(view);
        }

        return row;
    }

    // Label/value pairs on a single line: [label] [value] ([label] [value]).
    static Grid FormRow(string label, View view, string? label2 = null, View? view2 = null)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = label2 is null
                ? new ColumnDefinitionCollection(new(110), new(GridLength.Star))
                : new ColumnDefinitionCollection(new(110), new(GridLength.Star), new(110), new(GridLength.Star))
        };

        grid.Add(CreateFormLabel(label), 0);
        grid.Add(view, 1);

        if (label2 is not null && view2 is not null)
        {
            grid.Add(CreateFormLabel(label2), 2);
            grid.Add(view2, 3);
        }

        return grid;
    }

    static Label CreateFormLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        VerticalOptions = LayoutOptions.Center
    };

    static View BuildSupportStatus()
    {
        return new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                CreateStatusRow("Geolocation", true, "(GeoClue2 / D-Bus)"),
                CreateStatusRow("Geocoding", true, "(network provider)"),
            }
        };
    }

    static View CreateStatusRow(string name, bool supported, string? info = null)
    {
        return new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label
                {
                    Text = supported ? "✓" : "✗",
                    TextColor = supported ? Colors.MediumSeaGreen : Colors.Tomato,
                    FontAttributes = FontAttributes.Bold,
                    WidthRequest = 16
                },
                new Label { Text = name, FontSize = 13 },
                new Label
                {
                    Text = supported ? "Supported " + info : "Not available",
                    TextColor = Colors.Gray,
                    FontSize = 12,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
    }

    void SetGeocodingBusy(bool busy)
    {
        _geocodingBusyIndicator.IsRunning = busy;
        _geocodingBusyIndicator.IsVisible = busy;

        // Prevents piling up requests (Nominatim rate-limits to 1 request/s).
        _forwardButton.IsEnabled = !busy;
        _reverseButton.IsEnabled = !busy;
        _useLocationButton.IsEnabled = !busy;
    }

    // ---------- Parsing / formatting ----------

    GeolocationAccuracy SelectedAccuracy =>
        Enum.TryParse<GeolocationAccuracy>(_accuracyPicker.SelectedItem as string, out var accuracy)
            ? accuracy
            : GeolocationAccuracy.Medium;

    static TimeSpan ParseSeconds(string? text, double fallback) =>
        TimeSpan.FromSeconds(
            double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
                ? seconds
                : fallback);

    static bool TryParseCoordinate(string? text, double min, double max, out double value) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && value >= min && value <= max;

    static string FormatCoordinate(double value) =>
        value.ToString("0.000000", CultureInfo.InvariantCulture);

    static string FormatLocation(Location location)
    {
        static string Format(double? value, string unit) =>
            value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) + unit : "n/a";

        return string.Join(Environment.NewLine,
            $"Lat/Lon:  {FormatCoordinate(location.Latitude)}, {FormatCoordinate(location.Longitude)}",
            $"Accuracy: {Format(location.Accuracy, " m")}   Altitude: {Format(location.Altitude, " m")}",
            $"Speed:    {Format(location.Speed, " m/s")}   Course: {Format(location.Course, "°")}",
            $"Time:     {location.Timestamp.ToLocalTime():G}");
    }

    static string FormatPlacemark(Placemark placemark)
    {
        static string Join(params string?[] parts) =>
            string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        var street = string.Join(" ", new[] { placemark.SubThoroughfare, placemark.Thoroughfare }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return string.Join(Environment.NewLine, new[]
        {
            placemark.FeatureName,
            Join(street, placemark.SubLocality),
            Join(placemark.PostalCode, placemark.Locality),
            Join(placemark.SubAdminArea, placemark.AdminArea),
            Join(placemark.CountryName, placemark.CountryCode),
        }.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    // ---------- Geolocation ----------

    async Task GetLastKnownLocationAsync()
    {
        try
        {
            var location = await Geolocation.Default.GetLastKnownLocationAsync();
            ShowLocation(location, "Last known");
        }
        catch (FeatureNotSupportedException ex)
        {
            await DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    async Task GetCurrentLocationAsync()
    {
        if (_requestCts is not null)
            return;

        _requestCts = new CancellationTokenSource();
        _cancelButton.IsEnabled = true;
        _resultLabel.Text = "Requesting location...";

        try
        {
            var request = new GeolocationRequest(SelectedAccuracy, ParseSeconds(_timeoutEntry.Text, 30))
            {
                RequestFullAccuracy = _fullAccuracySwitch.IsToggled
            };

            var location = await Geolocation.Default.GetLocationAsync(request, _requestCts.Token);

            if (location is null)
                _resultLabel.Text = "No location received (timed out or service unavailable).";
            else
                ShowLocation(location, "Current");
        }
        catch (OperationCanceledException)
        {
            _resultLabel.Text = "Request cancelled.";
        }
        catch (FeatureNotSupportedException ex)
        {
            _resultLabel.Text = "Not supported.";
            await DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (PermissionException ex)
        {
            _resultLabel.Text = "Permission denied.";
            await DisplayAlertAsync("Permission denied", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            _resultLabel.Text = "Request failed.";
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            _requestCts.Dispose();
            _requestCts = null;
            _cancelButton.IsEnabled = false;
        }
    }

    void ShowLocation(Location? location, string source)
    {
        if (location is null)
        {
            _resultLabel.Text = $"{source}: no location available.";
            return;
        }

        _currentLocation = location;
        _resultLabel.Text = $"{source}\n{FormatLocation(location)}";
    }

    async Task OpenInMapAsync()
    {
        if (_currentLocation is null)
        {
            await DisplayAlertAsync("No location", "Get a location first.", "OK");
            return;
        }

        try
        {
            var lat = _currentLocation.Latitude.ToString(CultureInfo.InvariantCulture);
            var lon = _currentLocation.Longitude.ToString(CultureInfo.InvariantCulture);
            await Launcher.Default.OpenAsync($"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=16/{lat}/{lon}");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    async Task StartListeningAsync()
    {
        if (Geolocation.Default.IsListeningForeground)
            return;

        try
        {
            var request = new GeolocationListeningRequest(SelectedAccuracy, ParseSeconds(_minimumTimeEntry.Text, 1));

            Geolocation.Default.LocationChanged += OnLocationChanged;
            Geolocation.Default.ListeningFailed += OnListeningFailed;

            _listeningLabel.Text = "Starting...";
            _startListeningButton.IsEnabled = false;

            var started = await Geolocation.Default.StartListeningForegroundAsync(request);

            if (started)
            {
                _updateCount = 0;
                _updatesLabel.Text = "";
                _listeningLabel.Text = "Listening for location updates...";
                _stopListeningButton.IsEnabled = true;
            }
            else
            {
                Unsubscribe();
                _listeningLabel.Text = "Could not start listening (service unavailable or access denied).";
                _startListeningButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            Unsubscribe();
            _listeningLabel.Text = "Not listening.";
            _startListeningButton.IsEnabled = true;
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    void StopListening()
    {
        Unsubscribe();

        if (Geolocation.Default.IsListeningForeground)
            Geolocation.Default.StopListeningForeground();

        _listeningLabel.Text = "Not listening.";
        _startListeningButton.IsEnabled = true;
        _stopListeningButton.IsEnabled = false;
    }

    void Unsubscribe()
    {
        Geolocation.Default.LocationChanged -= OnLocationChanged;
        Geolocation.Default.ListeningFailed -= OnListeningFailed;
    }

    // Events are raised from a background thread by the Linux implementation.
    void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _updateCount++;
            _currentLocation = e.Location;
            _listeningLabel.Text = $"Listening — {_updateCount} update(s) received.";
            _updatesLabel.Text = FormatLocation(e.Location);
        });

    void OnListeningFailed(object? sender, GeolocationListeningFailedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Unsubscribe();
            _listeningLabel.Text = $"Listening failed: {e.Error}.";
            _startListeningButton.IsEnabled = true;
            _stopListeningButton.IsEnabled = false;
        });

    // ---------- Geocoding ----------

    async Task ForwardGeocodeAsync()
    {
        var address = _addressEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            await DisplayAlertAsync("Input required", "Please enter an address.", "OK");
            return;
        }

        SetGeocodingBusy(true);
        _forwardResultLabel.Text = "Looking up...";

        try
        {
            var locations = await Geocoding.Default.GetLocationsAsync(address);

            _forwardResultLabel.Text = locations.Count() == 0
                ? "No result."
                : string.Join(Environment.NewLine,
                    locations.Select((l, i) => $"#{i + 1}  {FormatCoordinate(l.Latitude)}, {FormatCoordinate(l.Longitude)}"));
        }
        catch (TimeoutException)
        {
            _forwardResultLabel.Text = $"No response after {GeocodingTimeout.TotalSeconds:0} s.";
        }
        catch (FeatureNotSupportedException ex)
        {
            _forwardResultLabel.Text = "Not supported.";
            await DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            _forwardResultLabel.Text = "Lookup failed.";
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            SetGeocodingBusy(false);
        }
    }

    async Task ReverseGeocodeAsync()
    {
        if (!TryParseCoordinate(_latitudeEntry.Text, -90, 90, out var latitude) ||
            !TryParseCoordinate(_longitudeEntry.Text, -180, 180, out var longitude))
        {
            await DisplayAlertAsync("Invalid input",
                "Latitude must be between -90 and 90, longitude between -180 and 180.", "OK");
            return;
        }

        SetGeocodingBusy(true);
        _reverseResultLabel.Text = "Looking up...";

        try
        {
            var placemarks = await Geocoding.Default.GetPlacemarksAsync(latitude, longitude);

            _reverseResultLabel.Text = placemarks.Count() == 0
                ? "No result."
                : string.Join(Environment.NewLine + Environment.NewLine,
                    placemarks.Select((p, i) => $"#{i + 1}  {FormatPlacemark(p)}"));
        }
        catch (TimeoutException)
        {
            _reverseResultLabel.Text = $"No response after {GeocodingTimeout.TotalSeconds:0} s.";
        }
        catch (FeatureNotSupportedException ex)
        {
            _reverseResultLabel.Text = "Not supported.";
            await DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            _reverseResultLabel.Text = "Lookup failed.";
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            SetGeocodingBusy(false);
        }
    }

    async Task FillWithCurrentLocationAsync()
    {
        // Reuse a location already obtained on this page before querying GeoClue again.
        var location = _currentLocation;

        if (location is null)
        {
            SetGeocodingBusy(true);

            try
            {
                location = await Geolocation.Default.GetLastKnownLocationAsync()
                           ?? await Geolocation.Default.GetLocationAsync(
                               new GeolocationRequest(SelectedAccuracy, ParseSeconds(_timeoutEntry.Text, 30)));
            }
            catch (FeatureNotSupportedException ex)
            {
                await DisplayAlertAsync("Not Supported", ex.Message, "OK");
                return;
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
                return;
            }
            finally
            {
                SetGeocodingBusy(false);
            }
        }

        if (location is null)
        {
            await DisplayAlertAsync("No location", "Could not determine the current location.", "OK");
            return;
        }

        _currentLocation = location;
        _latitudeEntry.Text = FormatCoordinate(location.Latitude);
        _longitudeEntry.Text = FormatCoordinate(location.Longitude);
    }
}