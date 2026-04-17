// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Utils;
using System.Text.Json.Nodes;
using Xamarin.MacDev;

namespace Microsoft.Maui.Cli.Providers.Apple;

/// <summary>
/// Apple platform provider backed by Xamarin.Apple.Tools.MaciOS.
/// Only functional on macOS; returns empty results on other platforms.
/// </summary>
public class AppleProvider : IAppleProvider
{
	readonly XcodeManager? _xcodeManager;
	readonly SimulatorService? _simulatorService;
	readonly RuntimeService? _runtimeService;
	readonly CommandLineTools? _commandLineTools;

	public AppleProvider()
	{
		if (!PlatformDetector.IsMacOS)
			return;

		var logger = ConsoleLogger.Instance;
		_xcodeManager = new XcodeManager(logger);
		_simulatorService = new SimulatorService(logger);
		_runtimeService = new RuntimeService(logger);
		_commandLineTools = new CommandLineTools(logger);
	}

	public List<XcodeInstallation> GetXcodeInstallations()
	{
		if (_xcodeManager is null)
			return new List<XcodeInstallation>();

		return _xcodeManager.List().Select(x => new XcodeInstallation
		{
			Path = x.Path,
			Version = x.Version.ToString(),
			Build = x.Build,
			IsSelected = x.IsSelected
		}).ToList();
	}

	public XcodeInstallation? GetSelectedXcode()
	{
		if (_xcodeManager is null)
			return null;

		var selected = _xcodeManager.GetSelected();
		if (selected is null)
			return null;

		return new XcodeInstallation
		{
			Path = selected.Path,
			Version = selected.Version.ToString(),
			Build = selected.Build,
			IsSelected = true
		};
	}

	public bool SelectXcode(string path)
	{
		if (_xcodeManager is null)
			return false;

		return _xcodeManager.Select(path);
	}

	public CommandLineToolsStatus GetCommandLineToolsStatus()
	{
		if (_commandLineTools is null)
			return new CommandLineToolsStatus();

		var info = _commandLineTools.Check();
		return new CommandLineToolsStatus
		{
			IsInstalled = info.IsInstalled,
			Version = info.Version,
			Path = info.Path
		};
	}

	public List<RuntimeInfo> GetRuntimes(string? platform = null, bool availableOnly = false)
	{
		if (_runtimeService is null)
			return new List<RuntimeInfo>();

		var runtimes = string.IsNullOrEmpty(platform)
			? _runtimeService.List(availableOnly)
			: _runtimeService.ListByPlatform(platform!, availableOnly);

		return runtimes.Select(r => new RuntimeInfo
		{
			Name = r.Name,
			Identifier = r.Identifier,
			Platform = r.Platform,
			Version = r.Version,
			BuildVersion = r.BuildVersion,
			IsAvailable = r.IsAvailable,
			IsBundled = r.IsBundled
		}).ToList();
	}

	public List<SimulatorInfo> GetSimulators(bool availableOnly = false)
	{
		if (_simulatorService is null)
			return new List<SimulatorInfo>();

		return _simulatorService.List(availableOnly).Select(s => new SimulatorInfo
		{
			Name = s.Name,
			Udid = s.Udid,
			State = s.State,
			Platform = s.Platform,
			OSVersion = s.OSVersion,
			RuntimeIdentifier = s.RuntimeIdentifier,
			DeviceTypeIdentifier = s.DeviceTypeIdentifier,
			IsAvailable = s.IsAvailable,
			IsBooted = s.IsBooted
		}).ToList();
	}

	public bool BootSimulator(string udidOrName)
	{
		return _simulatorService?.Boot(udidOrName) ?? false;
	}

	public bool ShutdownSimulator(string udidOrName)
	{
		return _simulatorService?.Shutdown(udidOrName) ?? false;
	}

	public bool DeleteSimulator(string udidOrName)
	{
		return _simulatorService?.Delete(udidOrName) ?? false;
	}

	public string? CreateSimulator(string name, string deviceTypeIdentifier, string? runtimeIdentifier = null)
	{
		return _simulatorService?.Create(name, deviceTypeIdentifier, runtimeIdentifier);
	}

