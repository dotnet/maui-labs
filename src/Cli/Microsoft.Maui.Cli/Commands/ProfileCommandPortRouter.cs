// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileCommandPortRouter
{
	internal static async Task TryForceStopRunningAndroidAppAsync(
		ResolvedMauiProject project,
		string framework,
		string configuration,
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var applicationId = MauiProjectResolver.GetAndroidApplicationId(project.ProjectPath, framework, configuration);
		if (string.IsNullOrWhiteSpace(applicationId))
		{
			ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Could not resolve the Android application ID for '{project.ProjectName}'. Skipping pre-launch force-stop.");
			return;
		}

		var adbPath = ResolveAdbPath();
		if (adbPath is null)
			return;

		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Force-stopping any existing '{applicationId}' process on {device.Id} before starting trace collection.");
		var stopResult = await ProcessRunner.RunAsync(
			adbPath,
			["-s", device.Id, "shell", "am", "force-stop", applicationId],
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);

		if (!stopResult.Success)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"adb force-stop for '{applicationId}' returned exit code {stopResult.ExitCode}: {ProfileCommandProcessHelpers.GetProcessFailureDetails(stopResult)}");
		}
	}

	internal static int FindAvailableTcpPort(int startingPort, int maxPort = IPEndPoint.MaxPort)
	{
		using var reservation = ReserveAvailableTcpPort(startingPort, maxPort);
		return reservation.Port;
	}

	internal static async Task<ReservedProfilePorts> ReserveProfilePortsAndConfigureRoutingAsync(
		Device device,
		ProfileTransportConfiguration transport,
		int startingPort,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		if (startingPort < 1 || startingPort > IPEndPoint.MaxPort)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"--diagnostic-port must be between 1 and {IPEndPoint.MaxPort}.");
		}

		for (var port = startingPort; port < IPEndPoint.MaxPort; port++)
		{
			ReservedTcpPort? diagnosticReservation = null;
			ReservedTcpPort? dsrouterTcpReservation = null;
			ReservedTcpPort? exitControlReservation = null;
			var ownsExitControlAdbReverse = false;
			int? dsrouterTcpPort = transport.RequiresExplicitDsrouter ? GetDsrouterTcpPort(port) : null;
			var exitControlPort = GetExitControlPort(port, transport);

			try
			{
				diagnosticReservation = TryReserveTcpPort(port);
				if (diagnosticReservation is null)
					continue;

				if (dsrouterTcpPort is { } routerPort)
				{
					dsrouterTcpReservation = TryReserveTcpPort(routerPort);
					if (dsrouterTcpReservation is null)
					{
						diagnosticReservation.Dispose();
						continue;
					}
				}

				exitControlReservation = TryReserveTcpPort(exitControlPort);
				if (exitControlReservation is null)
				{
					diagnosticReservation.Dispose();
					dsrouterTcpReservation?.Dispose();
					continue;
				}

				ProfileCommandProcessHelpers.WriteVerbose(
					formatter,
					useJson,
					verbose,
					dsrouterTcpPort is { } explicitRouterPort
						? $"Reserved device diagnostic port {port}, host dsrouter TCP port {explicitRouterPort}, and exit control port {exitControlPort}."
						: $"Reserved diagnostic port {port} and exit control port {exitControlPort}.");
				if (transport.RequiresManualExitControlPortRouting)
				{
					var adbPath = ResolveAdbPathOrThrow();
					var reverseMappings = await GetAdbReverseMappingsAsync(adbPath, device, cancellationToken);
					var mappedPorts = transport.RequiresExplicitDsrouter
						? new[] { port, exitControlPort }
						: new[] { exitControlPort };
					var collidingPort = mappedPorts.FirstOrDefault(candidate => HasAdbReverseMapping(reverseMappings, candidate));
					if (collidingPort > 0)
					{
						ProfileCommandProcessHelpers.WriteVerbose(
							formatter,
							useJson,
							verbose,
							$"Device port {collidingPort} already has an adb reverse mapping on {device.Id}; trying the next profiling port set.");
						diagnosticReservation.Dispose();
						dsrouterTcpReservation?.Dispose();
						exitControlReservation.Dispose();
						continue;
					}

					ProfileCommandProcessHelpers.WriteVerbose(
						formatter,
						useJson,
						verbose,
						$"dotnet-trace/dsrouter will handle the diagnostics port; configuring adb reverse for the auxiliary exit-control port on {device.Id}.");
					await CreateAdbReverseMappingAsync(
						adbPath,
						device,
						formatter,
						useJson,
						verbose,
						exitControlPort,
						exitControlPort,
						cancellationToken);
					ownsExitControlAdbReverse = true;
				}

				var reservedPorts = new ReservedProfilePorts(
					port,
					dsrouterTcpPort,
					exitControlPort,
					diagnosticReservation,
					dsrouterTcpReservation,
					exitControlReservation);
				reservedPorts.ShouldCleanupExitControlAdbReverse = ownsExitControlAdbReverse;
				return reservedPorts;
			}
			catch (DiagnosticPortRoutingConflictException ex)
			{
				diagnosticReservation?.Dispose();
				dsrouterTcpReservation?.Dispose();
				exitControlReservation?.Dispose();
				if (ownsExitControlAdbReverse)
					await RemoveOwnedAdbReverseMappingAsync(device, formatter, useJson, verbose, exitControlPort, exitControlPort);
				ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Port {ex.Port} was unavailable for adb routing ({ex.Direction}): {ex.Details}");
			}
			catch
			{
				diagnosticReservation?.Dispose();
				dsrouterTcpReservation?.Dispose();
				exitControlReservation?.Dispose();
				if (ownsExitControlAdbReverse)
					await RemoveOwnedAdbReverseMappingAsync(device, formatter, useJson, verbose, exitControlPort, exitControlPort);
				throw;
			}
		}

		throw new MauiToolException(
			ErrorCodes.InternalError,
			$"Could not find free diagnostic/control TCP ports starting at {startingPort}.");
	}

	internal static async Task RemoveOwnedAdbReverseMappingsAsync(
		Device device,
		ReservedProfilePorts ports,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose)
	{
		if (ports.ShouldCleanupDiagnosticAdbReverse && ports.DsrouterTcpPort is { } dsrouterTcpPort)
		{
			await RemoveOwnedAdbReverseMappingAsync(
				device,
				formatter,
				useJson,
				verbose,
				ports.DiagnosticPort,
				dsrouterTcpPort);
		}

		if (ports.ShouldCleanupExitControlAdbReverse)
		{
			await RemoveOwnedAdbReverseMappingAsync(
				device,
				formatter,
				useJson,
				verbose,
				ports.ExitControlPort,
				ports.ExitControlPort);
		}
	}

	internal static IReadOnlyList<AdbReverseMapping> ParseAdbReverseMappings(string output)
	{
		var mappings = new List<AdbReverseMapping>();
		foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length < 2
				|| !TryParseTcpEndpoint(parts[^2], out var devicePort)
				|| !TryParseTcpEndpoint(parts[^1], out var hostPort))
			{
				continue;
			}

			mappings.Add(new AdbReverseMapping(devicePort, hostPort));
		}

		return mappings;
	}

	internal static bool HasAdbReverseMapping(IReadOnlyList<AdbReverseMapping> mappings, int devicePort)
		=> mappings.Any(mapping => mapping.DevicePort == devicePort);

	internal static bool IsOwnedAdbReverseMapping(
		IReadOnlyList<AdbReverseMapping> mappings,
		int devicePort,
		int hostPort)
	{
		var matchingDeviceMappings = mappings.Where(mapping => mapping.DevicePort == devicePort).ToArray();
		return matchingDeviceMappings.Length == 1 && matchingDeviceMappings[0].HostPort == hostPort;
	}

	internal static async Task EnsureAdbReverseMappingOwnedAsync(
		Device device,
		int devicePort,
		int hostPort,
		CancellationToken cancellationToken)
	{
		var adbPath = ResolveAdbPathOrThrow();
		var startedAt = Stopwatch.GetTimestamp();
		do
		{
			var mappings = await GetAdbReverseMappingsAsync(adbPath, device, cancellationToken);
			if (IsOwnedAdbReverseMapping(mappings, devicePort, hostPort))
				return;

			await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
		}
		while (Stopwatch.GetElapsedTime(startedAt) < ProfileCommand.s_traceStartupRetryTimeout);

		throw new MauiToolException(
			ErrorCodes.InternalError,
			$"dotnet-dsrouter did not establish the expected adb reverse mapping tcp:{devicePort} -> tcp:{hostPort} on '{device.Id}'.");
	}

	internal static async Task EnsureAdbReversePortAvailableAsync(
		Device device,
		int devicePort,
		CancellationToken cancellationToken)
	{
		var adbPath = ResolveAdbPathOrThrow();
		var mappings = await GetAdbReverseMappingsAsync(adbPath, device, cancellationToken);
		if (HasAdbReverseMapping(mappings, devicePort))
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"Device port {devicePort} acquired an adb reverse mapping before dotnet-dsrouter could start on '{device.Id}'. Rerun the profiling command to select another port set.");
		}
	}

	internal static int GetDsrouterTcpPort(int diagnosticPort)
		=> GetOffsetPort(diagnosticPort, 1, "dsrouter TCP");

	internal static int GetExitControlPort(int diagnosticPort, ProfileTransportConfiguration transport)
		=> GetOffsetPort(diagnosticPort, transport.RequiresExplicitDsrouter ? 2 : ProfileCommand.ExitControlPortOffset, "exit control");

	static int GetOffsetPort(int diagnosticPort, int offset, string purpose)
	{
		if (diagnosticPort > IPEndPoint.MaxPort - offset)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Cannot reserve the {purpose} port after diagnostic port {diagnosticPort}.");
		}

		return checked(diagnosticPort + offset);
	}

	static ReservedTcpPort ReserveAvailableTcpPort(int startingPort, int maxPort = IPEndPoint.MaxPort)
	{
		if (startingPort < 1 || startingPort > IPEndPoint.MaxPort)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"--diagnostic-port must be between 1 and {IPEndPoint.MaxPort}.");
		}

		var finalPort = Math.Min(maxPort, IPEndPoint.MaxPort);
		for (var port = startingPort; port <= finalPort; port++)
		{
			var reservation = TryReserveTcpPort(port);
			if (reservation is not null)
				return reservation;
		}

		throw new MauiToolException(
			ErrorCodes.InternalError,
			$"Could not find a free diagnostic TCP port starting at {startingPort}.");
	}

	static async Task CreateAdbReverseMappingAsync(
		string adbPath,
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		int devicePort,
		int hostPort,
		CancellationToken cancellationToken)
	{
		var devicePortSpec = $"tcp:{devicePort}";
		var hostPortSpec = $"tcp:{hostPort}";
		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Creating adb reverse for {device.Id}: {devicePortSpec} -> {hostPortSpec}.");
		var reverseResult = await ProcessRunner.RunAsync(
			adbPath,
			BuildAdbReverseArguments(device.Id, devicePort, hostPort),
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);

		if (reverseResult.Success)
			return;

		var details = ProfileCommandProcessHelpers.GetProcessFailureDetails(reverseResult);
		if (IsPortBindingConflict(details))
			throw new DiagnosticPortRoutingConflictException(devicePort, "reverse", details);

		throw MauiToolException.UserActionRequired(
			ErrorCodes.InternalError,
			$"Failed to open Android reverse port forwarding for {devicePortSpec} on '{device.Id}'.",
			[
				$"Reconnect the device and verify `adb -s {device.Id} reverse {devicePortSpec} {hostPortSpec}` succeeds.",
				"Then rerun the profiling command."
			],
			nativeError: details);
	}

	internal static string[] BuildAdbReverseArguments(string deviceId, int devicePort, int hostPort)
		=> ["-s", deviceId, "reverse", "--no-rebind", $"tcp:{devicePort}", $"tcp:{hostPort}"];

	static async Task<IReadOnlyList<AdbReverseMapping>> GetAdbReverseMappingsAsync(
		string adbPath,
		Device device,
		CancellationToken cancellationToken)
	{
		var result = await ProcessRunner.RunAsync(
			adbPath,
			["-s", device.Id, "reverse", "--list"],
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);
		if (result.Success)
			return ParseAdbReverseMappings(result.StandardOutput);

		throw MauiToolException.UserActionRequired(
			ErrorCodes.InternalError,
			$"Failed to list Android reverse port mappings on '{device.Id}'.",
			[
				$"Reconnect the device and verify `adb -s {device.Id} reverse --list` succeeds.",
				"Then rerun the profiling command."
			],
			nativeError: ProfileCommandProcessHelpers.GetProcessFailureDetails(result));
	}

	static async Task RemoveOwnedAdbReverseMappingAsync(
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		int devicePort,
		int hostPort)
	{
		var adbPath = ResolveAdbPath();
		if (adbPath is null)
			return;

		try
		{
			var mappings = await GetAdbReverseMappingsAsync(adbPath, device, CancellationToken.None);
			if (!IsOwnedAdbReverseMapping(mappings, devicePort, hostPort))
			{
				ProfileCommandProcessHelpers.WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"Leaving adb reverse tcp:{devicePort} unchanged because it no longer matches this session's tcp:{devicePort} -> tcp:{hostPort} mapping.");
				return;
			}

			ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Removing owned adb reverse for {device.Id} on tcp:{devicePort} -> tcp:{hostPort}.");
			_ = await ProcessRunner.RunAsync(
				adbPath,
				["-s", device.Id, "reverse", "--remove", $"tcp:{devicePort}"],
				timeout: ProfileCommand.s_adbPortForwardTimeout,
				cancellationToken: CancellationToken.None);
		}
		catch
		{
			// Best-effort cleanup only.
		}
	}

	static bool TryParseTcpEndpoint(string endpoint, out int port)
	{
		port = 0;
		return endpoint.StartsWith("tcp:", StringComparison.Ordinal)
			&& int.TryParse(endpoint.AsSpan("tcp:".Length), out port);
	}

	static string ResolveAdbPathOrThrow()
		=> ResolveAdbPath() ?? throw MauiToolException.UserActionRequired(
			ErrorCodes.AndroidAdbNotFound,
			"ADB was not found, so Android profiling port mappings could not be configured.",
			[
				"Install the Android SDK platform-tools so adb is available.",
				"Or add adb to PATH and rerun the profiling command."
			]);

	internal static string? ResolveAdbPath()
	{
		var adbPath = ProcessRunner.GetCommandPath("adb");
		if (!string.IsNullOrWhiteSpace(adbPath))
			return adbPath;

		var sdkPath = PlatformDetector.Paths.GetAndroidSdkPath();
		if (string.IsNullOrWhiteSpace(sdkPath))
			return null;

		var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
		var candidate = Path.Combine(sdkPath, "platform-tools", "adb" + extension);
		return File.Exists(candidate) ? candidate : null;
	}

	internal readonly record struct AdbReverseMapping(int DevicePort, int HostPort);

	static bool IsPortBindingConflict(string details) =>
		details.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
		|| details.Contains("cannot bind listener", StringComparison.OrdinalIgnoreCase)
		|| details.Contains("cannot bind socket", StringComparison.OrdinalIgnoreCase)
		|| details.Contains("cannot rebind", StringComparison.OrdinalIgnoreCase)
		|| details.Contains("already exists", StringComparison.OrdinalIgnoreCase);

	static ReservedTcpPort? TryReserveTcpPort(int port)
	{
		try
		{
			var listener = new TcpListener(IPAddress.Loopback, port);
			listener.Start();
			return new ReservedTcpPort(port, listener);
		}
		catch (SocketException)
		{
			return null;
		}
	}
}
