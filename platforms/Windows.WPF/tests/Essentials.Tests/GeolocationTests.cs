using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;
using Windows.Devices.Geolocation;

namespace Essentials.Tests;

public sealed class GeolocationTests
{
	[Fact]
	public async Task OneShotLocation_IsCachedAsLastKnownLocation()
	{
		var expected = new Location(47.6205, -122.3493);
		var geolocation = new WPFGeolocation(new TestProvider(expected));

		var actual = await geolocation.GetLocationAsync(
			new GeolocationRequest(GeolocationAccuracy.Medium));

		Assert.Same(expected, actual);
		Assert.Same(expected, await geolocation.GetLastKnownLocationAsync());
	}

	[Fact]
	public async Task Listening_ForwardsLocationAndFailureEvents()
	{
		var provider = new TestProvider(new Location(1, 2));
		var geolocation = new WPFGeolocation(provider);
		Location? received = null;
		GeolocationError? failure = null;
		geolocation.LocationChanged += (_, args) => received = args.Location;
		geolocation.ListeningFailed += (_, args) => failure = args.Error;

		Assert.True(await geolocation.StartListeningForegroundAsync(
			new GeolocationListeningRequest(GeolocationAccuracy.High)));
		provider.RaiseLocation(new Location(3, 4));
		provider.RaiseFailure(GeolocationError.PositionUnavailable);

		Assert.Equal(3, received?.Latitude);
		Assert.Equal(GeolocationError.PositionUnavailable, failure);
		geolocation.StopListeningForeground();
		Assert.False(geolocation.IsListeningForeground);
	}

	[Theory]
	[InlineData(PositionStatus.Ready, true)]
	[InlineData(PositionStatus.Initializing, true)]
	[InlineData(PositionStatus.NoData, true)]
	[InlineData(PositionStatus.Disabled, false)]
	[InlineData(PositionStatus.NotAvailable, false)]
	public void NativeStatus_DeterminesWhetherLocationIsEnabled(
		PositionStatus status,
		bool expected)
	{
		var provider = new WindowsGeolocationProvider(
			new TestRuntime(status, GeolocationAccessStatus.Allowed));

		Assert.Equal(expected, provider.IsEnabled);
	}

	[Theory]
	[InlineData(PositionStatus.Disabled, GeolocationError.Unauthorized)]
	[InlineData(PositionStatus.NoData, GeolocationError.PositionUnavailable)]
	[InlineData(PositionStatus.NotAvailable, GeolocationError.PositionUnavailable)]
	public void NativeStatus_MapsListenerFailures(
		PositionStatus status,
		GeolocationError expected)
	{
		Assert.Equal(expected, WindowsGeolocationProvider.GetListeningError(status));
	}

	[Fact]
	public async Task OneShotLocation_DisabledAndDeniedAreExplicit()
	{
		var disabled = new WindowsGeolocationProvider(
			new TestRuntime(PositionStatus.Disabled, GeolocationAccessStatus.Allowed));
		await Assert.ThrowsAsync<FeatureNotEnabledException>(() =>
			disabled.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium),
				CancellationToken.None));

		var denied = new WindowsGeolocationProvider(
			new TestRuntime(PositionStatus.Ready, GeolocationAccessStatus.Denied));
		await Assert.ThrowsAsync<PermissionException>(() =>
			denied.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium),
				CancellationToken.None));
	}

	[Fact]
	public async Task OneShotLocation_HonorsRequestTimeoutAndCallerCancellation()
	{
		var timeoutRuntime = new TestRuntime(
			PositionStatus.Ready,
			GeolocationAccessStatus.Allowed,
			waitForCancellation: true);
		var timeoutProvider = new WindowsGeolocationProvider(timeoutRuntime);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			timeoutProvider.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromMilliseconds(25)),
				CancellationToken.None));
		Assert.True(timeoutRuntime.ObservedCancellation);

		var cancellationRuntime = new TestRuntime(
			PositionStatus.Ready,
			GeolocationAccessStatus.Allowed,
			waitForCancellation: true);
		var cancellationProvider = new WindowsGeolocationProvider(cancellationRuntime);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			cancellationProvider.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromMinutes(1)),
				cancellation.Token));
		Assert.True(cancellationRuntime.ObservedCancellation);
	}

	[Fact]
	public void ListenerStatus_MapsNoDataAndPermissionFailures()
	{
		Assert.Equal(
			GeolocationError.PositionUnavailable,
			WindowsGeolocationProvider.GetListeningError(PositionStatus.NoData));
		Assert.Equal(
			GeolocationError.Unauthorized,
			WindowsGeolocationProvider.GetListeningError(PositionStatus.Disabled));
		Assert.Null(WindowsGeolocationProvider.GetListeningError(PositionStatus.Ready));
	}

	sealed class TestProvider(Location location) : IWpfGeolocationProvider
	{
		public bool IsEnabled => true;
		public bool IsListening { get; private set; }

		public event EventHandler<Location>? LocationChanged;
		public event EventHandler<GeolocationError>? ListeningFailed;

		public Task<Location> GetLocationAsync(
			GeolocationRequest request,
			CancellationToken cancellationToken) =>
			Task.FromResult(location);

		public Task<bool> StartListeningAsync(GeolocationListeningRequest request)
		{
			IsListening = true;
			return Task.FromResult(true);
		}

		public void StopListening() => IsListening = false;

		public void RaiseLocation(Location value) =>
			LocationChanged?.Invoke(this, value);

		public void RaiseFailure(GeolocationError error) =>
			ListeningFailed?.Invoke(this, error);
	}

	sealed class TestRuntime(
		PositionStatus status,
		GeolocationAccessStatus access,
		bool waitForCancellation = false) : IWindowsGeolocationRuntime
	{
		public PositionStatus LocationStatus => status;
		public bool ObservedCancellation { get; private set; }

		public Task<GeolocationAccessStatus> RequestAccessAsync() =>
			Task.FromResult(access);

		public async Task<Location> GetLocationAsync(
			GeolocationAccuracy accuracy,
			CancellationToken cancellationToken)
		{
			if (!waitForCancellation)
				return new Location(47.6205, -122.3493);

			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				throw new InvalidOperationException("The cancellation wait unexpectedly completed.");
			}
			catch (OperationCanceledException)
			{
				ObservedCancellation = true;
				throw;
			}
		}
	}
}
