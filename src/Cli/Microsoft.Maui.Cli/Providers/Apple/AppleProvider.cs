// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Utils;
using System.Text.Json.Nodes;
using Xamarin.MacDev;
using Xamarin.MacDev.Models;

namespace Microsoft.Maui.Cli.Providers.Apple;

/// <summary>
/// Apple platform provider backed by Xamarin.Apple.Tools.MaciOS.
/// Only functional on macOS; returns empty results on other platforms.
/// Delegates environment checks to <see cref="EnvironmentChecker"/> and
/// environment install to <see cref="AppleInstaller"/>.
/// </summary>
public class AppleProvider : IAppleProvider
{
	readonly XcodeManager? _xcodeManager;
	readonly SimulatorService? _simulatorService;
	readonly RuntimeService? _runtimeService;
	readonly CommandLineTools? _commandLineTools;
	readonly EnvironmentChecker? _environmentChecker;
	readonly AppleInstaller? _appleInstaller;

	public AppleProvider()
	{
		if (!PlatformDetector.IsMacOS)
			return;

		var logger = ConsoleLogger.Instance;
		_xcodeManager = new XcodeManager(logger);
		_simulatorService = new SimulatorService(logger);
		_runtimeService = new RuntimeService(logger);
		_commandLineTools = new CommandLineTools(logger);
		_environmentChecker = new EnvironmentChecker(logger);
		_appleInstaller = new AppleInstaller(logger);
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

	public void OpenSimulatorApp()
	{
		using var process = System.Diagnostics.Process.Start("open", ["-a", "Simulator"]);
		process?.WaitForExit(5000);
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

	public bool EraseSimulator(string udidOrName)
	{
		return _simulatorService?.Erase(udidOrName) ?? false;
	}

	public bool InstallApp(string udid, string appBundlePath)
	{
		return _simulatorService?.Install(udid, appBundlePath) ?? false;
	}

	public bool UninstallApp(string udid, string bundleIdentifier)
	{
		return _simulatorService?.Uninstall(udid, bundleIdentifier) ?? false;
	}

	public bool LaunchApp(string udid, string bundleIdentifier, params string[] extraArgs)
	{
		return _simulatorService?.Launch(udid, bundleIdentifier, extraArgs) ?? false;
	}

	public bool TerminateApp(string udid, string bundleIdentifier)
	{
		return _simulatorService?.Terminate(udid, bundleIdentifier) ?? false;
	}

	public string? GetAppContainer(string udid, string bundleIdentifier, string? containerType = null)
	{
		return _simulatorService?.GetAppContainer(udid, bundleIdentifier, containerType);
	}

	public List<HealthCheck> CheckHealth()
	{
		var checks = new List<HealthCheck>();

		if (!PlatformDetector.IsMacOS || _environmentChecker is null)
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

		EnvironmentCheckResult result;
		try
		{
			result = _environmentChecker.Check();
		}
		catch (Exception ex)
		{
			checks.Add(new HealthCheck
			{
				Category = "apple",
				Name = "Environment",
				Status = CheckStatus.Error,
				Message = $"Environment check failed: {ex.Message}"
			});
			return checks;
		}

		checks.Add(MapXcodeCheck(result));
		checks.Add(MapCommandLineToolsCheck(result));

		// License check only meaningful when Xcode is present
		if (result.Xcode is not null)
			checks.Add(MapXcodeLicenseCheck());

		checks.Add(MapRuntimesCheck(result));

		if (result.Platforms.Count > 0)
		{
			checks.Add(new HealthCheck
			{
				Category = "apple",
				Name = "SDK Platforms",
				Status = CheckStatus.Ok,
				Message = $"Available: {string.Join(", ", result.Platforms)}",
				Details = new JsonObject
				{
					["platforms"] = new JsonArray(result.Platforms.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray())
				}
			});
		}

		return checks;
	}

	static HealthCheck MapXcodeCheck(EnvironmentCheckResult result)
	{
		if (result.Xcode is not null)
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "Xcode",
				Status = CheckStatus.Ok,
				Message = $"Xcode {result.Xcode.Version} ({result.Xcode.Build}) at {result.Xcode.Path}",
				Details = new JsonObject
				{
					["version"] = result.Xcode.Version.ToString(),
					["build"] = result.Xcode.Build,
					["path"] = result.Xcode.Path,
					["selected"] = result.Xcode.IsSelected
				}
			};
		}

		return new HealthCheck
		{
			Category = "apple",
			Name = "Xcode",
			Status = CheckStatus.Error,
			Message = "Xcode not found. Install Xcode from the App Store or run 'maui apple install'.",
			Fix = new FixInfo
			{
				IssueId = ErrorCodes.AppleXcodeNotFound,
				Description = "Install Xcode from the Mac App Store",
				AutoFixable = false,
				ManualSteps = new[] { "Open the Mac App Store and install Xcode", "Or run: maui apple install" }
			}
		};
	}

