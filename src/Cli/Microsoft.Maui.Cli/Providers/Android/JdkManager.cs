// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Utils;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.Providers.Android;

/// <summary>
/// JDK detection and installation manager.
/// </summary>
public class JdkManager : IJdkManager
{
	/// <summary>
	/// The default JDK version installed by <c>maui android</c> commands when none is specified.
	/// This is the single source of truth; all callers (CLI options, interfaces, fakes) should
	/// reference this constant rather than hard-coding a version number.
	/// </summary>
	public const int DefaultJdkVersion = 21;

	/// <summary>
	/// JDK versions supported for automatic download and installation. Order matters:
	/// the first entry is the default. Used by <see cref="GetAvailableVersions"/> and
	/// <see cref="InstallAsync(int?, string?, CancellationToken)"/> validation.
	/// </summary>
	public static readonly IReadOnlyList<int> SupportedInstallVersions = new[] { DefaultJdkVersion, 17 };

	/// <summary>
	/// Minimum JDK major version accepted by health checks and fallback detection.
	/// Versions older than this are considered unsupported for .NET MAUI Android builds.
	/// </summary>
	const int MinJdkVersion = 11;
	const int DownloadBufferSize = 81920;

	static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

	public string? DetectedJdkPath { get; private set; }
	public int? DetectedJdkVersion { get; private set; }

	public bool IsInstalled => !string.IsNullOrEmpty(DetectedJdkPath);

	public JdkManager()
	{
		Detect();
	}

