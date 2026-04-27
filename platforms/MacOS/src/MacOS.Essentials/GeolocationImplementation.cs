using CoreLocation;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class GeolocationImplementation : IGeolocation
{
	CLLocationManager? _listeningManager;

	public bool IsListeningForeground => _listeningManager != null;

	public bool IsEnabled => CLLocationManager.LocationServicesEnabled;

	public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged;
	public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed;

	public Task<Location?> GetLastKnownLocationAsync()
	{
		using var manager = new CLLocationManager();
		var clLocation = manager.Location;
		if (clLocation == null)
			return Task.FromResult<Location?>(null);

		return Task.FromResult<Location?>(ToLocation(clLocation));
	}

	public async Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken)
	{
		var tcs = new TaskCompletionSource<Location?>();
		var manager = new CLLocationManager
		{
			DesiredAccuracy = ToDesiredAccuracy(request.DesiredAccuracy),
		};

		var del = new SingleLocationDelegate(tcs);
		manager.Delegate = del;

		var registration = cancelToken.Register(() =>
		{
			manager.StopUpdatingLocation();
			tcs.TrySetResult(null);
		});

		try
		{
			manager.StartUpdatingLocation();
			return await tcs.Task;
		}
		finally
		{
			registration.Dispose();
			manager.StopUpdatingLocation();
			manager.Delegate = null;
			manager.Dispose();
		}
	}

	public Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request)
	{
		if (_listeningManager != null)
			throw new InvalidOperationException("Already listening for location updates.");

		_listeningManager = new CLLocationManager();
		_listeningManager.DesiredAccuracy = ToDesiredAccuracy(request.DesiredAccuracy);

		var del = new ContinuousLocationDelegate(this);
		_listeningManager.Delegate = del;
		_listeningManager.StartUpdatingLocation();

		return Task.FromResult(true);
	}

	public void StopListeningForeground()
	{
		var manager = _listeningManager;
		_listeningManager = null;
		if (manager == null)
			return;

		manager.StopUpdatingLocation();
		manager.Delegate = null;
		manager.Dispose();
	}

	void OnLocationUpdated(CLLocation location)
	{
		LocationChanged?.Invoke(null, new GeolocationLocationChangedEventArgs(ToLocation(location)));
	}

	void OnLocationFailed()
	{
		StopListeningForeground();
		ListeningFailed?.Invoke(null, new GeolocationListeningFailedEventArgs(GeolocationError.PositionUnavailable));
	}

	static Location ToLocation(CLLocation clLocation)
	{
		return new Location(clLocation.Coordinate.Latitude, clLocation.Coordinate.Longitude)
		{
			Accuracy = clLocation.HorizontalAccuracy,
			Altitude = clLocation.Altitude,
			Speed = clLocation.Speed >= 0 ? clLocation.Speed : null,
			Course = clLocation.Course >= 0 ? clLocation.Course : null,
			Timestamp = (DateTime)clLocation.Timestamp,
		};
	}

	static double ToDesiredAccuracy(GeolocationAccuracy accuracy)
	{
		return accuracy switch
		{
			GeolocationAccuracy.Lowest => CLLocation.AccuracyThreeKilometers,
			GeolocationAccuracy.Low => CLLocation.AccuracyKilometer,
			GeolocationAccuracy.Medium => CLLocation.AccuracyHundredMeters,
			GeolocationAccuracy.High => CLLocation.AccuracyNearestTenMeters,
			GeolocationAccuracy.Best => CLLocation.AccuracyBestForNavigation,
			_ => CLLocation.AccuracyHundredMeters,
		};
	}

	class SingleLocationDelegate : CLLocationManagerDelegate
	{
		readonly TaskCompletionSource<Location?> _tcs;

		public SingleLocationDelegate(TaskCompletionSource<Location?> tcs) => _tcs = tcs;

		public override void LocationsUpdated(CLLocationManager manager, CLLocation[] locations)
		{
			if (locations.Length > 0)
				_tcs.TrySetResult(ToLocation(locations[^1]));
		}

		public override void Failed(CLLocationManager manager, Foundation.NSError error)
		{
			_tcs.TrySetResult(null);
		}
	}

	class ContinuousLocationDelegate : CLLocationManagerDelegate
	{
		readonly GeolocationImplementation _impl;

		public ContinuousLocationDelegate(GeolocationImplementation impl) => _impl = impl;

		public override void LocationsUpdated(CLLocationManager manager, CLLocation[] locations)
		{
			if (locations.Length > 0)
				_impl.OnLocationUpdated(locations[^1]);
		}

		public override void Failed(CLLocationManager manager, Foundation.NSError error)
		{
			_impl.OnLocationFailed();
		}
	}
}
