// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Utils;
using XatSdkManager = Xamarin.Android.Tools.SdkManager;
using XatSdkPackage = Xamarin.Android.Tools.SdkPackage;

namespace Microsoft.Maui.Cli.Providers.Android;

/// <summary>
/// Wrapper for Android SDK Manager operations.
/// Delegates to Xamarin.Android.Tools.SdkManager for core functionality.
/// </summary>
public class SdkManager : IDisposable
{
	readonly Func<string?> _getSdkPath;
	readonly Func<string?> _getJdkPath;
	readonly XatSdkManager _sdkManager;

	/// <summary>
	/// Creates a logger that forwards android-tools diagnostics when verbose mode is active.
	/// When verbose is false, only Error levels are forwarded; others are suppressed
	/// to avoid polluting CLI output with expected warnings about missing JDK paths, etc.
	/// </summary>
	static Action<TraceLevel, string> CreateLogger(bool verbose = false)
	{
		if (verbose)
			return (level, msg) => Console.Error.WriteLine($"[android-tools:{level}] {msg}");

		return (level, msg) =>
		{
			if (level == TraceLevel.Error)
				Console.Error.WriteLine($"[android-tools:error] {msg}");
		};
	}

	public SdkManager(Func<string?> getSdkPath, Func<string?> getJdkPath, bool verbose = false)
	{
		_getSdkPath = getSdkPath;
		_getJdkPath = getJdkPath;
		_sdkManager = new XatSdkManager(logger: CreateLogger(verbose));
	}

	(string? SdkPath, string? JdkPath) SyncPaths()
	{
		var sdkPath = _getSdkPath();
		var jdkPath = _getJdkPath();
		_sdkManager.AndroidSdkPath = sdkPath;
		_sdkManager.JavaSdkPath = jdkPath;
		return (sdkPath, jdkPath);
	}

	public string? SdkManagerPath
	{
		get
		{
			var (sdkPath, _) = SyncPaths();
			return ResolveSdkManagerPath(sdkPath) ?? _sdkManager.FindSdkManagerPath();
		}
	}

	public bool IsAvailable => !string.IsNullOrEmpty(SdkManagerPath);

	public void Dispose() => _sdkManager.Dispose();

	internal static string? ResolveSdkManagerPath(string? sdkPath)
	{
		if (string.IsNullOrEmpty(sdkPath))
			return null;

		var ext = OperatingSystem.IsWindows() ? ".bat" : "";

		static string? FindToolInDirectory(string directoryPath, string extension)
		{
			var toolPath = Path.Combine(directoryPath, "bin", "sdkmanager" + extension);
			return File.Exists(toolPath) ? toolPath : null;
		}

		var cmdlineToolsDir = Path.Combine(sdkPath, "cmdline-tools");
		if (Directory.Exists(cmdlineToolsDir))
		{
			var subdirs = new List<(string path, Version version)>();
			foreach (var dir in Directory.GetDirectories(cmdlineToolsDir))
			{
				var name = Path.GetFileName(dir);
				if (string.IsNullOrEmpty(name) || name.Equals("latest", StringComparison.OrdinalIgnoreCase))
					continue;

				Version.TryParse(name, out var version);
				subdirs.Add((dir, version ?? new Version(0, 0)));
			}

			subdirs.Sort((a, b) => b.version.CompareTo(a.version));

			foreach (var (dir, _) in subdirs)
			{
				var toolPath = FindToolInDirectory(dir, ext);
				if (toolPath != null)
					return toolPath;
			}

			var latestPath = FindToolInDirectory(Path.Combine(cmdlineToolsDir, "latest"), ext);
			if (latestPath != null)
				return latestPath;

			var directPath = FindToolInDirectory(cmdlineToolsDir, ext);
			if (directPath != null)
				return directPath;
		}

		var legacyPath = Path.Combine(sdkPath, "tools", "bin", "sdkmanager" + ext);
		return File.Exists(legacyPath) ? legacyPath : null;
	}

	public async Task<List<SdkPackage>> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
	{
		SyncPaths();
		try
		{
			var (installed, _) = await _sdkManager.ListAsync(cancellationToken);
			return installed.Select(MapToMauiPackage).ToList();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.WriteLine($"SDK GetInstalledPackagesAsync failed: {ex.Message}");
			return new List<SdkPackage>();
		}
	}

	public async Task<List<SdkPackage>> GetAvailablePackagesAsync(CancellationToken cancellationToken = default)
	{
		SyncPaths();
		try
		{
			var (_, available) = await _sdkManager.ListAsync(cancellationToken);
			return available.Select(MapToMauiPackage).ToList();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.WriteLine($"SDK GetAvailablePackagesAsync failed: {ex.Message}");
			return new List<SdkPackage>();
		}
	}

	static SdkPackage MapToMauiPackage(XatSdkPackage pkg) => new()
	{
		Path = pkg.Path,
		Version = pkg.Version,
		Description = pkg.Description,
		IsInstalled = pkg.IsInstalled
	};