	void Detect()
	{
		DetectedJdkPath = PlatformDetector.Paths.GetJdkPath();
		if (DetectedJdkPath != null)
		{
			DetectedJdkVersion = GetJdkVersion(DetectedJdkPath);
			if (DetectedJdkVersion.HasValue)
				return;
		}

		// Fallback: use android-tools comprehensive JDK discovery
		// Searches Program Files, registry, known vendor paths, etc.
		try
		{
			// Detection accepts any JDK >= MinJdkVersion. We deliberately don't cap the
			// upper bound here: CheckHealthAsync only rejects versions below MinJdkVersion,
			// so a user who has JDK 22/23/etc. installed should be found and used rather
			// than ignored. Installation is separately gated by SupportedInstallVersions.
			var knownJdk = Xamarin.Android.Tools.JdkInfo.GetKnownSystemJdkInfos(logger: (_, _) => { })
				.Where(j => j.Version != null && j.Version.Major >= MinJdkVersion)
				.OrderByDescending(j => j.Version)
				.FirstOrDefault();

			if (knownJdk != null)
			{
				DetectedJdkPath = knownJdk.HomePath;
				DetectedJdkVersion = knownJdk.Version?.Major;
			}
		}
		catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"JDK auto-detection failed: {ex.Message}"); }
	}

	static int? GetJdkVersion(string jdkPath)
	{
		var javaBin = Path.Combine(jdkPath, "bin", "java");
		if (PlatformDetector.IsWindows)
			javaBin += ".exe";

		if (!File.Exists(javaBin))
			return null;

		try
		{
			var result = ProcessRunner.RunSync(javaBin, ["-version"], timeout: TimeSpan.FromSeconds(10));
			// Java version output is on stderr
			var output = result.StandardError + result.StandardOutput;

			// Parse version from output like: openjdk version "17.0.1" or java version "1.8.0_292"
			var match = System.Text.RegularExpressions.Regex.Match(output, @"version ""(\d+)");
			if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
			{
				return version;
			}
		}
		catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"JDK version detection failed for '{jdkPath}': {ex.Message}"); }

		return null;
	}

	public async Task<HealthCheck> CheckHealthAsync(CancellationToken cancellationToken = default)
	{
		Detect();

		if (!IsInstalled)
		{
			return new HealthCheck
			{
				Category = "android",
				Name = "JDK",
				Status = CheckStatus.Error,
				Message = "JDK not found",
				Fix = new FixInfo
				{
					IssueId = ErrorCodes.JdkNotFound,
					Description = $"Install OpenJDK {DefaultJdkVersion}",
					AutoFixable = true,
					Command = "maui android jdk install"
				}
			};
		}

		if (DetectedJdkVersion.HasValue)
		{
			if (DetectedJdkVersion < MinJdkVersion)
			{
				return new HealthCheck
				{
					Category = "android",
					Name = "JDK",
					Status = CheckStatus.Error,
					Message = $"JDK {DetectedJdkVersion} is too old (minimum: {MinJdkVersion})",
					Details = new JsonObject
					{
						["path"] = DetectedJdkPath!,
						["version"] = DetectedJdkVersion.Value
					},
					Fix = new FixInfo
					{
						IssueId = ErrorCodes.JdkVersionUnsupported,
						Description = $"Install OpenJDK {DefaultJdkVersion}",
						AutoFixable = true,
						Command = "maui android jdk install"
					}
				};
			}

			return new HealthCheck
			{
				Category = "android",
				Name = "JDK",
				Status = CheckStatus.Ok,
				Message = $"JDK {DetectedJdkVersion}",
				Details = new JsonObject
				{
					["path"] = DetectedJdkPath!,
					["version"] = DetectedJdkVersion.Value
				}
			};
		}

		return new HealthCheck
		{
			Category = "android",
			Name = "JDK",
			Status = CheckStatus.Warning,
			Message = "JDK found but version unknown",
			Details = new JsonObject
			{
				["path"] = DetectedJdkPath!
			}
		};
	}

	public async Task InstallAsync(int? version = null, string? installPath = null,
		CancellationToken cancellationToken = default)
	{
		await InstallAsync(version, installPath, onProgress: null, cancellationToken);
	}

	/// <summary>
	/// Installs JDK with structured progress reporting for rich UI rendering.
	/// </summary>
	public async Task InstallAsync(int? version, string? installPath,
		Action<double, string>? onProgress, CancellationToken cancellationToken = default)
	{
		var resolvedVersion = version ?? DefaultJdkVersion;

		if (!SupportedInstallVersions.Contains(resolvedVersion))
		{
			throw new MauiToolException(
				ErrorCodes.JdkVersionUnsupported,
				$"JDK version {resolvedVersion} is not available for installation. " +
				$"Supported versions: {string.Join(", ", SupportedInstallVersions)}");
		}

		var targetPath = installPath ?? PlatformDetector.Paths.DefaultJdkPath;
		Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

		var downloadUrl = GetDownloadUrl(resolvedVersion);
		var tempArchivePath = Path.Combine(Path.GetTempPath(), $"openjdk-{resolvedVersion}.tar.gz");

		try
		{
			// Download with progress
			using var response = await s_httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();

			var totalBytes = response.Content.Headers.ContentLength ?? 0;

			await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
			await using (var fs = File.Create(tempArchivePath))
			{
				var buffer = new byte[DownloadBufferSize];
				long totalRead = 0;
				int bytesRead;

				while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
				{
					await fs.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
					totalRead += bytesRead;

					if (totalBytes > 0)
					{
						var pct = (double)totalRead / totalBytes * 100;
						onProgress?.Invoke(pct, $"Downloaded {totalRead / (1024 * 1024)} MB / {totalBytes / (1024 * 1024)} MB");
					}
				}
			}

			onProgress?.Invoke(100, "Extracting...");

			// Extract
			await ExtractArchiveAsync(tempArchivePath, targetPath, cancellationToken);

			// Update detected path
			DetectedJdkPath = targetPath;
			DetectedJdkVersion = resolvedVersion;
		}
		catch (Exception ex) when (ex is not MauiToolException)
		{
			throw new MauiToolException(
				ErrorCodes.JdkInstallFailed,
				$"Failed to install JDK: {ex.Message}",
				nativeError: ex.Message);
		}
		finally
		{
			if (File.Exists(tempArchivePath))
				File.Delete(tempArchivePath);
		}
	}

	static string GetDownloadUrl(int version)
	{
		// Use Eclipse Temurin (Adoptium) builds
		var os = PlatformDetector.IsMacOS ? "mac" : PlatformDetector.IsWindows ? "windows" : "linux";
		var arch = PlatformDetector.IsArm64 ? "aarch64" : "x64";
		var ext = PlatformDetector.IsWindows ? "zip" : "tar.gz";

		// Latest LTS versions from Adoptium
		return version switch
		{
			17 => $"https://api.adoptium.net/v3/binary/latest/17/ga/{os}/{arch}/jdk/hotspot/normal/eclipse?project=jdk",
			21 => $"https://api.adoptium.net/v3/binary/latest/21/ga/{os}/{arch}/jdk/hotspot/normal/eclipse?project=jdk",
			_ => throw new MauiToolException(ErrorCodes.JdkVersionUnsupported, $"JDK version {version} is not available for automatic download")
		};
	}

	/// <summary>
	/// Validates that the install path is not a dangerous system or user directory.
	/// Prevents accidental recursive deletion of critical directories.
	/// </summary>
	internal static void ValidateInstallPath(string path)
	{
		var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		// Block home directory
		var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (string.Equals(fullPath, homePath, StringComparison.OrdinalIgnoreCase))
			throw new MauiToolException(ErrorCodes.JdkInstallFailed,
				$"Refusing to use home directory as install path: {path}");

		// Block root and well-known system directories
		string[] dangerousPaths = ["/", "/usr", "/bin", "/etc", "/var", "/tmp", "/opt", "/home",
			"C:\\", "C:\\Windows", "C:\\Users", "C:\\Program Files", "C:\\Program Files (x86)"];

		foreach (var dangerous in dangerousPaths)
		{
			var normalizedDangerous = dangerous.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (string.Equals(fullPath, normalizedDangerous, StringComparison.OrdinalIgnoreCase))
				throw new MauiToolException(ErrorCodes.JdkInstallFailed,
					$"Refusing to use system directory as install path: {path}");
		}

		// Path must be at least 3 segments deep to prevent broad deletions
		var segments = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
			StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length < 3)
			throw new MauiToolException(ErrorCodes.JdkInstallFailed,
				$"Install path is too shallow (requires at least 3 directory levels): {path}");
	}

	async Task ExtractArchiveAsync(string archivePath, string targetPath, CancellationToken cancellationToken)
	{
		ValidateInstallPath(targetPath);

		if (Directory.Exists(targetPath))
			Directory.Delete(targetPath, recursive: true);
		Directory.CreateDirectory(targetPath);

		if (PlatformDetector.IsWindows)
		{
			// Use PowerShell for zip extraction
			System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, targetPath);
		}
		else
		{
			// Use tar for tar.gz extraction
			var result = await ProcessRunner.RunAsync(
				"tar",
				["-xzf", archivePath, "-C", targetPath, "--strip-components=1"],
				timeout: TimeSpan.FromMinutes(5),
				cancellationToken: cancellationToken);

			if (!result.Success)
			{
				throw new MauiToolException(
					ErrorCodes.JdkInstallFailed,
					$"Failed to extract JDK: {result.StandardError}",
					nativeError: result.StandardError);
			}
		}

		// On macOS, the JDK is inside Contents/Home
		if (PlatformDetector.IsMacOS)
		{
			var contentsHome = Path.Combine(targetPath, "Contents", "Home");
			if (Directory.Exists(contentsHome))
			{
				// Move contents up
				var tempDir = Path.Combine(Path.GetTempPath(), $"jdk-temp-{Guid.NewGuid()}");
				Directory.Move(contentsHome, tempDir);
				Directory.Delete(targetPath, recursive: true);
				Directory.Move(tempDir, targetPath);
			}
		}
	}

	public IEnumerable<int> GetAvailableVersions() => SupportedInstallVersions;
}
