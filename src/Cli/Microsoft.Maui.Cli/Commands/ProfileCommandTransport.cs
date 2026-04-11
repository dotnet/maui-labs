// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileCommandTransport
{
	internal static string[] BuildCompileArguments(
		string projectPath,
		string framework,
		string configuration,
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		ProfilingBuildInjection? buildInjection)
	{
		var args = new List<string>
		{
			"build",
			projectPath,
			"-c", configuration,
			"-f", framework,
			"--nologo"
		};

		AppendDiagnosticArguments(args, transport, diagnosticPort);
		AppendBuildInjectionArguments(args, buildInjection);
		return [.. args];
	}

	internal static string[] BuildLaunchArguments(
		string projectPath,
		string framework,
		string configuration,
		Device device,
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		ProfilingBuildInjection? buildInjection)
	{
		var args = new List<string>
		{
			"build",
			projectPath,
			"-t:Run",
			"-c", configuration,
			"-f", framework,
			"-p:WaitForExit=false",
		};

		AppendDiagnosticArguments(args, transport, diagnosticPort);

		if (string.Equals(transport.Platform, Platforms.Android, StringComparison.OrdinalIgnoreCase))
		{
			args.Add($"-p:AdbTarget=-s {device.Id}");
			args.Add("-p:AndroidEnableProfiler=true");
		}
		else if (string.Equals(transport.Platform, Platforms.iOS, StringComparison.OrdinalIgnoreCase))
		{
			args.Add($"-p:_DeviceName=:v2:udid={device.Id}");
			args.Add("-p:_MlaunchWaitForExit=false");
		}

		AppendBuildInjectionArguments(args, buildInjection);
		return [.. args];
	}

	static void AppendDiagnosticArguments(List<string> args, ProfileTransportConfiguration transport, int diagnosticPort)
	{
		args.Add($"-p:DiagnosticAddress={transport.DiagnosticAddress}");
		args.Add($"-p:DiagnosticPort={diagnosticPort}");
		args.Add("-p:DiagnosticSuspend=true");
		args.Add($"-p:DiagnosticListenMode={transport.DiagnosticListenMode}");
		args.Add("-p:EnableDiagnostics=true");
	}

	static void AppendBuildInjectionArguments(List<string> args, ProfilingBuildInjection? buildInjection)
	{
		if (buildInjection is null)
			return;

		args.Add($"-p:CustomAfterMicrosoftCommonTargets={buildInjection.TargetsPath}");
		args.Add("-p:MauiStartupProfilingInject=true");
		args.Add($"-p:MauiStartupProfilingExitHost={buildInjection.ExitControlHost}");
		args.Add($"-p:MauiStartupProfilingExitPort={buildInjection.ExitControlPort}");
		args.Add($"-p:MauiStartupProfilingInjectBootstrap={(buildInjection.InjectBootstrap ? "true" : "false")}");

		if (!string.IsNullOrWhiteSpace(buildInjection.AssemblyPath))
			args.Add($"-p:MauiStartupProfilingAssemblyPath={buildInjection.AssemblyPath}");
	}

	internal static string[] BuildDsrouterArguments(ProfileTransportConfiguration transport, int diagnosticPort)
	{
		var args = new List<string>
		{
			transport.DsrouterKind,
			transport.DsrouterRuntimeEndpointOption,
			$"{IPAddress.Loopback}:{diagnosticPort}"
		};

		if (!string.IsNullOrWhiteSpace(transport.DsrouterForwardPort))
		{
			args.Add("--forward-port");
			args.Add(transport.DsrouterForwardPort);
		}

		return [.. args];
	}

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
			ProfileCommandTooling.WriteVerbose(formatter, useJson, verbose, $"Could not resolve the Android application ID for '{project.ProjectName}'. Skipping pre-launch force-stop.");
			return;
		}

		var adbPath = ResolveAdbPath();
		if (adbPath is null)
			return;

		ProfileCommandTooling.WriteVerbose(formatter, useJson, verbose, $"Force-stopping any existing '{applicationId}' process on {device.Id} before starting trace collection.");
		var stopResult = await ProcessRunner.RunAsync(
			adbPath,
			["-s", device.Id, "shell", "am", "force-stop", applicationId],
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);

		if (!stopResult.Success)
		{
			ProfileCommandTooling.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"adb force-stop for '{applicationId}' returned exit code {stopResult.ExitCode}: {ProfileCommandTooling.GetProcessFailureDetails(stopResult)}");
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
			ReservedTcpPort? exitControlReservation = null;
			var exitControlPort = GetExitControlPort(port);

			try
			{
				diagnosticReservation = TryReserveTcpPort(port);
				if (diagnosticReservation is null)
					continue;

				exitControlReservation = TryReserveTcpPort(exitControlPort);
				if (exitControlReservation is null)
				{
					diagnosticReservation.Dispose();
					continue;
				}

				ProfileCommandTooling.WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"Reserved diagnostic port {port} and exit control port {exitControlPort}.");
				if (transport.RequiresManualExitControlPortRouting)
				{
					ProfileCommandTooling.WriteVerbose(
						formatter,
						useJson,
						verbose,
						$"dotnet-dsrouter will handle the diagnostics port; configuring adb reverse for the auxiliary exit-control port on {device.Id}.");
					await EnsureAdbPortRoutingAsync(device, formatter, useJson, verbose, cancellationToken, exitControlPort);
				}
				return new ReservedProfilePorts(port, exitControlPort, diagnosticReservation, exitControlReservation);
			}
			catch (DiagnosticPortRoutingConflictException ex)
			{
				diagnosticReservation?.Dispose();
				exitControlReservation?.Dispose();
				await RemoveAdbPortRoutingAsync(device, formatter, useJson, verbose, port, exitControlPort);
				ProfileCommandTooling.WriteVerbose(formatter, useJson, verbose, $"Port {ex.Port} was unavailable for adb routing ({ex.Direction}): {ex.Details}");
			}
			catch
			{
				diagnosticReservation?.Dispose();
				exitControlReservation?.Dispose();
				throw;
			}
		}

		throw new MauiToolException(
			ErrorCodes.InternalError,
			$"Could not find free diagnostic/control TCP ports starting at {startingPort}.");
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

	internal static async Task EnsureAdbPortRoutingAsync(
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken,
		params int[] ports)
	{
		var adbPath = ResolveAdbPath();
		if (adbPath is null)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.AndroidAdbNotFound,
				"ADB was not found, so the app exit-control port could not be opened on the Android device.",
				[
					"Install the Android SDK platform-tools so adb is available.",
					"Or add adb to PATH and rerun `maui profile`."
				]);
		}

		foreach (var port in ports.Distinct().Where(port => port > 0))
		{
			var portSpec = $"tcp:{port}";
			ProfileCommandTooling.WriteVerbose(formatter, useJson, verbose, $"Ensuring adb reverse for {device.Id} on {portSpec}.");

			await ResetAdbPortMappingAsync(adbPath, device.Id, "reverse", portSpec, cancellationToken);
			var reverseResult = await ProcessRunner.RunAsync(
				adbPath,
				["-s", device.Id, "reverse", portSpec, portSpec],
				timeout: ProfileCommand.s_adbPortForwardTimeout,
				cancellationToken: cancellationToken);

			if (!reverseResult.Success)
			{
				var details = ProfileCommandTooling.GetProcessFailureDetails(reverseResult);
				if (IsPortBindingConflict(details))
					throw new DiagnosticPortRoutingConflictException(port, "reverse", details);

				throw MauiToolException.UserActionRequired(
					ErrorCodes.InternalError,
					$"Failed to open Android reverse port forwarding for {portSpec} on '{device.Id}'.",
					[
						$"Reconnect the device or emulator and verify `adb -s {device.Id} reverse {portSpec} {portSpec}` succeeds.",
						"Then rerun `maui profile`."
					],
					nativeError: details);
			}
		}
	}

	internal static async Task RemoveAdbPortRoutingAsync(
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		params int[] ports)
	{
		if (ports.Length == 0 || ports.All(port => port < 1))
			return;

		var adbPath = ResolveAdbPath();
		if (adbPath is null)
			return;

		try
		{
			foreach (var port in ports.Distinct().Where(port => port > 0))
			{
				var portSpec = $"tcp:{port}";
				ProfileCommandTooling.WriteVerbose(formatter, useJson, verbose, $"Removing adb reverse/forward mappings for {device.Id} on {portSpec}.");
				await ResetAdbPortMappingAsync(adbPath, device.Id, "reverse", portSpec, CancellationToken.None);
				await ResetAdbPortMappingAsync(adbPath, device.Id, "forward", portSpec, CancellationToken.None);
			}
		}
		catch
		{
			// Best-effort cleanup only.
		}
	}

	static async Task ResetAdbPortMappingAsync(string adbPath, string deviceId, string direction, string portSpec, CancellationToken cancellationToken)
	{
		var removeArgs = direction switch
		{
			"reverse" => new[] { "-s", deviceId, "reverse", "--remove", portSpec },
			"forward" => new[] { "-s", deviceId, "forward", "--remove", portSpec },
			_ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Expected 'reverse' or 'forward'.")
		};

		_ = await ProcessRunner.RunAsync(
			adbPath,
			removeArgs,
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);
	}

	static string? ResolveAdbPath()
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

	internal static ProfilingBuildInjection? TryCreateBuildInjection(string exitControlHost, int exitControlPort, bool injectBootstrap)
	{
		var targetsPath = TryResolveBuildAssetPath(ProfileCommand.StartupProfilingInjectionTargetsFileName);
		var assemblyPath = TryResolveBuildAssetPath(ProfileCommand.StartupProfilingAssemblyFileName);
		var sourcePath = TryResolveBuildAssetPath(ProfileCommand.StartupProfilingInjectionSourceFileName);

		if (targetsPath is null || assemblyPath is null || sourcePath is null)
			return null;

		return new ProfilingBuildInjection(targetsPath, assemblyPath, exitControlHost, exitControlPort, injectBootstrap);
	}

	static string? TryResolveBuildAssetPath(string fileName)
	{
		var baseDirectory = AppContext.BaseDirectory;
		var candidates = new[]
		{
			Path.Combine(baseDirectory, fileName),
			Path.Combine(baseDirectory, "Build", fileName)
		};

		return candidates.FirstOrDefault(File.Exists);
	}

	static bool IsPortBindingConflict(string details) =>
		details.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
		|| details.Contains("cannot bind listener", StringComparison.OrdinalIgnoreCase)
		|| details.Contains("cannot bind socket", StringComparison.OrdinalIgnoreCase);

	internal static int GetExitControlPort(int diagnosticPort)
	{
		if (diagnosticPort >= IPEndPoint.MaxPort)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Cannot reserve an exit control port after diagnostic port {diagnosticPort}.");
		}

		return checked(diagnosticPort + ProfileCommand.ExitControlPortOffset);
	}

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

	internal static ProfileTransportConfiguration ResolveProfileTransport(string platform, Device device)
	{
		var normalizedPlatform = Platforms.Normalize(platform);
		return normalizedPlatform switch
		{
			Platforms.Android => new ProfileTransportConfiguration(
				Platform: Platforms.Android,
				DiagnosticAddress: device.IsEmulator ? "10.0.2.2" : IPAddress.Loopback.ToString(),
				DiagnosticListenMode: "connect",
				DsrouterKind: "server-server",
				DsrouterRuntimeEndpointOption: "-tcps",
				DsrouterForwardPort: "Android",
				RequiresManualExitControlPortRouting: !device.IsEmulator),
			Platforms.iOS => new ProfileTransportConfiguration(
				Platform: Platforms.iOS,
				DiagnosticAddress: IPAddress.Loopback.ToString(),
				DiagnosticListenMode: "listen",
				DsrouterKind: "server-client",
				DsrouterRuntimeEndpointOption: "-tcpc",
				DsrouterForwardPort: device.IsEmulator ? null : "iOS",
				RequiresManualExitControlPortRouting: false),
			_ => throw new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				$"Startup profiling is not implemented yet for platform '{platform}'.")
		};
	}
}
