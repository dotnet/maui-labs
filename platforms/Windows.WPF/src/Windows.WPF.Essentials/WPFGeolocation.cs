using Microsoft.Maui.Devices.Sensors;
using Windows.Devices.Geolocation;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials;

public sealed class WPFGeolocation : IGeolocation
{
	readonly IWpfGeolocationProvider _provider;
	Location? _lastLocation;

	public WPFGeolocation()
		: this(new WindowsGeolocationProvider())
	{
	}

	internal WPFGeolocation(IWpfGeolocationProvider provider)
	{
		_provider = provider;
		_provider.LocationChanged += OnLocationChanged;
		_provider.ListeningFailed += OnListeningFailed;
	}

	public bool IsListeningForeground => _provider.IsListening;

	public bool IsEnabled => _provider.IsEnabled;

	public Task<Location?> GetLastKnownLocationAsync() =>
		Task.FromResult(_lastLocation);

	public async Task<Location?> GetLocationAsync(
		GeolocationRequest request,
		CancellationToken cancelToken = default)
	{
		var location = await _provider.GetLocationAsync(request, cancelToken);
		_lastLocation = location;
		return location;
	}

	public Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request) =>
		_provider.StartListeningAsync(request);

	public void StopListeningForeground() =>
		_provider.StopListening();

	public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged;
	public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed;

	void OnLocationChanged(object? sender, Location location)
	{
		_lastLocation = location;
		LocationChanged?.Invoke(this, new GeolocationLocationChangedEventArgs(location));
	}

	void OnListeningFailed(object? sender, GeolocationError error) =>
		ListeningFailed?.Invoke(this, new GeolocationListeningFailedEventArgs(error));
}

internal interface IWpfGeolocationProvider
{
	bool IsEnabled { get; }
	bool IsListening { get; }
	Task<Location> GetLocationAsync(GeolocationRequest request, CancellationToken cancellationToken);
	Task<bool> StartListeningAsync(GeolocationListeningRequest request);
	void StopListening();
	event EventHandler<Location>? LocationChanged;
	event EventHandler<GeolocationError>? ListeningFailed;
}

internal sealed class WindowsGeolocationProvider : IWpfGeolocationProvider
{
	readonly IWindowsGeolocationRuntime _runtime;
	Geolocator? _listener;

	public WindowsGeolocationProvider()
		: this(new WindowsGeolocationRuntime())
	{
	}

	internal WindowsGeolocationProvider(IWindowsGeolocationRuntime runtime) =>
		_runtime = runtime;

	public bool IsEnabled => IsStatusEnabled(_runtime.LocationStatus);
	public bool IsListening => _listener is not null;

	public event EventHandler<Location>? LocationChanged;
	public event EventHandler<GeolocationError>? ListeningFailed;

	public async Task<Location> GetLocationAsync(
		GeolocationRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		await EnsureAccessAsync().ConfigureAwait(false);
		CheckStatus(_runtime.LocationStatus);

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (request.Timeout > TimeSpan.Zero &&
			request.Timeout != Timeout.InfiniteTimeSpan)
		{
			timeout.CancelAfter(request.Timeout);
		}

		return await _runtime.GetLocationAsync(request.DesiredAccuracy, timeout.Token)
			.ConfigureAwait(false);
	}

	public async Task<bool> StartListeningAsync(GeolocationListeningRequest request)
	{
		if (_listener is not null)
			throw new InvalidOperationException("Already listening for location updates.");

		await EnsureAccessAsync().ConfigureAwait(false);
		var locator = CreateLocator(request.DesiredAccuracy);
		CheckStatus(locator.LocationStatus);
#if NET11_0_OR_GREATER
		locator.MovementThreshold = Math.Max(0, request.MinimumDistance);
#else
		locator.MovementThreshold = 0;
#endif
		locator.ReportInterval = (uint)Math.Clamp(
			request.MinimumTime.TotalMilliseconds,
			0,
			uint.MaxValue);
		locator.PositionChanged += OnPositionChanged;
		locator.StatusChanged += OnStatusChanged;
		_listener = locator;
		return true;
	}

