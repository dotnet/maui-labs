// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
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
public partial class SdkManager : IDisposable
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
		await InstallPackagesAsync(packages, acceptLicenses, (Action<AndroidPackageInstallProgress>?)null, cancellationToken);
	}

	public async Task InstallPackagesAsync(IEnumerable<string> packages, bool acceptLicenses,
		Action<string, int, int>? onProgress, CancellationToken cancellationToken = default)
	{
		// Adapt the coarse (package, index, total) callback onto the richer streaming overload.
		// The streaming overload fires many times per package (once per progress line); collapse
		// those back to a single call per package so this legacy callback keeps its original
		// one-call-per-package contract (the seed update is the first event for each package).
		var lastIndex = 0;
		Action<AndroidPackageInstallProgress>? adapter = onProgress is null
			? null
			: p =>
			{
				if (p.PackageIndex == lastIndex)
					return;
				lastIndex = p.PackageIndex;
				onProgress(p.Package, p.PackageIndex, p.PackageTotal);
			};

		await InstallPackagesAsync(packages, acceptLicenses, adapter, cancellationToken);
	}

	public async Task InstallPackagesAsync(IEnumerable<string> packages, bool acceptLicenses,
		Action<AndroidPackageInstallProgress>? onProgress, CancellationToken cancellationToken = default)
	{
		var (sdkPath, jdkPath) = SyncPaths();
		EnsureAvailable();

		var packageList = packages.ToList();

		try
		{
			// No progress requested: install everything in one buffered upstream call.
			if (onProgress is null)
			{
				await _sdkManager.InstallAsync(packageList, acceptLicenses, cancellationToken);
				return;
			}

			// Reuse the already-synced sdkPath to resolve the sdkmanager path once (avoids the
			// extra SyncPaths() call the SdkManagerPath property would trigger). EnsureAvailable()
			// above guarantees this resolves to a non-null path.
			var sdkManagerPath = ResolveSdkManagerPath(sdkPath) ?? _sdkManager.FindSdkManagerPath()!;

			// Install one package at a time so we can report per-package progress. Installing
			// individually also keeps the streamed percentage meaningful (it resets per package).
			for (var i = 0; i < packageList.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var package = packageList[i];
				var index = i + 1;

				// Seed an initial update so the UI immediately reflects which package is starting,
				// before sdkmanager prints its first progress line.
				onProgress(new AndroidPackageInstallProgress(package, index, packageList.Count, string.Empty, -1));

				// Live streaming needs stdin to auto-accept per-package license prompts; without
				// auto-accept a prompt would deadlock behind the redirected stdout. When we can't
				// auto-accept, fall back to the buffered upstream install for this package — the
				// seed callback above still gives coarse per-package progress.
				if (acceptLicenses)
				{
					await RunSdkManagerInstallAsync(
						sdkManagerPath, package, acceptLicenses, sdkPath, jdkPath,
						(phase, percent) => onProgress(
							new AndroidPackageInstallProgress(package, index, packageList.Count, phase, percent)),
						cancellationToken);
				}
				else
				{
					await _sdkManager.InstallAsync(new[] { package }, acceptLicenses, cancellationToken);
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

	/// <summary>
	/// Runs <c>sdkmanager &lt;package&gt;</c> directly (instead of going through the upstream wrapper,
	/// which buffers stdout) so we can stream live download/extraction progress to <paramref name="onProgress"/>.
	/// When <paramref name="acceptLicenses"/> is true, writes <c>y</c> to stdin every 500ms to mirror
	/// the upstream auto-accept loop and keep the install non-interactive.
	/// </summary>
	async Task RunSdkManagerInstallAsync(string sdkManagerPath, string package, bool acceptLicenses,
		string? sdkPath, string? jdkPath, Action<string, int> onProgress, CancellationToken cancellationToken)
	{
		var psi = new ProcessStartInfo
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = acceptLicenses,
		};

		// On Windows, sdkmanager resolves to a .bat wrapper. Executing a .bat with
		// UseShellExecute=false is not universally reliable (it is not a PE image), so run it
		// through cmd.exe /c. Other platforms exec the shell script directly.
		if (OperatingSystem.IsWindows() && sdkManagerPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
		{
			psi.FileName = "cmd.exe";
			psi.ArgumentList.Add("/c");
			psi.ArgumentList.Add(sdkManagerPath);
		}
		else
		{
			psi.FileName = sdkManagerPath;
		}

		psi.ArgumentList.Add(package);

		// The upstream wrapper passes --sdk_root explicitly; mirror that so the install targets the
		// resolved SDK even when ANDROID_HOME/ANDROID_SDK_ROOT aren't set in the ambient environment.
		if (!string.IsNullOrEmpty(sdkPath))
			psi.ArgumentList.Add($"--sdk_root={sdkPath}");

		foreach (var kvp in AndroidEnvironment.BuildEnvironmentVariables(sdkPath, jdkPath))
			psi.Environment[kvp.Key] = kvp.Value;

		using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
		var stderr = new System.Text.StringBuilder();

		if (!process.Start())
			throw new InvalidOperationException($"Failed to start sdkmanager for package '{package}'.");

		// Stops the auto-accept loop deterministically once the process exits or the caller cancels.
		// Linked to the caller's token so cancellation also tears the loop down.
		using var stopAccept = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		using var registration = cancellationToken.Register(() =>
		{
			try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
			catch { /* Best-effort: process may have already exited. */ }
		});

		// Auto-accept any per-package license prompt without blocking on stdin.
		Task acceptTask = Task.CompletedTask;
		if (acceptLicenses)
		{
			acceptTask = Task.Run(async () =>
			{
				try
				{
					while (!process.HasExited && !stopAccept.IsCancellationRequested)
					{
						await process.StandardInput.WriteLineAsync("y");
						await process.StandardInput.FlushAsync();
						await Task.Delay(500, stopAccept.Token);
					}
				}
				catch { /* Process exited, stdin closed, or stop requested; nothing more to accept. */ }
			}, stopAccept.Token);
		}

		var stdoutTask = ReadProgressStreamAsync(process.StandardOutput, onProgress, cancellationToken);
		var stderrTask = ReadIntoBufferAsync(process.StandardError, stderr, cancellationToken);

		try
		{
			// Race the process exit against the stdout reader. If onProgress throws, stdoutTask
			// faults and stops draining stdout; sdkmanager would then block once the OS pipe buffer
			// fills, hanging WaitForExitAsync forever. Detecting the faulted reader lets us kill the
			// process so the fault surfaces instead of deadlocking.
			var exitTask = process.WaitForExitAsync(cancellationToken);
			var finished = await Task.WhenAny(exitTask, stdoutTask);
			if (finished == stdoutTask && stdoutTask.IsFaulted)
			{
				try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
				catch { /* Best-effort: process may have already exited. */ }
			}
			await SafeAwait(exitTask);
		}
		finally
		{
			// Always stop the auto-accept loop and drain the reader tasks before the process is
			// disposed, so no task is left observing a disposed stream (avoids UnobservedTaskException).
			stopAccept.Cancel();
			await SafeAwait(acceptTask);
			await SafeAwait(stdoutTask);
			await SafeAwait(stderrTask);
		}

		// A kill triggered by cancellation makes WaitForExitAsync complete normally with a non-zero
		// exit code; surface that as cancellation rather than a spurious install failure.
		cancellationToken.ThrowIfCancellationRequested();

		// On a clean (non-cancelled) exit, a faulted stdout reader means the progress callback threw
		// (e.g. the live renderer was disposed). Surface it instead of reporting a false success.
		if (stdoutTask.IsFaulted)
		{
			var fault = stdoutTask.Exception?.InnerException ?? stdoutTask.Exception;
			if (fault is not null and not OperationCanceledException)
				ExceptionDispatchInfo.Capture(fault).Throw();
		}

		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"Package installation ({package}) failed with exit code {process.ExitCode}: {stderr.ToString().Trim()}");
		}
	}

	/// <summary>
	/// Awaits a task while swallowing cancellation and teardown faults from the streaming/stdin
	/// helpers. The exit code and the caller's cancellation token are the source of truth for the
	/// install result, so faults from drained background reads must not mask them.
	/// </summary>
	static async Task SafeAwait(Task task)
	{
		try { await task; }
		catch { /* Background read/stdin task faulted during teardown; ignore. */ }
	}

	/// <summary>
	/// Reads sdkmanager stdout line by line and reports parsed progress. <see cref="TextReader.ReadLineAsync()"/>
	/// treats a lone carriage return as a line terminator, so the in-place <c>[====] NN%</c> updates
	/// sdkmanager emits each surface as their own line.
	/// </summary>
	static async Task ReadProgressStreamAsync(StreamReader reader, Action<string, int> onProgress, CancellationToken cancellationToken)
	{
		string? line;
		while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
		{
			if (TryParseInstallProgressLine(line, out var phase, out var percent))
				onProgress(phase, percent);
		}
	}

	static async Task ReadIntoBufferAsync(StreamReader reader, System.Text.StringBuilder buffer, CancellationToken cancellationToken)
	{
		var chunk = new char[4096];
		int count;
		while ((count = await reader.ReadAsync(chunk, cancellationToken)) > 0)
			buffer.Append(chunk, 0, count);
	}

	static readonly (string Keyword, string Canonical)[] KnownPhases =	{
		("Downloading", "Downloading"),
		("Unzipping", "Unzipping"),
		("Installing", "Installing"),
		("Fetching", "Fetching"),
		("Verifying", "Verifying"),
		("Preparing", "Preparing"),
		("Computing", "Computing updates"),
	};

	/// <summary>
	/// Parses a single line of <c>sdkmanager</c> stdout into a (phase, percent) pair.
	/// Tolerant of progress-bar prefixes (<c>[====   ]</c>), carriage-return in-place updates,
	/// phase-only lines (no percentage), and lines that carry no progress at all.
	/// Returns <see langword="false"/> when the line carries neither a percentage nor a known phase.
	/// </summary>
	internal static bool TryParseInstallProgressLine(string? line, out string phase, out int percent)
	{
		phase = string.Empty;
		percent = -1;

		if (string.IsNullOrWhiteSpace(line))
			return false;

		// Defensive handling for direct callers (e.g. unit tests) that may pass a raw '\r'-joined
		// buffer: take the last in-place segment. The streaming path never reaches here with a '\r'
		// because ReadProgressStreamAsync already splits on it via ReadLineAsync.
		var trimmed = line.Replace('\r', '\n');
		var lastNewline = trimmed.LastIndexOf('\n');
		if (lastNewline >= 0)
			trimmed = trimmed[(lastNewline + 1)..];
		trimmed = trimmed.Trim();

		if (trimmed.Length == 0)
			return false;

		var match = PercentRegex().Match(trimmed);
		if (match.Success)
		{
			if (int.TryParse(match.Groups[1].Value, out var p))
				percent = Math.Clamp(p, 0, 100);

			var after = trimmed[(match.Index + match.Length)..].Trim();
			phase = ExtractPhase(after);
			return true;
		}

		// No percentage — only treat as progress if it names a known phase (e.g. "Downloading foo.zip").
		var phaseOnly = ExtractPhase(trimmed);
		if (phaseOnly.Length > 0)
		{
			phase = phaseOnly;
			return true;
		}

		return false;
	}

	[GeneratedRegex(@"(\d{1,3})\s*%")]
	private static partial Regex PercentRegex();

	static string ExtractPhase(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return string.Empty;

		foreach (var (keyword, canonical) in KnownPhases)
		{
			if (text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
				return canonical;
		}

		return string.Empty;
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

	/// <summary>
	/// Resolves the installed command-line tools (sdkmanager path + revision) from the highest
	/// <c>cmdline-tools</c> revision reported by <c>source.properties</c>. Returns
	/// <see langword="null"/> when no SDK path is configured or no sdkmanager is installed.
	/// </summary>
	public Xamarin.Android.Tools.CommandLineTool? FindCommandLineTools()
	{
		SyncPaths();
		return _sdkManager.FindSdkManager();
	}

	/// <summary>
	/// Ensures the Android SDK at <paramref name="targetPath"/> contains the latest
	/// <c>cmdline-tools;latest</c> package, bootstrapping and/or updating from the Google
	/// catalog as needed. Returns the resolved sdkmanager executable and installed revision.
	/// </summary>
	public async Task<Xamarin.Android.Tools.CommandLineTool> EnsureLatestCommandLineToolsAsync(
		string targetPath,
		Action<Xamarin.Android.Tools.SdkBootstrapPhase, int, string>? onProgress = null,
		CancellationToken cancellationToken = default)
	{
		var bootstrapProgress = new Progress<Xamarin.Android.Tools.SdkBootstrapProgress>(p =>
			onProgress?.Invoke(p.Phase, p.PercentComplete, p.Message));
		return await _sdkManager.EnsureLatestCommandLineToolsAsync(targetPath, bootstrapProgress, cancellationToken);
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
	/// Checks whether the current SDK path requires administrator privileges to write to.
	/// When the path already exists, the check uses actual filesystem writability so that the
	/// result matches what callers (e.g., IDE extensions) observe when they probe the directory
	/// themselves.  When the path does not yet exist (e.g., before the SDK is installed), the
	/// check falls back to a Program Files prefix heuristic to predict whether creating the
	/// directory will require elevation.
	/// </summary>
	public bool SdkPathRequiresElevation()
	{
		if (!PlatformDetector.IsWindows)
			return false;

		var sdkPath = _getSdkPath();
		if (string.IsNullOrEmpty(sdkPath))
			return false;

		// If the directory exists, probe it directly rather than guessing from the path.
		// This makes the CLI's decision consistent with callers that check actual write access.
		if (Directory.Exists(sdkPath))
			return !CanWriteToDirectory(sdkPath);

		// Directory does not exist yet — fall back to Program Files prefix heuristic so we can
		// predict whether creating it will require elevation.
		var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

		return sdkPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
			|| sdkPath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Attempts to create a probe file inside <paramref name="path"/> to determine whether the
	/// current process has write access to that directory. The probe file is opened with
	/// <see cref="FileOptions.DeleteOnClose"/> so it is always removed, even if the process
	/// crashes or an AV scanner holds a handle.
	/// Returns <see langword="true"/> if the probe succeeds. Returns <see langword="false"/>
	/// when access is denied or the directory no longer exists
	/// (<see cref="UnauthorizedAccessException"/> or <see cref="DirectoryNotFoundException"/>).
	/// Returns <see langword="true"/> for other <see cref="IOException"/>s so transient I/O
	/// failures (e.g., on network shares) are not treated as evidence that elevation is required.
	/// </summary>
	internal static bool CanWriteToDirectory(string path)
	{
		var probe = Path.Combine(path, Path.GetRandomFileName());
		try
		{
			using var fs = new FileStream(
				probe,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				bufferSize: 1,
				FileOptions.DeleteOnClose);
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		catch (DirectoryNotFoundException)
		{
			// Directory was deleted between our Exists check and the probe — treat as
			// non-writable so the caller doesn't assume it can install here.
			return false;
		}
		catch (IOException)
		{
			// Treat transient I/O problems (e.g., network paths) as writable so we do not
			// report a false "elevation required" when the real issue is unrelated to permissions.
			return true;
		}
	}
}