	static HealthCheck MapCommandLineToolsCheck(EnvironmentCheckResult result)
	{
		if (result.CommandLineTools.IsInstalled)
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "Command Line Tools",
				Status = CheckStatus.Ok,
				Message = $"CLT {result.CommandLineTools.Version ?? "installed"} at {result.CommandLineTools.Path}"
			};
		}

		return new HealthCheck
		{
			Category = "apple",
			Name = "Command Line Tools",
			Status = CheckStatus.Error,
			Message = "Xcode Command Line Tools not found. Run 'maui apple install' to install.",
			Fix = new FixInfo
			{
				IssueId = ErrorCodes.AppleCltNotFound,
				Description = "Install Command Line Tools",
				AutoFixable = true,
				Command = "xcode-select --install"
			}
		};
	}

	HealthCheck MapXcodeLicenseCheck()
	{
		try
		{
			if (_environmentChecker?.IsXcodeLicenseAccepted() == true)
			{
				return new HealthCheck
				{
					Category = "apple",
					Name = "Xcode License",
					Status = CheckStatus.Ok,
					Message = "Xcode license accepted"
				};
			}

			return new HealthCheck
			{
				Category = "apple",
				Name = "Xcode License",
				Status = CheckStatus.Error,
				Message = "Xcode license not accepted. Run 'sudo xcodebuild -license accept'.",
				Fix = new FixInfo
				{
					IssueId = ErrorCodes.AppleXcodeLicenseNotAccepted,
					Description = "Accept the Xcode license agreement",
					AutoFixable = false,
					ManualSteps = new[] { "Run: sudo xcodebuild -license accept", "Or run: maui apple install" }
				}
			};
		}
		catch
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "Xcode License",
				Status = CheckStatus.Warning,
				Message = "Unable to determine Xcode license status"
			};
		}
	}

	static HealthCheck MapRuntimesCheck(EnvironmentCheckResult result)
	{
		if (result.Runtimes.Count == 0)
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "iOS Runtimes",
				Status = CheckStatus.Warning,
				Message = "No simulator runtimes found. Run 'maui apple install --platform iOS' to install."
			};
		}

		var iosRuntimes = result.Runtimes
			.Where(r => string.Equals(r.Platform, "iOS", StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (iosRuntimes.Count == 0)
		{
			return new HealthCheck
			{
				Category = "apple",
				Name = "iOS Runtimes",
				Status = CheckStatus.Warning,
				Message = "No iOS runtimes found. Run 'maui apple install --platform iOS' to install."
			};
		}

		var latest = iosRuntimes
			.OrderByDescending(r => Version.TryParse(r.Version, out var v) ? v : new Version(0, 0))
			.First();

		return new HealthCheck
		{
			Category = "apple",
			Name = "iOS Runtimes",
			Status = CheckStatus.Ok,
			Message = $"{iosRuntimes.Count} iOS runtime(s) available (latest: {latest.Name})"
		};
	}

	public Task<AppleInstallResult> InstallEnvironmentAsync(IEnumerable<string>? platforms = null, bool dryRun = false, CancellationToken cancellationToken = default)
	{
		if (!PlatformDetector.IsMacOS || _appleInstaller is null)
		{
			return Task.FromResult(new AppleInstallResult
			{
				Status = "skipped",
				DryRun = dryRun
			});
		}

		// AppleInstaller.Install() is synchronous and cannot be interrupted mid-operation.
		// Task.Run enables thread pool cancellation between the before/after checks.
		return Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = _appleInstaller.Install(platforms, dryRun);
			cancellationToken.ThrowIfCancellationRequested();

			return new AppleInstallResult
			{
				Status = result.Status.ToString().ToLowerInvariant(),
				XcodeVersion = result.Xcode is not null ? $"{result.Xcode.Version} ({result.Xcode.Build})" : null,
				CommandLineToolsInstalled = result.CommandLineTools.IsInstalled,
				Platforms = result.Platforms.ToList(),
				Runtimes = result.Runtimes.Select(r => r.Name).ToList(),
				DryRun = dryRun
			};
		}, cancellationToken);
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