	public void StopListening()
	{
		var locator = _listener;
		_listener = null;
		if (locator is null)
			return;

		locator.PositionChanged -= OnPositionChanged;
		locator.StatusChanged -= OnStatusChanged;
	}

	async Task EnsureAccessAsync()
	{
		var access = await _runtime.RequestAccessAsync().ConfigureAwait(false);
		if (access == GeolocationAccessStatus.Allowed)
			return;

		throw access == GeolocationAccessStatus.Denied
			? new PermissionException("Windows location access was denied.")
			: new FeatureNotEnabledException("Windows location services are unavailable.");
	}

	static Geolocator CreateLocator(GeolocationAccuracy accuracy) =>
		new()
		{
			DesiredAccuracy = accuracy is GeolocationAccuracy.High or GeolocationAccuracy.Best
				? PositionAccuracy.High
				: PositionAccuracy.Default,
		};

	void OnPositionChanged(Geolocator sender, global::Windows.Devices.Geolocation.PositionChangedEventArgs args) =>
		LocationChanged?.Invoke(this, ToLocation(args.Position));

	void OnStatusChanged(Geolocator sender, StatusChangedEventArgs args)
	{
		var error = GetListeningError(args.Status);

		if (error.HasValue)
		{
			StopListening();
			ListeningFailed?.Invoke(this, error.Value);
		}
	}

	internal static bool IsStatusEnabled(PositionStatus status) =>
		status is not PositionStatus.Disabled and not PositionStatus.NotAvailable;

	internal static GeolocationError? GetListeningError(PositionStatus status) =>
		status switch
		{
			PositionStatus.Disabled => GeolocationError.Unauthorized,
			PositionStatus.NoData or PositionStatus.NotAvailable =>
				GeolocationError.PositionUnavailable,
			_ => null,
		};

	internal static void CheckStatus(PositionStatus status)
	{
		if (!IsStatusEnabled(status))
			throw new FeatureNotEnabledException("Windows location services are unavailable.");
	}

	internal static Location ToLocation(Geoposition position)
	{
		var coordinate = position.Coordinate;
		var point = coordinate.Point.Position;
		return new Location(point.Latitude, point.Longitude)
		{
			Altitude = point.Altitude,
			Accuracy = coordinate.Accuracy,
			Timestamp = coordinate.Timestamp,
		};
	}
}

internal interface IWindowsGeolocationRuntime
{
	PositionStatus LocationStatus { get; }
	Task<GeolocationAccessStatus> RequestAccessAsync();
	Task<Location> GetLocationAsync(
		GeolocationAccuracy accuracy,
		CancellationToken cancellationToken);
}

internal sealed class WindowsGeolocationRuntime : IWindowsGeolocationRuntime
{
	public PositionStatus LocationStatus => new Geolocator().LocationStatus;

	public Task<GeolocationAccessStatus> RequestAccessAsync() =>
		RunOnDispatcherAsync(async () => await Geolocator.RequestAccessAsync());

	public async Task<Location> GetLocationAsync(
		GeolocationAccuracy accuracy,
		CancellationToken cancellationToken)
	{
		var locator = new Geolocator
		{
			DesiredAccuracy = accuracy is GeolocationAccuracy.High or GeolocationAccuracy.Best
				? PositionAccuracy.High
				: PositionAccuracy.Default,
		};
		WindowsGeolocationProvider.CheckStatus(locator.LocationStatus);
		var position = await locator.GetGeopositionAsync().AsTask(cancellationToken);
		return WindowsGeolocationProvider.ToLocation(position);
	}

	static Task<T> RunOnDispatcherAsync<T>(Func<Task<T>> action)
	{
		var dispatcher = System.Windows.Application.Current?.Dispatcher;
		if (dispatcher is null || dispatcher.CheckAccess())
			return action();

		return dispatcher.InvokeAsync(action).Task.Unwrap();
	}
}
