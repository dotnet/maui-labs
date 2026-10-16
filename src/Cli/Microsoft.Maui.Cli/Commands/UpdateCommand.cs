// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class UpdateCommand
{
	internal const string PackageId = "Microsoft.Maui.Cli";
	internal static string CurrentVersion => typeof(Program).Assembly
		.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

	internal static string ToolHome => Path.Combine(
		Environment.GetEnvironmentVariable("DOTNET_CLI_HOME")
			?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");

	internal static bool IsGlobalInstall(string? processPath, string baseDirectory, string toolHome)
	{
		if (processPath is null)
			return false;
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var shim = Path.Combine(toolHome, OperatingSystem.IsWindows() ? "maui.exe" : "maui");
		var store = Path.GetFullPath(Path.Combine(toolHome, ".store", PackageId.ToLowerInvariant())) + Path.DirectorySeparatorChar;
		return string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(shim), comparison)
			&& Path.GetFullPath(baseDirectory).StartsWith(store, comparison);
	}

	internal static string[] Arguments(bool prerelease) => prerelease
		? ["tool", "update", "--global", PackageId, "--prerelease"]
		: ["tool", "update", "--global", PackageId];

	public static Command Create() => Create(null, null);

	internal static Command Create(IOutputFormatter? output, Func<bool>? isGlobalInstall)
	{
		var self = new Option<bool>("--self") { Description = "Update the MAUI CLI itself, not a MAUI project." };
		var prerelease = new Option<bool>("--prerelease") { Description = "Include preview releases (automatic for preview CLI installs)." };
		var command = new Command("update", "Use --self to update the MAUI CLI. Project updates are not supported by this command.") { self, prerelease };
		command.Validators.Add(result =>
		{
			if (!result.GetValue(self))
				result.AddError("Specify 'maui update --self' to update the MAUI CLI. This command does not update your MAUI project.");
		});
		command.SetAction(async (parse, cancellationToken) =>
		{
			var formatter = output ?? Program.GetFormatter(parse);
			if (!(isGlobalInstall?.Invoke() ?? IsGlobalInstall(Environment.ProcessPath, AppContext.BaseDirectory, ToolHome)))
			{
				formatter.WriteError(new InvalidOperationException(
					"Self-update supports only the default global .NET tool install. Use the installer that owns this copy."));
				return 1;
			}

			var args = Arguments(parse.GetValue(prerelease) || CurrentVersion.Contains('-'));
			if (Program.IsDryRun(parse))
			{
				if (parse.GetValue(GlobalOptions.JsonOption))
					formatter.Write(new CliUpdateResult("dry_run", args, Environment.CurrentDirectory));
				else
				{
					formatter.WriteInfo($"Would run: dotnet {string.Join(" ", args)}");
					formatter.WriteInfo($"Working directory: {Environment.CurrentDirectory}");
				}
				return 0;
			}

			if (OperatingSystem.IsWindows())
			{
				cancellationToken.ThrowIfCancellationRequested();
				var showCompletion = CliUpdatePolicy.IsInteractive(
					parse.GetValue(GlobalOptions.JsonOption), Program.IsCiMode(parse),
					Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected,
					Environment.GetEnvironmentVariable);
				var log = StartWindowsUpdate(args, showCompletion);
				if (parse.GetValue(GlobalOptions.JsonOption))
					formatter.Write(new CliUpdateResult("started", args, Environment.CurrentDirectory, LogPath: log));
				else
					formatter.WriteInfo(showCompletion
						? $"Update started; keep this terminal open for the completion message. SDK output and final exit code: {log}"
						: $"Update started; it will run after this process exits. SDK output and final exit code: {log}");
				return 0;
			}

			var result = await ProcessRunner.RunAsync("dotnet", args, cancellationToken: cancellationToken);
			if (parse.GetValue(GlobalOptions.JsonOption))
				formatter.Write(new CliUpdateResult(result.Success ? "completed" : "failed", args,
					Environment.CurrentDirectory, result.ExitCode, result.StandardOutput, result.StandardError));
			else if (result.Success)
				formatter.WriteSuccess(result.StandardOutput.Trim());
			else
				formatter.WriteError(new InvalidOperationException(
					$"SDK update failed ({result.ExitCode}). {result.StandardError} {result.StandardOutput}"));
			return result.Success ? 0 : 1;
		});
		return command;
	}

	internal static string WindowsScript(int parentId, string dotnet, string[] args, string workingDirectory, string log,
		bool showCompletion = false)
	{
		static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
		return $$"""
			$ErrorActionPreference = 'Stop'
			$ProgressPreference = 'SilentlyContinue'
			$log = {{Quote(log)}}
			function Write-UpdateStatus([string] $message) {
			    {{(showCompletion ? """
			    try { [Console]::Error.WriteLine($message) }
			    catch [System.IO.IOException], [System.ObjectDisposedException] { Add-Content -LiteralPath $log -Value 'Terminal closed; see this log for update results.' }
			    """ : "")}}
			}
			try {
			    $parent = Get-Process -Id {{parentId}} -ErrorAction SilentlyContinue
			    if ($parent -and !$parent.WaitForExit(60000)) { throw 'Timed out waiting for MAUI CLI to exit.' }
			    Set-Location -LiteralPath {{Quote(workingDirectory)}}
			    Write-UpdateStatus 'Updating MAUI CLI...'
			    $ErrorActionPreference = 'Continue'
			    $global:LASTEXITCODE = 1
			    & {{Quote(dotnet)}} {{string.Join(" ", args.Select(Quote))}} *>> $log
			    $code = $LASTEXITCODE
			    $ErrorActionPreference = 'Stop'
			    Add-Content -LiteralPath $log -Value "SDK_EXIT=$code"
			    if ($code -eq 0) {
			        Write-UpdateStatus 'MAUI CLI update completed. Run maui version to see the installed version.'
			    } else {
			        Write-UpdateStatus "MAUI CLI update failed (SDK exit $code). See log: $log"
			    }
			    exit $code
			} catch {
			    Add-Content -LiteralPath $log -Value $_.Exception.Message
			    Add-Content -LiteralPath $log -Value 'SDK_EXIT=1'
			    Write-UpdateStatus "MAUI CLI update failed. See log: $log"
			    exit 1
			}
			""";
	}

	static string StartWindowsUpdate(string[] args, bool showCompletion)
	{
		var state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Maui", "updates");
		Directory.CreateDirectory(state);
		var log = Path.Combine(state, $"update-{Guid.NewGuid():N}.log");
		var dotnet = ProcessRunner.GetCommandPath("dotnet")
			?? throw new InvalidOperationException("The .NET SDK is required to update MAUI CLI. Install an SDK and retry.");
		var script = WindowsScript(Environment.ProcessId, dotnet, args, Environment.CurrentDirectory, log, showCompletion);
		var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
			"WindowsPowerShell", "v1.0", "powershell.exe"))
		{
			UseShellExecute = false,
			CreateNoWindow = !showCompletion
		};
		foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
			start.ArgumentList.Add(arg);
		File.WriteAllText(log, $"Update started by MAUI CLI {CurrentVersion}.{Environment.NewLine}");
		using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the SDK updater.");
		if (process.WaitForExit(200))
			throw new InvalidOperationException($"The SDK updater exited before accepting the update. See {log}");
		return log;
	}
}

internal sealed record CliUpdateResult(string Status, string[] Arguments, string WorkingDirectory,
	int? ExitCode = null, string? StandardOutput = null, string? StandardError = null, string? LogPath = null);
