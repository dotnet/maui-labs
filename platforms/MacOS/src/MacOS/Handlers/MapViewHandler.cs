using System.Collections.Specialized;
using CoreLocation;
using MapKit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.MacOS.Controls;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class MapViewHandler : MacOSViewHandler<MapView, MKMapView>
{
    public static readonly IPropertyMapper<MapView, MapViewHandler> Mapper =
        new PropertyMapper<MapView, MapViewHandler>(ViewMapper)
        {
            [nameof(MapView.Latitude)] = MapRegion,
            [nameof(MapView.Longitude)] = MapRegion,
            [nameof(MapView.LatitudeDelta)] = MapRegion,
            [nameof(MapView.LongitudeDelta)] = MapRegion,
            [nameof(MapView.MapType)] = MapMapType,
            [nameof(MapView.IsShowingUser)] = MapIsShowingUser,
            [nameof(MapView.IsScrollEnabled)] = MapIsScrollEnabled,
            [nameof(MapView.IsZoomEnabled)] = MapIsZoomEnabled,
        };

    MapViewDelegate? _mapDelegate;

    public MapViewHandler() : base(Mapper)
    {
    }

    protected override MKMapView CreatePlatformView()
    {
        return new MKMapView { WantsLayer = true };
    }

    protected override void ConnectHandler(MKMapView platformView)
    {
        base.ConnectHandler(platformView);

        _mapDelegate = new MapViewDelegate(VirtualView);
        platformView.Delegate = _mapDelegate;

        VirtualView.Pins.CollectionChanged += OnPinsChanged;
        VirtualView.Circles.CollectionChanged += OnOverlaysChanged;
        VirtualView.Polylines.CollectionChanged += OnOverlaysChanged;
        VirtualView.Polygons.CollectionChanged += OnOverlaysChanged;

        UpdateRegion();
        SyncPins();
        SyncOverlays();
    }

    protected override void DisconnectHandler(MKMapView platformView)
    {
        VirtualView.Pins.CollectionChanged -= OnPinsChanged;
        VirtualView.Circles.CollectionChanged -= OnOverlaysChanged;
        VirtualView.Polylines.CollectionChanged -= OnOverlaysChanged;
        VirtualView.Polygons.CollectionChanged -= OnOverlaysChanged;

        platformView.Delegate = null;
        _mapDelegate = null;
        base.DisconnectHandler(platformView);
    }

    void OnPinsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncPins();
    void OnOverlaysChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncOverlays();

    public static void MapRegion(MapViewHandler handler, MapView mapView) => handler.UpdateRegion();

    void UpdateRegion()
    {
        var center = new CLLocationCoordinate2D(VirtualView.Latitude, VirtualView.Longitude);
        var span = new MKCoordinateSpan(VirtualView.LatitudeDelta, VirtualView.LongitudeDelta);
        var region = new MKCoordinateRegion(center, span);
        PlatformView.SetRegion(region, animated: true);
    }

    void SyncPins()
    {
        if (PlatformView.Annotations?.Length > 0)
            PlatformView.RemoveAnnotations(PlatformView.Annotations);
        foreach (var pin in VirtualView.Pins)
        {
            var annotation = new MKPointAnnotation
            {
                Coordinate = new CLLocationCoordinate2D(pin.Latitude, pin.Longitude),
                Title = pin.Label,
                Subtitle = pin.Address,
            };
            PlatformView.AddAnnotation(annotation);
        }
    }

    void SyncOverlays()
    {
        if (PlatformView.Overlays?.Length > 0)
            PlatformView.RemoveOverlays(PlatformView.Overlays);

        foreach (var circle in VirtualView.Circles)
        {
            var overlay = MKCircle.Circle(
                new CLLocationCoordinate2D(circle.CenterLatitude, circle.CenterLongitude),
                circle.Radius);
            PlatformView.AddOverlay(overlay);
        }

        foreach (var polyline in VirtualView.Polylines)
        {
            var coords = polyline.Positions
                .Select(p => new CLLocationCoordinate2D(p.Latitude, p.Longitude))
                .ToArray();
            PlatformView.AddOverlay(MKPolyline.FromCoordinates(coords));
        }

        foreach (var polygon in VirtualView.Polygons)
        {
            var coords = polygon.Positions
                .Select(p => new CLLocationCoordinate2D(p.Latitude, p.Longitude))
                .ToArray();
            PlatformView.AddOverlay(MKPolygon.FromCoordinates(coords));
        }
    }

    public static void MapMapType(MapViewHandler handler, MapView mapView)
    {
        handler.PlatformView.MapType = mapView.MapType switch
        {
            Controls.MapType.Satellite => MKMapType.Satellite,
            Controls.MapType.Hybrid => MKMapType.Hybrid,
            _ => MKMapType.Standard,
        };
    }

    public static void MapIsShowingUser(MapViewHandler handler, MapView mapView)
        => handler.PlatformView.ShowsUserLocation = mapView.IsShowingUser;

    public static void MapIsScrollEnabled(MapViewHandler handler, MapView mapView)
        => handler.PlatformView.ScrollEnabled = mapView.IsScrollEnabled;

    public static void MapIsZoomEnabled(MapViewHandler handler, MapView mapView)
        => handler.PlatformView.ZoomEnabled = mapView.IsZoomEnabled;

    public override Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var width = double.IsPositiveInfinity(widthConstraint) ? 400 : widthConstraint;
        var height = double.IsPositiveInfinity(heightConstraint) ? 400 : heightConstraint;
        return new Graphics.Size(width, height);
    }

    class MapViewDelegate : MKMapViewDelegate
    {
        readonly MapView _mapView;
        public MapViewDelegate(MapView mapView) => _mapView = mapView;

        public override MKOverlayRenderer OverlayRenderer(MKMapView mapView, IMKOverlay overlay)
        {
            if (overlay is MKCircle circle)
            {
                var source = _mapView.Circles.FirstOrDefault(c =>
                    Math.Abs(c.CenterLatitude - circle.Coordinate.Latitude) < 0.0001 &&
                    Math.Abs(c.CenterLongitude - circle.Coordinate.Longitude) < 0.0001);

                var renderer = new MKCircleRenderer(circle)
                {
                    LineWidth = source?.StrokeWidth ?? 2,
                };
                if (source?.StrokeColor != null)
                    renderer.StrokeColor = source.StrokeColor.ToPlatformColor();
                if (source?.FillColor != null)
                    renderer.FillColor = source.FillColor.ToPlatformColor();
                return renderer;
            }

            if (overlay is MKPolyline polyline)
            {
                var idx = GetOverlayIndex<MKPolyline>(mapView, polyline);
                var source = idx < _mapView.Polylines.Count ? _mapView.Polylines[idx] : null;

                var renderer = new MKPolylineRenderer(polyline)
                {
                    LineWidth = source?.StrokeWidth ?? 2,
                };
                if (source?.StrokeColor != null)
                    renderer.StrokeColor = source.StrokeColor.ToPlatformColor();
                return renderer;
            }

            if (overlay is MKPolygon polygon)
            {
                var idx = GetOverlayIndex<MKPolygon>(mapView, polygon);
                var source = idx < _mapView.Polygons.Count ? _mapView.Polygons[idx] : null;

                var renderer = new MKPolygonRenderer(polygon)
                {
                    LineWidth = source?.StrokeWidth ?? 2,
                };
                if (source?.StrokeColor != null)
                    renderer.StrokeColor = source.StrokeColor.ToPlatformColor();
                if (source?.FillColor != null)
                    renderer.FillColor = source.FillColor.ToPlatformColor();
                return renderer;
            }

            return new MKOverlayRenderer(overlay);
        }

        static int GetOverlayIndex<T>(MKMapView mapView, T target) where T : class, IMKOverlay
        {
            int idx = 0;
            foreach (var o in mapView.Overlays)
            {
                if (o == target) return idx;
                if (o is T) idx++;
            }
            return 0;
        }
    }
}
