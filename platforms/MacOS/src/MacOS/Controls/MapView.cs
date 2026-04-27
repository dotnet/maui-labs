using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Controls;

public enum MapType
{
    Standard,
    Satellite,
    Hybrid,
}

public class MapPin
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public class MapCircle
{
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public double Radius { get; set; } = 1000;
    public Color? StrokeColor { get; set; }
    public Color? FillColor { get; set; }
    public float StrokeWidth { get; set; } = 2;
}

public class MapPolyline
{
    public List<(double Latitude, double Longitude)> Positions { get; set; } = [];
    public Color? StrokeColor { get; set; }
    public float StrokeWidth { get; set; } = 2;
}

public class MapPolygon
{
    public List<(double Latitude, double Longitude)> Positions { get; set; } = [];
    public Color? StrokeColor { get; set; }
    public Color? FillColor { get; set; }
    public float StrokeWidth { get; set; } = 2;
}

/// <summary>
/// A MapView control for macOS that displays an MKMapView.
/// </summary>
public class MapView : View
{
    public static readonly BindableProperty LatitudeProperty =
        BindableProperty.Create(nameof(Latitude), typeof(double), typeof(MapView), 47.6062);

    public static readonly BindableProperty LongitudeProperty =
        BindableProperty.Create(nameof(Longitude), typeof(double), typeof(MapView), -122.3321);

    public static readonly BindableProperty LatitudeDeltaProperty =
        BindableProperty.Create(nameof(LatitudeDelta), typeof(double), typeof(MapView), 0.05);

    public static readonly BindableProperty LongitudeDeltaProperty =
        BindableProperty.Create(nameof(LongitudeDelta), typeof(double), typeof(MapView), 0.05);

    public static readonly BindableProperty MapTypeProperty =
        BindableProperty.Create(nameof(MapType), typeof(MapType), typeof(MapView), MapType.Standard);

    public static readonly BindableProperty IsShowingUserProperty =
        BindableProperty.Create(nameof(IsShowingUser), typeof(bool), typeof(MapView), false);

    public static readonly BindableProperty IsScrollEnabledProperty =
        BindableProperty.Create(nameof(IsScrollEnabled), typeof(bool), typeof(MapView), true);

    public static readonly BindableProperty IsZoomEnabledProperty =
        BindableProperty.Create(nameof(IsZoomEnabled), typeof(bool), typeof(MapView), true);

    public ObservableCollection<MapPin> Pins { get; } = [];
    public ObservableCollection<MapCircle> Circles { get; } = [];
    public ObservableCollection<MapPolyline> Polylines { get; } = [];
    public ObservableCollection<MapPolygon> Polygons { get; } = [];

    public double Latitude
    {
        get => (double)GetValue(LatitudeProperty);
        set => SetValue(LatitudeProperty, value);
    }

    public double Longitude
    {
        get => (double)GetValue(LongitudeProperty);
        set => SetValue(LongitudeProperty, value);
    }

    public double LatitudeDelta
    {
        get => (double)GetValue(LatitudeDeltaProperty);
        set => SetValue(LatitudeDeltaProperty, value);
    }

    public double LongitudeDelta
    {
        get => (double)GetValue(LongitudeDeltaProperty);
        set => SetValue(LongitudeDeltaProperty, value);
    }

    public MapType MapType
    {
        get => (MapType)GetValue(MapTypeProperty);
        set => SetValue(MapTypeProperty, value);
    }

    public bool IsShowingUser
    {
        get => (bool)GetValue(IsShowingUserProperty);
        set => SetValue(IsShowingUserProperty, value);
    }

    public bool IsScrollEnabled
    {
        get => (bool)GetValue(IsScrollEnabledProperty);
        set => SetValue(IsScrollEnabledProperty, value);
    }

    public bool IsZoomEnabled
    {
        get => (bool)GetValue(IsZoomEnabledProperty);
        set => SetValue(IsZoomEnabledProperty, value);
    }
}