	public List<HealthCheck> CheckHealth()
	{
		var checks = new List<HealthCheck>();

		if (!PlatformDetector.IsMacOS)
		{
			checks.Add(new HealthCheck
			{
				Category = "apple",
				Name = "Platform",
				Status = CheckStatus.Skipped,
				Message = "Apple checks only available on macOS"
			});
			return checks;
		}

		// Xcode check
		var xcode = _xcodeManager?.GetBest();
		if (xcode is not null)
		{
			checks.Add(new HealthCheck
			{
				Category = "apple",
				Name = "Xcode",
				Status = CheckStatus.Ok,
				Message = $"Xcode {xcode.Version} ({xcode.Build}) at {xcode.Path}",
				Details = new JsonObject
				{
					["version"] = xcode.Version.ToString(),
					["build"] = xcode.Build,
					["path"] = xcode.Path,
					["selected"] = xcode.IsSelected
				}
			});
		}
		else
		{
			checks.Add(new HealthCheck
			{
				Category = "apple",
				Name = "Xcode",
				Status = CheckStatus.Error,
				Message = "Xcode not found. Install Xcode from the App Store.",
				Fix = new FixInfo
				{
					IssueId = ErrorCodes.AppleXcodeNotFound,
					Description = "Install Xcode from the Mac App Store",
					AutoFixable = false,
					ManualSteps = new[] { "Open the Mac App Store and install Xcode" }
				}
			});
		}

		// Command Line Tools check
		var clt = _commandLineTools?.Check();
		if (clt is not null && clt.IsInstalled)
		{
			checks.Add(new HealthCheck
			{
				Category = "apple",
				Name = "Command Line Tools",
				Status = CheckStatus.Ok,
				Message = $"CLT {clt.Version ?? "installed"} at {clt.Path}"
			});
		}
		else
		{
			checks.Add(new HealthCheck
			{
				Category = "apple",
				Name = "Command Line Tools",
				Status = CheckStatus.Warning,
				Message = "Xcode Command Line Tools not found",
				Fix = new FixInfo
				{
					IssueId = ErrorCodes.AppleCltNotFound,
					Description = "Install Command Line Tools",
					AutoFixable = true,
					Command = "xcode-select --install"
				}
			});
		}

		// Simulator runtimes check
		HealthCheck iosRuntimesCheck;
		try
		{
			var runtimes = _runtimeService?.List(availableOnly: true);
			if (runtimes is { Count: > 0 })
			{
				var iosRuntimes = runtimes.Where(r => string.Equals(r.Platform, "iOS", StringComparison.OrdinalIgnoreCase)).ToList();
				iosRuntimesCheck = new HealthCheck
				{
					Category = "apple",
					Name = "iOS Runtimes",
					Status = iosRuntimes.Count > 0 ? CheckStatus.Ok : CheckStatus.Warning,
					Message = iosRuntimes.Count > 0
						? $"{iosRuntimes.Count} iOS runtime(s) available (latest: {iosRuntimes.OrderByDescending(r => Version.TryParse(r.Version, out var v) ? v : new Version(0, 0)).First().Name})"
						: "No iOS runtimes found. Install one via Xcode."
				};
			}
			else
			{
				iosRuntimesCheck = new HealthCheck
				{
					Category = "apple",
					Name = "iOS Runtimes",
					Status = CheckStatus.Warning,
					Message = "No simulator runtimes found. Install simulator runtimes via Xcode."
				};
			}
		}
		catch (Exception ex)
		{
			iosRuntimesCheck = new HealthCheck
			{
				Category = "apple",
				Name = "iOS Runtimes",
				Status = CheckStatus.Warning,
				Message = $"Unable to determine installed iOS simulator runtimes: {ex.Message}"
			};
		}

		checks.Add(iosRuntimesCheck);

		return checks;
	}

	public List<Device> GetDevices()
	{
		if (_simulatorService is null)
			return new List<Device>();

		var sims = _simulatorService.List(availableOnly: true);
		return sims.Select(s =>
		{
			// Scope to iOS simulators only for now; tvOS/watchOS/visionOS
			// are not yet recognized in the shared Platforms model.
			var platform = Platforms.iOS;

			return new Device
			{
				Id = s.Udid,
				Name = s.Name,
				Platforms = new[] { platform },
				Type = DeviceType.Simulator,
				State = s.IsBooted ? DeviceState.Booted : DeviceState.Shutdown,
				IsEmulator = true,
				IsRunning = s.IsBooted,
				ConnectionType = Models.ConnectionType.Local,
				EmulatorId = s.Udid,
				Model = s.DeviceTypeIdentifier,
				Manufacturer = "Apple",
				Version = s.OSVersion,
				VersionName = s.Platform != null ? $"{s.Platform} {s.OSVersion}" : s.OSVersion,
				Idiom = s.DeviceTypeIdentifier?.Contains("iPad") == true ? DeviceIdiom.Tablet : DeviceIdiom.Phone,
				Details = new JsonObject
				{
					["runtime"] = s.RuntimeIdentifier ?? "",
					["device_type"] = s.DeviceTypeIdentifier ?? "",
					["platform"] = s.Platform ?? ""
				}
			};
		}).ToList();
	}
}
