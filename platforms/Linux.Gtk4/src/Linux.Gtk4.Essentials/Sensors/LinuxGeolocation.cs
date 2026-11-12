using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public sealed class LinuxGeolocation : IGeolocation
{
	readonly ILinuxGeolocationProvider _provider;
	readonly object _listeningLock = new();
	ListeningCancellation? _listeningCancellation;
	Location? _lastLocation;

	public LinuxGeolocation()
		: this(new GeoClueCommandProvider())
	{
	}

	internal LinuxGeolocation(ILinuxGeolocationProvider provider) =>
		_provider = provider;

	public bool IsListeningForeground
	{
		get
		{
			lock (_listeningLock)
				return _listeningCancellation is not null;
		}
	}

	public bool IsEnabled => _provider.IsAvailable;

	public Task<Location?> GetLastKnownLocationAsync() =>
		Task.FromResult(_lastLocation);

	public async Task<Location?> GetLocationAsync(
		GeolocationRequest request,
		CancellationToken cancelToken)
	{
		var location = await _provider.GetLocationAsync(request, cancelToken);
		_lastLocation = location;
		return location;
	}

	public Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request)
	{
		if (!IsEnabled)
			throw new FeatureNotSupportedException(
				"Continuous Linux geolocation requires GeoClue's 'where-am-i' utility.");

		ListeningCancellation cancellation;
		lock (_listeningLock)
		{
			if (_listeningCancellation is not null)
				throw new InvalidOperationException("Already listening for location updates.");

			_listeningCancellation = cancellation = new ListeningCancellation();
		}

		_ = ListenAsync(request, cancellation);
		return Task.FromResult(true);
	}

	public void StopListeningForeground()
	{
		ListeningCancellation? cancellation;
		lock (_listeningLock)
		{
			cancellation = _listeningCancellation;
			_listeningCancellation = null;
		}
		cancellation?.Cancel();
	}

	public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged;
	public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed;

	async Task ListenAsync(
		GeolocationListeningRequest request,
		ListeningCancellation cancellation)
	{
		Location? lastReported = null;
		var cancellationToken = cancellation.Token;
		var minimumDistance =
#if NET11_0_OR_GREATER
			request.MinimumDistance;
#else
			0;
#endif
		var delay = request.MinimumTime > TimeSpan.Zero
			? request.MinimumTime
			: TimeSpan.FromSeconds(1);

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var location = await _provider.GetLocationAsync(
					new GeolocationRequest(request.DesiredAccuracy),
					cancellationToken).ConfigureAwait(false);
				var shouldReport = lastReported is null ||
					minimumDistance <= 0 ||
					Location.CalculateDistance(lastReported, location, DistanceUnits.Kilometers) * 1000 >= minimumDistance;
				if (!cancellation.TryRunIfActive(() =>
				{
					_lastLocation = location;
					if (shouldReport)
					{
						lastReported = location;
						LocationChanged?.Invoke(this, new GeolocationLocationChangedEventArgs(location));
					}
				}))
					break;

				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch
		{
			var shouldReport = false;
			lock (_listeningLock)
			{
				if (ReferenceEquals(_listeningCancellation, cancellation))
				{
					_listeningCancellation = null;
					shouldReport = true;
				}
			}

			if (shouldReport)
			{
				cancellation.TryRunIfActive(() =>
					ListeningFailed?.Invoke(this,
						new GeolocationListeningFailedEventArgs(GeolocationError.PositionUnavailable)));
			}
		}
		finally
		{
			lock (_listeningLock)
			{
				if (ReferenceEquals(_listeningCancellation, cancellation))
					_listeningCancellation = null;
			}

			cancellation.Dispose();
		}
	}
}

internal sealed class ListeningCancellation : IDisposable
{
	readonly object _gate = new();
	CancellationTokenSource? _source = new();
	bool _active = true;

	public CancellationToken Token
	{
		get
		{
			lock (_gate)
				return _source?.Token ?? new CancellationToken(canceled: true);
		}
	}

	public void Cancel()
	{
		lock (_gate)
		{
			_active = false;
			_source?.Cancel();
		}
	}

	public bool TryRunIfActive(Action action)
	{
		lock (_gate)
		{
			if (!_active || _source is null)
				return false;

			action();
			return true;
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_active = false;
			_source?.Dispose();
			_source = null;
		}
	}
}

internal interface ILinuxGeolocationProvider
{
	bool IsAvailable { get; }
	Task<Location> GetLocationAsync(GeolocationRequest request, CancellationToken cancellationToken);
}

internal sealed class GeoClueCommandProvider : ILinuxGeolocationProvider
{
	static readonly string[] CommandCandidates =
	[
		"/usr/bin/where-am-i",
		"/usr/local/bin/where-am-i",
		"/usr/libexec/geoclue-2.0/demos/where-am-i",
		"/usr/lib/geoclue-2.0/demos/where-am-i",
	];