	public async Task InstallPackagesAsync(IEnumerable<string> packages, bool acceptLicenses = false,
		CancellationToken cancellationToken = default)
	{
		await InstallPackagesAsync(packages, acceptLicenses, onProgress: null, cancellationToken);
	}

	public async Task InstallPackagesAsync(IEnumerable<string> packages, bool acceptLicenses,
		Action<string, int, int>? onProgress, CancellationToken cancellationToken = default)
	{
		SyncPaths();
		EnsureAvailable();

		var packageList = packages.ToList();

		try
		{
			if (onProgress is null)
			{
				await _sdkManager.InstallAsync(packageList, acceptLicenses, cancellationToken);
			}
			else
			{
				// Install one at a time so we can report per-package progress
				for (var i = 0; i < packageList.Count; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					onProgress(packageList[i], i + 1, packageList.Count);
					await _sdkManager.InstallAsync(new[] { packageList[i] }, acceptLicenses, cancellationToken);
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (IsPermissionError(ex))
				throw new UnauthorizedAccessException($"Failed to install packages (permission denied): {ex.Message}", ex);

			throw new MauiToolException(
				ErrorCodes.AndroidPackageInstallFailed,
				$"Failed to install packages: {ex.Message}",
				nativeError: ex.Message);
		}
	}

	public async Task AcceptLicensesAsync(CancellationToken cancellationToken = default)
	{
		await AcceptLicensesAsync(onProgress: null, cancellationToken);
	}

	public async Task AcceptLicensesAsync(Action<string>? onProgress, CancellationToken cancellationToken = default)
	{
		SyncPaths();
		EnsureAvailable();
		onProgress?.Invoke("Accepting SDK licenses...");
		await _sdkManager.AcceptLicensesAsync(cancellationToken);
		onProgress?.Invoke("SDK licenses accepted");
	}

	public async Task UninstallPackagesAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default)
	{
		SyncPaths();
		EnsureAvailable();

		try
		{
			await _sdkManager.UninstallAsync(packages, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (IsPermissionError(ex))
				throw new UnauthorizedAccessException($"Failed to uninstall packages (permission denied): {ex.Message}", ex);

			throw new MauiToolException(
				ErrorCodes.AndroidPackageInstallFailed,
				$"Failed to uninstall packages: {ex.Message}",
				nativeError: ex.Message);
		}
	}

	public Task<bool> AreLicensesAcceptedAsync(CancellationToken cancellationToken = default)
	{
		SyncPaths();
		return Task.FromResult(_sdkManager.AreLicensesAccepted());
	}

	public async Task InstallSdkAsync(string targetPath, IProgress<string>? progress = null,
		CancellationToken cancellationToken = default)
	{
		_sdkManager.AndroidSdkPath = targetPath;
		var bootstrapProgress = new Progress<Xamarin.Android.Tools.SdkBootstrapProgress>(p =>
			progress?.Report($"{p.Phase}: {p.Message}"));
		await _sdkManager.BootstrapAsync(targetPath, bootstrapProgress, cancellationToken);
	}

	/// <summary>
	/// Installs SDK with structured progress reporting for rich UI rendering.
	/// </summary>
	public async Task InstallSdkAsync(string targetPath,
		Action<Xamarin.Android.Tools.SdkBootstrapPhase, int, string>? onProgress = null,
		CancellationToken cancellationToken = default)
	{
		_sdkManager.AndroidSdkPath = targetPath;
		var bootstrapProgress = new Progress<Xamarin.Android.Tools.SdkBootstrapProgress>(p =>
			onProgress?.Invoke(p.Phase, p.PercentComplete, p.Message));
		await _sdkManager.BootstrapAsync(targetPath, bootstrapProgress, cancellationToken);
	}

	void EnsureAvailable()
	{
		if (!IsAvailable)
			throw MauiToolException.AutoFixable(
				ErrorCodes.AndroidSdkManagerNotFound,
				"SDK Manager not found. Run 'maui android install' first.",
				"maui android install");
	}

	/// <summary>
	/// Checks if an exception from sdkmanager indicates a file/directory permission problem.
	/// The Android sdkmanager process reports permission errors as text in stderr/stdout
	/// rather than throwing UnauthorizedAccessException, so we pattern-match the message.
	/// </summary>
	static bool IsPermissionError(Exception ex)
	{
		if (ex is UnauthorizedAccessException)
			return true;

		var message = ex.Message;
		if (string.IsNullOrEmpty(message))
			return false;

		return message.Contains("Failed to read or create install properties file", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("Access to the path", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Checks whether the current SDK path is in a location that typically requires
	/// administrator privileges to write to (e.g., Program Files).
	/// </summary>
	public bool SdkPathRequiresElevation()
	{
		if (!PlatformDetector.IsWindows)
			return false;

		var sdkPath = _getSdkPath();
		if (string.IsNullOrEmpty(sdkPath))
			return false;

		var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

		return sdkPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
			|| sdkPath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase);
	}
}
