// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Output;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Utils;

internal static class DotnetPgoInstaller
{
	const string RuntimeRepoUrl = "https://github.com/dotnet/runtime.git";
	const string FallbackBranch = "release/10.0";
	const string BranchEnvironmentVariable = "MAUI_DOTNET_PGO_BRANCH";
	const string ProjectPath = "src/coreclr/tools/dotnet-pgo/dotnet-pgo.csproj";
	const int StatusTailLineCount = 5;
	const int StatusMaxLineLength = 120;
	static readonly TimeSpan s_buildTimeout = TimeSpan.FromMinutes(15);

	internal static string DisplayPath => GetDisplayPath();

	internal static async Task<string> EnsureAvailableAsync(
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		if (TryResolvePath() is { } installedPath)
			return installedPath;

		if (!useJson)
		{
			formatter.WriteInfo($"dotnet-pgo was not found at {DisplayPath}.");
			formatter.WriteInfo("Building dotnet-pgo from dotnet/runtime source...");
		}

		return await BuildFromSourceAsync(formatter, useJson, verbose, cancellationToken);
	}

	internal static string? TryResolvePath()
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			return null;

		var expectedPath = GetExecutablePath(userProfile);
		if (File.Exists(expectedPath))
			return expectedPath;

		var legacyPath = GetInstallPath(userProfile);
		if (!string.Equals(expectedPath, legacyPath, StringComparison.OrdinalIgnoreCase) && File.Exists(legacyPath))
			return legacyPath;