	readonly string? _commandPath;
	readonly IGeoClueCommandRunner _commandRunner;

	public GeoClueCommandProvider()
		: this(null, new GeoClueCommandRunner())
	{
	}

	internal GeoClueCommandProvider(
		string? commandPath,
		IGeoClueCommandRunner commandRunner)
	{
		_commandPath = commandPath;
		_commandRunner = commandRunner;
	}

	public bool IsAvailable => ResolveCommandPath() is not null;

	public async Task<Location> GetLocationAsync(
		GeolocationRequest request,
		CancellationToken cancellationToken)
	{
		var commandPath = ResolveCommandPath();
		if (commandPath is null)
			throw new FeatureNotSupportedException(
				"Linux geolocation requires GeoClue's 'where-am-i' utility.");

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (request.Timeout > TimeSpan.Zero &&
			request.Timeout != Timeout.InfiniteTimeSpan)
		{
			timeout.CancelAfter(request.Timeout);
		}

		var result = await _commandRunner.RunAsync(commandPath, timeout.Token)
			.ConfigureAwait(false);
		if (!result.CompleteFix && result.ExitCode != 0)
			throw new InvalidOperationException(
				$"GeoClue failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");

		return ParseOutput(result.StandardOutput);
	}

	internal static Location ParseOutput(string output)
	{
		double? latitude = null;
		double? longitude = null;
		double? accuracy = null;
		double? altitude = null;
		foreach (var line in output.Split('\n'))
		{
			var trimmed = line.Trim();
			if (trimmed.StartsWith("Latitude:", StringComparison.OrdinalIgnoreCase))
				latitude = ParseCoordinate(trimmed);
			else if (trimmed.StartsWith("Longitude:", StringComparison.OrdinalIgnoreCase))
				longitude = ParseCoordinate(trimmed);
			else if (trimmed.StartsWith("Accuracy:", StringComparison.OrdinalIgnoreCase))
				accuracy = ParseCoordinate(trimmed);
			else if (trimmed.StartsWith("Altitude:", StringComparison.OrdinalIgnoreCase))
				altitude = ParseCoordinate(trimmed);
		}

		if (!latitude.HasValue || !longitude.HasValue)
			throw new InvalidDataException("GeoClue returned no parseable latitude and longitude.");

		return new Location(latitude.Value, longitude.Value)
		{
			Accuracy = accuracy,
			Altitude = altitude,
			Timestamp = DateTimeOffset.UtcNow,
		};
	}

	internal static Location? ParseOutputIfComplete(string output)
	{
		try
		{
			return ParseOutput(output);
		}
		catch (InvalidDataException)
		{
			return null;
		}
	}

	internal static double? ParseCoordinate(string line)
	{
		var separator = line.IndexOf(':');
		if (separator < 0)
			return null;

		var value = line[(separator + 1)..].Trim();
		var firstSpace = value.IndexOf(' ');
		if (firstSpace >= 0)
			value = value[..firstSpace];
		value = value.TrimEnd('\u00b0');

		return double.TryParse(
			value,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var parsed)
			? parsed
			: null;
	}

	string? ResolveCommandPath()
	{
		if (_commandPath is not null)
			return File.Exists(_commandPath) ? _commandPath : null;

		return CommandCandidates.FirstOrDefault(File.Exists);
	}
}

internal interface IGeoClueCommandRunner
{
	Task<GeoClueCommandResult> RunAsync(
		string commandPath,
		CancellationToken cancellationToken);
}

internal sealed class GeoClueCommandRunner : IGeoClueCommandRunner
{
	public async Task<GeoClueCommandResult> RunAsync(
		string commandPath,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo(commandPath)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		startInfo.Environment["LC_ALL"] = "C";
		startInfo.Environment["LANG"] = "C";

		using var process = Process.Start(startInfo)
			?? throw new InvalidOperationException("GeoClue's 'where-am-i' utility could not be started.");
		using var registration = cancellationToken.Register(() => KillProcess(process));
		var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
		var output = new StringBuilder();
		var completeFix = false;

		try
		{
			while (await process.StandardOutput.ReadLineAsync(cancellationToken)
				.ConfigureAwait(false) is { } line)
			{
				output.AppendLine(line);
				if (line.TrimStart().StartsWith("Accuracy:", StringComparison.OrdinalIgnoreCase) &&
					GeoClueCommandProvider.ParseOutputIfComplete(output.ToString()) is not null)
				{
					completeFix = true;
					KillProcess(process);
					break;
				}
			}

			await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
			return new GeoClueCommandResult(
				process.ExitCode,
				output.ToString(),
				await errorTask.ConfigureAwait(false),
				completeFix);
		}
		catch
		{
			KillProcess(process);
			throw;
		}
	}

	static void KillProcess(Process process)
	{
		try
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
		catch (InvalidOperationException)
		{
		}
	}
}

internal readonly record struct GeoClueCommandResult(
	int ExitCode,
	string StandardOutput,
	string StandardError,
	bool CompleteFix);
