// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Utils;
using System.Text.Json.Nodes;
using Xamarin.Android.Tools;

namespace Microsoft.Maui.Cli.Providers.Android;

/// <summary>
/// Wrapper for Android Debug Bridge (adb) operations.
/// Delegates to Xamarin.Android.Tools.AdbRunner for core functionality.
/// </summary>
public class Adb
{
	readonly IDictionary<string, string>? _environmentVariables;
	readonly string? _adbPath;
	AdbRunner? _runner;

	public Adb(Func<string?> getSdkPath, IDictionary<string, string>? environmentVariables = null)
	{
		_adbPath = ResolveAdbPath(getSdkPath());
		_environmentVariables = environmentVariables;
	}

	public string? AdbPath => _adbPath;

	public bool IsAvailable => _adbPath != null;

	internal AdbRunner? Runner => GetRunner();

	AdbRunner? GetRunner()
	{
		if (_adbPath == null)
			return null;

		return _runner ??= new AdbRunner(_adbPath, _environmentVariables);
	}

	static string? ResolveAdbPath(string? sdkPath)
	{
		if (string.IsNullOrEmpty(sdkPath))
			return null;

		var ext = OperatingSystem.IsWindows() ? ".exe" : "";
		var path = Path.Combine(sdkPath, "platform-tools", "adb" + ext);
		return File.Exists(path) ? path : null;
	}

	public async Task<List<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
	{
		var runner = Runner;
		if (runner == null)
			return new List<Device>();

		try
		{
			// AdbRunner.ListDevicesAsync already queries AVD names for online emulators
			// via getprop ro.boot.qemu.avd_name + emu avd name fallback
			var devices = await runner.ListDevicesAsync(cancellationToken);
			var mapped = devices.Select(MapToMauiDevice).ToList();

			// Enrich addressable devices with `adb shell getprop` so physical
			// USB devices surface architecture/version/manufacturer/model the
			// same way the legacy ServiceHub PopulateDeviceAsync did.
			return await AndroidDeviceEnricher.EnrichAsync(
				mapped,
				(serial, prop, ct) => runner.GetShellPropertyAsync(serial, prop, ct),
				cancellationToken);
		}
		catch (InvalidOperationException ex)
		{
			System.Diagnostics.Trace.WriteLine($"ADB GetDevicesAsync failed: {ex.Message}");
			return new List<Device>();
		}
	}

	static Device MapToMauiDevice(AdbDeviceInfo info)
	{
		var isEmulator = info.IsEmulator;
		var state = MapDeviceState(info.Status);
		var isRunning = state == DeviceState.Connected || state == DeviceState.Booted;

		var details = new JsonObject();
		if (!string.IsNullOrEmpty(info.AvdName))
			details["avd"] = info.AvdName;

		return new Device
		{
			Id = info.Serial,
			Name = !string.IsNullOrEmpty(info.AvdName) ? info.AvdName : (info.Model ?? info.Serial),
			Platforms = new[] { "android" },
			Type = isEmulator ? DeviceType.Emulator : DeviceType.Physical,
			State = state,
			IsEmulator = isEmulator,
			IsRunning = isRunning,
			ConnectionType = isEmulator ? ConnectionType.Local : ConnectionType.Usb,
			EmulatorId = info.AvdName,
			Model = info.Model,
			Idiom = DeviceIdiom.Phone,
			Details = details.Count > 0 ? details : null,
		};
	}

	static DeviceState MapDeviceState(AdbDeviceStatus status)
	{
		return status switch
		{
			AdbDeviceStatus.Online => DeviceState.Connected,
			AdbDeviceStatus.Offline => DeviceState.Offline,
			AdbDeviceStatus.Unauthorized => DeviceState.Disconnected,
			AdbDeviceStatus.NotRunning => DeviceState.Shutdown,
			_ => DeviceState.Unknown
		};
	}

	public async Task StopEmulatorAsync(string deviceSerial, CancellationToken cancellationToken = default)
	{
		if (!IsAvailable)
			throw new MauiToolException(ErrorCodes.AndroidAdbNotFound, "ADB not found");

		var runner = Runner;
		if (runner == null)
			throw new MauiToolException(ErrorCodes.AndroidAdbNotFound, "ADB not found");

		await runner.StopEmulatorAsync(deviceSerial, cancellationToken);
	}

	/// <summary>Lists active <c>adb forward</c> (host → device) port rules for a device.</summary>
	public async Task<IReadOnlyList<AndroidPortMapping>> ListForwardPortsAsync(string deviceSerial, CancellationToken cancellationToken = default)
	{
		var runner = RequireRunner();
		var rules = await runner.ListForwardPortsAsync(deviceSerial, cancellationToken);
		return rules.Select(MapPortRule).ToList();
	}

	/// <summary>Lists active <c>adb reverse</c> (device → host) port rules for a device.</summary>
	public async Task<IReadOnlyList<AndroidPortMapping>> ListReversePortsAsync(string deviceSerial, CancellationToken cancellationToken = default)
	{
		var runner = RequireRunner();
		var rules = await runner.ListReversePortsAsync(deviceSerial, cancellationToken);
		return rules.Select(MapPortRule).ToList();
	}

	/// <summary>Adds an <c>adb forward tcp:hostPort tcp:devicePort</c> rule.</summary>
	public async Task AddForwardPortAsync(string deviceSerial, int hostPort, int devicePort, CancellationToken cancellationToken = default)
	{
		var runner = RequireRunner();
		await runner.ForwardPortAsync(
			deviceSerial,
			new AdbPortSpec(AdbProtocol.Tcp, hostPort),
			new AdbPortSpec(AdbProtocol.Tcp, devicePort),
			cancellationToken);
	}

	/// <summary>Adds an <c>adb reverse tcp:devicePort tcp:hostPort</c> rule.</summary>
	public async Task AddReversePortAsync(string deviceSerial, int devicePort, int hostPort, CancellationToken cancellationToken = default)
	{
		var runner = RequireRunner();
		await runner.ReversePortAsync(
			deviceSerial,
			new AdbPortSpec(AdbProtocol.Tcp, devicePort),
			new AdbPortSpec(AdbProtocol.Tcp, hostPort),
			cancellationToken);
	}

	/// <summary>Removes all <c>adb forward</c> rules for a device.</summary>
	public async Task ClearForwardPortsAsync(string deviceSerial, CancellationToken cancellationToken = default)
	{
		var runner = RequireRunner();
		await runner.RemoveAllForwardPortsAsync(deviceSerial, cancellationToken);
	}

	/// <summary>Removes all <c>adb reverse</c> rules for a device.</summary>
	public async Task ClearReversePortsAsync(string deviceSerial, CancellationToken cancellationToken = default)
	{
		var runner = RequireRunner();
		await runner.RemoveAllReversePortsAsync(deviceSerial, cancellationToken);
	}

	AdbRunner RequireRunner() =>
		Runner ?? throw new MauiToolException(ErrorCodes.AndroidAdbNotFound, "ADB not found");

	static AndroidPortMapping MapPortRule(AdbPortRule rule) => new()
	{
		Local = rule.Local.Port,
		Remote = rule.Remote.Port,
		Protocol = rule.Local.Protocol.ToString().ToLowerInvariant(),
	};

}
