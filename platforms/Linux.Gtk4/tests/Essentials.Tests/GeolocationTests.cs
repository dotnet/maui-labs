using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

namespace Essentials.Tests;

public sealed class GeolocationTests
{
	[Fact]
	public async Task Listening_ReportsLocationsAndStopsCleanly()
	{
		var provider = new TestProvider(
			new Location(47.6205, -122.3493),
			new Location(47.6206, -122.3494));
		var geolocation = new LinuxGeolocation(provider);
		var received = new TaskCompletionSource<Location>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		geolocation.LocationChanged += (_, args) => received.TrySetResult(args.Location);

		Assert.True(await geolocation.StartListeningForegroundAsync(
			new GeolocationListeningRequest(GeolocationAccuracy.Medium, TimeSpan.FromMilliseconds(1))));
		var location = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
		geolocation.StopListeningForeground();

		Assert.Equal(47.6205, location.Latitude);
		Assert.False(geolocation.IsListeningForeground);
		Assert.Same(location, await geolocation.GetLastKnownLocationAsync());
	}

	[Fact]
	public async Task Listening_ProviderFailureRaisesFailureAndStops()
	{
		var geolocation = new LinuxGeolocation(new TestProvider(
			new InvalidOperationException("GeoClue unavailable")));
		var failed = new TaskCompletionSource<GeolocationError>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		geolocation.ListeningFailed += (_, args) => failed.TrySetResult(args.Error);

		await geolocation.StartListeningForegroundAsync(
			new GeolocationListeningRequest(GeolocationAccuracy.Medium));
		var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));

		Assert.Equal(GeolocationError.PositionUnavailable, error);
		Assert.False(geolocation.IsListeningForeground);
	}

	[Fact]
	public async Task Listening_RejectsConcurrentSubscription()
	{
		var geolocation = new LinuxGeolocation(new TestProvider(new Location(1, 2)));
		await geolocation.StartListeningForegroundAsync(
			new GeolocationListeningRequest(GeolocationAccuracy.Medium, TimeSpan.FromMinutes(1)));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			geolocation.StartListeningForegroundAsync(
				new GeolocationListeningRequest(GeolocationAccuracy.Medium)));
		geolocation.StopListeningForeground();
	}

	[Fact]
	public void GeoClueOutput_ParsesDegreeSuffixAndInvariantCoordinates()
	{
		var location = GeoClueCommandProvider.ParseOutput(
			"""

			New location:
			Latitude:    47.620500°
			Longitude:   -122.349300°
			Accuracy:    12 meters
			Altitude:    18.500000 meters
			""");

		Assert.Equal(47.6205, location.Latitude);
		Assert.Equal(-122.3493, location.Longitude);
		Assert.Equal(12, location.Accuracy);
		Assert.Equal(18.5, location.Altitude);
	}

	[Fact]
	public async Task GeoClue_ReturnsAfterFirstCompleteFixWithoutWaitingForUtilityTimeout()
	{
		if (OperatingSystem.IsWindows())
			return; // The GTK backend and its command probe run on Unix hosts.

		var script = Path.Combine(
			Path.GetTempPath(),
			$"geoclue-test-{Guid.NewGuid():N}.sh");
		await File.WriteAllTextAsync(
			script,
			"""
			#!/bin/sh
			printf '\nNew location:\nLatitude:    47.620500°\nLongitude:   -122.349300°\nAccuracy:    12 meters\n'
			sleep 30
			""");
		File.SetUnixFileMode(
			script,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		try
		{
			var provider = new GeoClueCommandProvider(script, new GeoClueCommandRunner());
			var stopwatch = Stopwatch.StartNew();

			var location = await provider.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)),
				CancellationToken.None);

			Assert.Equal(47.6205, location.Latitude);
			Assert.True(
				stopwatch.Elapsed < TimeSpan.FromSeconds(3),
				$"GeoClue waited {stopwatch.Elapsed} instead of returning after the first fix.");
		}
		finally
		{
			File.Delete(script);
		}
	}

	[Fact]
	public async Task GeoClue_HonorsTimeoutAndPreservesPermissionFailure()
	{
		var timeoutProvider = new GeoClueCommandProvider(
			GetExistingCommandPath(),
			new DelayedCommandRunner());
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			timeoutProvider.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromMilliseconds(25)),
				CancellationToken.None));

		var deniedProvider = new GeoClueCommandProvider(
			GetExistingCommandPath(),
			new ResultCommandRunner(new GeoClueCommandResult(
				1,
				"",
				"Failed to connect to GeoClue2 service: Permission denied",
				false)));
		var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			deniedProvider.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium),
				CancellationToken.None));
		Assert.Contains("Permission denied", error.Message);
	}

	[Fact]
	public async Task GeoClue_MissingUtilityIsExplicit()
	{
		var provider = new GeoClueCommandProvider(
			Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
			new ResultCommandRunner(default));

		Assert.False(provider.IsAvailable);
		await Assert.ThrowsAsync<FeatureNotSupportedException>(() =>
			provider.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium),
				CancellationToken.None));
	}

	[Fact]
	public async Task StopDuringProviderRequest_IsCancellationNotFailure()
	{
		var provider = new CancellationProvider();
		var geolocation = new LinuxGeolocation(provider);
		var failed = false;
		geolocation.ListeningFailed += (_, _) => failed = true;

		await geolocation.StartListeningForegroundAsync(
			new GeolocationListeningRequest(GeolocationAccuracy.Medium));
		await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
		geolocation.StopListeningForeground();
		await provider.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
		await Task.Delay(50);

		Assert.False(failed);
		Assert.False(geolocation.IsListeningForeground);
	}

	[Fact]
	public async Task StopBeforeProviderCompletion_DropsStaleLocationAndFailure()
	{
		var provider = new DeferredProvider();
		var geolocation = new LinuxGeolocation(provider);
		var locations = 0;
		var failures = 0;
		geolocation.LocationChanged += (_, _) => locations++;
		geolocation.ListeningFailed += (_, _) => failures++;

		await geolocation.StartListeningForegroundAsync(
			new GeolocationListeningRequest(GeolocationAccuracy.Medium));
		await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
		geolocation.StopListeningForeground();
		provider.Release.TrySetResult(new Location(47.6205, -122.3493));
		await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
		await Task.Delay(50);

		Assert.Equal(0, locations);
		Assert.Equal(0, failures);
	}

	[Fact]
	public async Task ListeningCancellation_CancelAndDisposeAreRaceSafe()
	{
		for (var iteration = 0; iteration < 100; iteration++)
		{
			using var cancellation = new ListeningCancellation();
			var errors = new ConcurrentQueue<Exception>();
			using var start = new ManualResetEventSlim();
			var cancel = Task.Run(() => Run(() => cancellation.Cancel()));
			var dispose = Task.Run(() => Run(cancellation.Dispose));
			start.Set();
			await Task.WhenAll(cancel, dispose);

			Assert.Empty(errors);

			void Run(Action action)
			{
				start.Wait();
				try
				{
					action();
				}
				catch (Exception ex)
				{
					errors.Enqueue(ex);
				}
			}
		}
	}

	static string GetExistingCommandPath() =>
		OperatingSystem.IsWindows()
			? Environment.ProcessPath!
			: "/bin/sh";

	sealed class TestProvider : ILinuxGeolocationProvider
	{
		readonly Queue<object> _results;

		public TestProvider(params object[] results) =>
			_results = new Queue<object>(results);

		public bool IsAvailable => true;

		public Task<Location> GetLocationAsync(
			GeolocationRequest request,
			CancellationToken cancellationToken)
		{
			if (_results.Count == 0)
				return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
					.ContinueWith<Location>(_ => throw new OperationCanceledException(cancellationToken),
						cancellationToken);

			var result = _results.Dequeue();
			return result is Exception exception
				? Task.FromException<Location>(exception)
				: Task.FromResult((Location)result);
		}
	}

	sealed class ResultCommandRunner(GeoClueCommandResult result) : IGeoClueCommandRunner
	{
		public Task<GeoClueCommandResult> RunAsync(
			string commandPath,
			CancellationToken cancellationToken) =>
			Task.FromResult(result);
	}

	sealed class DelayedCommandRunner : IGeoClueCommandRunner
	{
		public async Task<GeoClueCommandResult> RunAsync(
			string commandPath,
			CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			throw new InvalidOperationException("The timeout wait unexpectedly completed.");
		}
	}

	sealed class CancellationProvider : ILinuxGeolocationProvider
	{
		public bool IsAvailable => true;
		public TaskCompletionSource Started { get; } = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Cancelled { get; } = new(
			TaskCreationOptions.RunContinuationsAsynchronously);

		public async Task<Location> GetLocationAsync(
			GeolocationRequest request,
			CancellationToken cancellationToken)
		{
			Started.TrySetResult();
			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				throw new InvalidOperationException("The cancellation wait unexpectedly completed.");
			}
			catch (OperationCanceledException)
			{
				await Task.Yield();
				Assert.True(cancellationToken.IsCancellationRequested);
				Cancelled.TrySetResult();
				throw;
			}
		}
	}

	sealed class DeferredProvider : ILinuxGeolocationProvider
	{
		public bool IsAvailable => true;
		public TaskCompletionSource Started { get; } = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource<Location> Release { get; } = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Returned { get; } = new(
			TaskCreationOptions.RunContinuationsAsynchronously);

		public async Task<Location> GetLocationAsync(
			GeolocationRequest request,
			CancellationToken cancellationToken)
		{
			Started.TrySetResult();
			var location = await Release.Task;
			Returned.TrySetResult();
			return location;
		}
	}
}