		return null;
	}

	internal static string ResolvePathOrThrow()
	{
		if (TryResolvePath() is { } installedPath)
			return installedPath;

		throw MauiToolException.UserActionRequired(
			ErrorCodes.DiagnosticsToolNotFound,
			$"MIBC conversion requires dotnet-pgo at '{DisplayPath}'.",
			[
				$"Install or copy the dotnet-pgo binary to {DisplayPath}.",
				"Then rerun 'maui profile startup --format mibc'."
			]);
	}

	internal static string GetInstallPath(string userProfile)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);
		return Path.Combine(userProfile, ".maui", "dotnet-pgo");
	}

	internal static string GetExecutablePath(string userProfile)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);
		return Path.Combine(userProfile, ".maui", GetExecutableFileName());
	}

	internal static string[] BuildPublishArguments(string runtimeIdentifier, string outputDirectory) =>
	[
		"publish",
		ProjectPath,
		"-c", "Release",
		"-r", runtimeIdentifier,
		"--self-contained",
		"-p:UseAppHost=true",
		"-p:PublishSingleFile=true",
		"-p:PublishTrimmed=false",
		"-p:TreatWarningsAsErrors=false",
		"-o", outputDirectory
	];

	internal static string? ParseLatestStableReleaseBranch(string? lsRemoteOutput)
	{
		if (string.IsNullOrWhiteSpace(lsRemoteOutput))
			return null;

		return lsRemoteOutput
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault())
			.Where(static token => !string.IsNullOrWhiteSpace(token))
			.Select(static token => Regex.Match(token!, @"(?:refs/heads/)?(release/\d+\.\d+)$"))
			.Where(static match => match.Success)
			.Select(static match =>
			{
				var branch = match.Groups[1].Value;
				var versionText = branch["release/".Length..];
				return Version.TryParse(versionText, out var version)
					? new { Branch = branch, Version = version }
					: null;
			})
			.Where(static candidate => candidate is not null)
			.OrderByDescending(static candidate => candidate!.Version)
			.Select(static candidate => candidate!.Branch)
			.FirstOrDefault();
	}

	static async Task<string> ResolveSourceBranchAsync(
		string gitPath,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var overrideBranch = Environment.GetEnvironmentVariable(BranchEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(overrideBranch))
		{
			WriteVerbose(formatter, useJson, verbose, $"Using dotnet-pgo source branch override from {BranchEnvironmentVariable}: {overrideBranch}");
			return overrideBranch.Trim();
		}

		var lsRemoteResult = await ProcessRunner.RunAsync(
			gitPath,
			["ls-remote", "--heads", RuntimeRepoUrl, "refs/heads/release/*"],
			timeout: TimeSpan.FromSeconds(30),
			cancellationToken: cancellationToken);

		if (lsRemoteResult.Success &&
			ParseLatestStableReleaseBranch(lsRemoteResult.StandardOutput) is { } latestStableBranch)
		{
			WriteVerbose(formatter, useJson, verbose, $"Selected latest stable dotnet/runtime release branch for dotnet-pgo: {latestStableBranch}");
			return latestStableBranch;
		}

		if (!useJson)
			formatter.WriteWarning($"Could not detect the latest stable dotnet/runtime release branch automatically. Falling back to {FallbackBranch}.");

		WriteVerbose(
			formatter,
			useJson,
			verbose,
			$"Falling back to {FallbackBranch} because git ls-remote failed or returned no stable release branches. Output: {lsRemoteResult.StandardOutput} {lsRemoteResult.StandardError}".Trim());

		return FallbackBranch;
	}

	static async Task<string> BuildFromSourceAsync(
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
		{
			throw new MauiToolException(
				ErrorCodes.DiagnosticsToolNotFound,
				"Could not resolve the current user's home directory to locate or build dotnet-pgo.");
		}

		var gitPath = ProcessRunner.GetCommandPath("git");
		if (gitPath is null)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DiagnosticsToolNotFound,
				"Building dotnet-pgo from source requires 'git', but it was not found in PATH.",
				[
					"Install git and rerun 'maui profile startup --format mibc'.",
					$"Or manually place the dotnet-pgo binary at {DisplayPath}."
				]);
		}

		var cloneDirectory = Path.Combine(Path.GetTempPath(), $"dotnet-runtime-{Guid.NewGuid():N}");
		var dotnetRoot = Path.Combine(cloneDirectory, ".dotnet");
		var publishDirectory = Path.Combine(cloneDirectory, "artifacts", "dotnet-pgo");
		var installPath = GetExecutablePath(userProfile);
		var runtimeBranch = await ResolveSourceBranchAsync(gitPath, formatter, useJson, verbose, cancellationToken);

		try
		{
			WriteVerbose(formatter, useJson, verbose, $"Cloning {RuntimeRepoUrl} ({runtimeBranch}) into {cloneDirectory}.");
			var cloneResult = await RunWithOptionalStatusAsync(
				formatter,
				useJson,
				$"Cloning dotnet/runtime ({runtimeBranch})...",
				reportLine => ProcessRunner.RunAsync(
					gitPath,
					["clone", "--depth", "1", "--single-branch", "--branch", runtimeBranch, RuntimeRepoUrl, cloneDirectory],
					timeout: s_buildTimeout,
					onOutputData: reportLine,
					onErrorData: reportLine,
					cancellationToken: cancellationToken));
			if (!cloneResult.Success)
				throw CreateProcessFailureException("git clone", cloneResult);

			var sdkVersion = GetSdkVersion(cloneDirectory);

			var bootstrapResult = await RunWithOptionalStatusAsync(
				formatter,
				useJson,
				$"Bootstrapping .NET SDK {sdkVersion}...",
				async reportLine =>
				{
					var installScriptPath = await DownloadInstallScriptAsync(cloneDirectory, cancellationToken);
					var (bootstrapCommand, bootstrapArgs) = GetDotnetInstallCommand(installScriptPath, dotnetRoot, sdkVersion);
					WriteVerbose(formatter, useJson, verbose, $"Bootstrapping .NET SDK {sdkVersion} via {FormatCommandLine(bootstrapCommand, bootstrapArgs)}");
					return await ProcessRunner.RunAsync(
						bootstrapCommand,
						bootstrapArgs,
						cloneDirectory,
						timeout: s_buildTimeout,
						onOutputData: reportLine,
						onErrorData: reportLine,
						cancellationToken: cancellationToken);
				});
			if (!bootstrapResult.Success)
				throw CreateProcessFailureException("dotnet-install", bootstrapResult);

			var bootstrapDotnetPath = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
			if (!File.Exists(bootstrapDotnetPath))
			{
				throw new MauiToolException(
					ErrorCodes.DiagnosticsToolNotFound,
					$"dotnet SDK bootstrap completed, but '{bootstrapDotnetPath}' was not found.");
			}

			var runtimeIdentifier = GetCurrentRuntimeIdentifier();
			var publishArgs = BuildPublishArguments(runtimeIdentifier, publishDirectory);
			WriteVerbose(formatter, useJson, verbose, $"Publishing dotnet-pgo via {FormatCommandLine(bootstrapDotnetPath, publishArgs)}");

			var publishResult = await RunWithOptionalStatusAsync(
				formatter,
				useJson,
				$"Publishing dotnet-pgo for {runtimeIdentifier}...",
				reportLine => ProcessRunner.RunAsync(
					bootstrapDotnetPath,
					publishArgs,
					cloneDirectory,
					environmentVariables: new Dictionary<string, string>
					{
						["NUGET_PACKAGES"] = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(cloneDirectory, ".packages")
					},
					timeout: s_buildTimeout,
					onOutputData: reportLine,
					onErrorData: reportLine,
					cancellationToken: cancellationToken));
			if (!publishResult.Success)
				throw CreateProcessFailureException("dotnet publish", publishResult);

			var publishedExecutable = Path.Combine(publishDirectory, OperatingSystem.IsWindows() ? "dotnet-pgo.exe" : "dotnet-pgo");
			if (!File.Exists(publishedExecutable))
			{
				throw new MauiToolException(
					ErrorCodes.DiagnosticsToolNotFound,
					$"dotnet-pgo publish completed, but '{publishedExecutable}' was not produced.");
			}

			Directory.CreateDirectory(Path.GetDirectoryName(installPath) ?? Path.Combine(userProfile, ".maui"));
			File.Copy(publishedExecutable, installPath, overwrite: true);

			if (!OperatingSystem.IsWindows())
				File.SetUnixFileMode(installPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

			if (!useJson)
				formatter.WriteInfo($"dotnet-pgo installed to {DisplayPath}.");

			return installPath;
		}
		catch (MauiToolException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DiagnosticsToolNotFound,
				"Automatic dotnet-pgo source build failed.",
				[
					$"Retry after ensuring git and outbound network access are available, or set {BranchEnvironmentVariable} to a different dotnet/runtime branch.",
					$"As a fallback, manually place the dotnet-pgo binary at {DisplayPath}."
				],
				nativeError: ex.Message);
		}
		finally
		{
			TryDeleteDirectory(cloneDirectory);
		}
	}

	static async Task<T> RunWithOptionalStatusAsync<T>(
		IOutputFormatter formatter,
		bool useJson,
		string message,
		Func<Action<string>?, Task<T>> operation)
	{
		if (formatter is SpectreOutputFormatter spectre && !useJson)
		{
			var recentLines = new Queue<string>();
			var syncLock = new object();
			var lastRefreshUtc = DateTime.MinValue;

			return await spectre.StatusAsync(message, async updateStatus =>
			{
				void ReportLine(string line)
				{
					string? updatedStatus = null;

					lock (syncLock)
					{
						if (!AppendStatusTailLine(recentLines, line))
							return;

						var now = DateTime.UtcNow;
						if (now - lastRefreshUtc < TimeSpan.FromMilliseconds(75))
							return;

						lastRefreshUtc = now;
						updatedStatus = FormatStatusMessage(message, recentLines);
					}

					if (updatedStatus is not null)
						updateStatus(updatedStatus);
				}

				var result = await operation(ReportLine);

				lock (syncLock)
					updateStatus(FormatStatusMessage(message, recentLines));

				return result;
			});
		}

		if (!useJson)
			formatter.WriteInfo(message);

		return await operation(null);
	}

	internal static bool AppendStatusTailLine(Queue<string> recentLines, string? line, int maxLines = StatusTailLineCount)
	{
		ArgumentNullException.ThrowIfNull(recentLines);

		if (string.IsNullOrWhiteSpace(line))
			return false;

		var normalizedLine = Regex.Replace(line.Trim(), @"\s+", " ");
		if (normalizedLine.Length > StatusMaxLineLength)
			normalizedLine = normalizedLine[..(StatusMaxLineLength - 3)] + "...";

		recentLines.Enqueue(normalizedLine);
		while (recentLines.Count > maxLines)
			recentLines.Dequeue();

		return true;
	}

	internal static string FormatStatusMessage(string message, IEnumerable<string> recentLines)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		var lines = recentLines
			.Where(static line => !string.IsNullOrWhiteSpace(line))
			.ToArray();

		if (lines.Length == 0)
			return Markup.Escape(message);

		var builder = new StringBuilder(Markup.Escape(message));
		foreach (var line in lines)
		{
			builder.AppendLine();
			builder.Append("[grey]");
			builder.Append(Markup.Escape($"  {line}"));
			builder.Append("[/]");
		}

		return builder.ToString();
	}

	static string GetSdkVersion(string cloneDirectory)
	{
		var globalJsonPath = Path.Combine(cloneDirectory, "global.json");
		if (!File.Exists(globalJsonPath))
		{
			throw new MauiToolException(
				ErrorCodes.DiagnosticsToolNotFound,
				$"The cloned dotnet/runtime repository did not contain '{globalJsonPath}'.");
		}

		try
		{
			using var document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
			if (document.RootElement.TryGetProperty("sdk", out var sdkElement) &&
				sdkElement.TryGetProperty("version", out var versionElement) &&
				!string.IsNullOrWhiteSpace(versionElement.GetString()))
			{
				return versionElement.GetString()!;
			}
		}
		catch (Exception ex) when (ex is IOException or JsonException)
		{
			throw new MauiToolException(
				ErrorCodes.DiagnosticsToolNotFound,
				$"Could not determine the dotnet/runtime SDK version from '{globalJsonPath}'.",
				nativeError: ex.Message);
		}

		throw new MauiToolException(
			ErrorCodes.DiagnosticsToolNotFound,
			$"Could not determine the dotnet/runtime SDK version from '{globalJsonPath}'.");
	}

	static async Task<string> DownloadInstallScriptAsync(string cloneDirectory, CancellationToken cancellationToken)
	{
		var scriptUrl = OperatingSystem.IsWindows()
			? "https://dot.net/v1/dotnet-install.ps1"
			: "https://dot.net/v1/dotnet-install.sh";
		var scriptPath = Path.Combine(cloneDirectory, OperatingSystem.IsWindows() ? "dotnet-install.ps1" : "dotnet-install.sh");

		using var httpClient = new HttpClient();
		using var response = await httpClient.GetAsync(scriptUrl, cancellationToken);
		response.EnsureSuccessStatusCode();

		await using (var fileStream = File.Create(scriptPath))
		{
			await response.Content.CopyToAsync(fileStream, cancellationToken);
			await fileStream.FlushAsync(cancellationToken);
		}

		return scriptPath;
	}

	static (string FileName, string[] Args) GetDotnetInstallCommand(string scriptPath, string installDirectory, string version)
	{
		if (OperatingSystem.IsWindows())
		{
			var powershellPath = ProcessRunner.GetCommandPath("pwsh") ?? ProcessRunner.GetCommandPath("powershell");
			if (powershellPath is null)
			{
				throw MauiToolException.UserActionRequired(
					ErrorCodes.DiagnosticsToolNotFound,
					"Building dotnet-pgo from source requires PowerShell on Windows, but it was not found.",
					[
						"Install PowerShell and rerun the command.",
						$"Or manually place the dotnet-pgo binary at {DisplayPath}."
					]);
			}

			return (powershellPath, ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-InstallDir", installDirectory, "-Version", version]);
		}

		return ("bash", [scriptPath, "--install-dir", installDirectory, "--version", version]);
	}

	internal static string GetCurrentRuntimeIdentifier()
	{
		var architecture = RuntimeInformation.OSArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => throw new MauiToolException(ErrorCodes.DiagnosticsToolNotFound, $"Unsupported architecture '{RuntimeInformation.OSArchitecture}' for dotnet-pgo source builds.")
		};

		var operatingSystem = OperatingSystem.IsMacOS()
			? "osx"
			: OperatingSystem.IsLinux()
				? "linux"
				: OperatingSystem.IsWindows()
					? "win"
					: throw new MauiToolException(ErrorCodes.DiagnosticsToolNotFound, "Unsupported operating system for dotnet-pgo source builds.");

		return $"{operatingSystem}-{architecture}";
	}

	static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);
		}
		catch (Exception ex)
		{
			Trace.WriteLine($"Temporary dotnet-pgo source-build cleanup failed for '{path}': {ex.Message}");
		}
	}

	static void WriteVerbose(IOutputFormatter formatter, bool useJson, bool verbose, string message)
	{
		if (verbose && !useJson)
			formatter.WriteProgress($"[debug] {message}");
	}

	static string FormatCommandLine(string fileName, IEnumerable<string> arguments) =>
		string.Join(" ", [QuoteForDisplay(fileName), .. arguments.Select(QuoteForDisplay)]);

	static string QuoteForDisplay(string value) =>
		value.Any(ch => char.IsWhiteSpace(ch) || ch is '"' or '\'')
			? $"\"{value.Replace("\"", "\\\"")}\""
			: value;

	static string GetDisplayPath()
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return string.IsNullOrWhiteSpace(userProfile)
			? Path.Combine(".maui", GetExecutableFileName())
			: GetExecutablePath(userProfile);
	}

	static string GetExecutableFileName()
		=> OperatingSystem.IsWindows() ? "dotnet-pgo.exe" : "dotnet-pgo";

	static Exception CreateProcessFailureException(string commandName, ProcessResult result)
	{
		var details = string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput.Trim()
			: result.StandardError.Trim();

		return new MauiToolException(
			ErrorCodes.InternalError,
			$"{commandName} failed with exit code {result.ExitCode}.",
			nativeError: details);
	}
}
