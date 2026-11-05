// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Output;
using System.Text.Json;
using System.CommandLine;
using System.Diagnostics;
using System.Text;
using Spectre.Console.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class UpdateCommandTests
{
	[Theory]
	[InlineData(false, 4)]
	[InlineData(true, 5)]
	public void Arguments_OnlyUpdatesGlobalPackage(bool prerelease, int count)
	{
		var args = UpdateCommand.Arguments(prerelease);
		Assert.Equal(count, args.Length);
		Assert.Equal(["tool", "update", "--global", "Microsoft.Maui.Cli"], args.Take(4));
		Assert.Equal(prerelease, args.Contains("--prerelease"));
		Assert.DoesNotContain("--allow-downgrade", args);
	}

	[Fact]
	public void IsGlobalInstall_RequiresMatchingShimAndPackage()
	{
		var home = Path.Combine(Path.GetTempPath(), "cli home", ".dotnet", "tools");
		var shim = Path.Combine(home, OperatingSystem.IsWindows() ? "maui.exe" : "maui");
		var payload = Path.Combine(home, ".store", "microsoft.maui.cli", "1.0.0", "tools", "net10.0", "any");
		Assert.True(UpdateCommand.IsGlobalInstall(shim, payload, home));
		Assert.False(UpdateCommand.IsGlobalInstall(null, payload, home));
		Assert.False(UpdateCommand.IsGlobalInstall(Path.Combine(home, "other"), payload, home));
		Assert.False(UpdateCommand.IsGlobalInstall(shim, payload.Replace("microsoft.maui.cli", "microsoft.maui.cli.other"), home));
		Assert.False(UpdateCommand.IsGlobalInstall(shim, Path.GetTempPath(), home));
	}

	static readonly string[] WindowsInputValues =
	[
		"normal", "two words", "ordinary's apostrophe",
		"\u2018; Write-Output INJECTED; #", "\u2019; Write-Output INJECTED; #",
		"\u201a; Write-Output INJECTED; #", "\u201b; Write-Output INJECTED; #",
		"semicolon;value", "back`tick", "$(Write-Output INJECTED)", "line one\r\nline two"
	];

	const string WindowsPathValue = "two words ' \u2018\u2019\u201a\u201b ; ` $(Write-Output INJECTED)";

	[Fact]
	public void WindowsUpdateStartInfo_KeepsInputsOutOfScript()
	{
		var dotnet = $@"C:\SDK {WindowsPathValue}\dotnet.exe";
		var directory = $@"C:\project {WindowsPathValue}";
		var log = $@"C:\logs {WindowsPathValue}\update.log";
		var start = UpdateCommand.WindowsUpdateStartInfo(123, dotnet, WindowsInputValues, directory, log);
		var script = Encoding.Unicode.GetString(Convert.FromBase64String(start.ArgumentList[^1]));

		Assert.Equal(UpdateCommand.WindowsScript, script);
		Assert.Equal(["-NoProfile", "-NonInteractive", "-EncodedCommand"], start.ArgumentList.Take(3));
		Assert.False(start.UseShellExecute);
		Assert.True(start.CreateNoWindow);
		Assert.Equal("123", start.Environment["MAUI_CLI_UPDATE_PARENT_ID"]);
		Assert.Equal(dotnet, start.Environment["MAUI_CLI_UPDATE_DOTNET"]);
		Assert.Equal(directory, start.Environment["MAUI_CLI_UPDATE_WORKING_DIRECTORY"]);
		Assert.Equal(log, start.Environment["MAUI_CLI_UPDATE_LOG"]);
		Assert.Equal("0", start.Environment["MAUI_CLI_UPDATE_SHOW_COMPLETION"]);
		Assert.Equal(WindowsInputValues, JsonSerializer.Deserialize<string[]>(start.Environment["MAUI_CLI_UPDATE_ARGUMENTS"]!));
	}

	[Fact]
	public async Task WindowsScript_InputDataIsNotInterpreted()
	{
		if (!OperatingSystem.IsWindows())
			return;

		var root = Path.Combine(Path.GetTempPath(), $"maui-update-{Guid.NewGuid():N}");
		var directory = Path.Combine(root, $"project {WindowsPathValue}");
		Directory.CreateDirectory(directory);
		try
		{
			var log = Path.Combine(root, $"log {WindowsPathValue}.txt");
			var capture = Path.Combine(root, "capture.json");
			var dotnet = $@"C:\SDK {WindowsPathValue}\dotnet.exe";
			var start = UpdateCommand.WindowsUpdateStartInfo(int.MaxValue, dotnet, WindowsInputValues, directory, log);
			start.Environment["MAUI_TEST_UPDATE_CAPTURE"] = capture;
			var script = Encoding.Unicode.GetString(Convert.FromBase64String(start.ArgumentList[^1]));
			const string probe = """
				$ErrorActionPreference = 'Stop'
				$ProgressPreference = 'SilentlyContinue'
				Set-Item -LiteralPath ('Function:\' + $env:MAUI_CLI_UPDATE_DOTNET) -Value {
				    $result = [pscustomobject]@{
				        Arguments = @($args)
				        WorkingDirectory = (Get-Location).Path
				        Command = $MyInvocation.MyCommand.Name
				        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
				    }
				    [IO.File]::WriteAllText($env:MAUI_TEST_UPDATE_CAPTURE, (ConvertTo-Json -InputObject $result -Depth 5))
				    Write-Output 'SDK output'
				    $global:LASTEXITCODE = 0
				}
				""";
			start.ArgumentList[^1] = Convert.ToBase64String(Encoding.Unicode.GetBytes(probe + Environment.NewLine + script));

			var (exitCode, terminal) = await RunWindowsUpdaterAsync(start);

			Assert.Equal(0, exitCode);
			Assert.Empty(terminal);
			using var json = JsonDocument.Parse(await File.ReadAllTextAsync(capture));
			var result = json.RootElement;
			Assert.Equal(WindowsInputValues, result.GetProperty("Arguments").EnumerateArray().Select(arg => arg.GetString()));
			Assert.Equal(directory, result.GetProperty("WorkingDirectory").GetString());
			Assert.Equal(dotnet, result.GetProperty("Command").GetString());
			Assert.StartsWith("5.1.", result.GetProperty("PowerShellVersion").GetString());
			var contents = await File.ReadAllTextAsync(log);
			Assert.Contains("SDK output", contents);
			Assert.Contains("SDK_EXIT=0", contents);
			Assert.DoesNotContain("INJECTED", contents);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public async Task WindowsScript_NativeProcessReceivesUnchangedArguments()
	{
		if (!OperatingSystem.IsWindows())
			return;

		var directory = Path.Combine(Path.GetTempPath(), $"maui-update-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			var capture = Path.Combine(directory, "capture.json");
			var probe = Path.Combine(directory, "sdk-probe.ps1");
			await File.WriteAllTextAsync(probe, """
				[IO.File]::WriteAllText($env:MAUI_TEST_UPDATE_CAPTURE, (ConvertTo-Json -InputObject @($args)))
				Write-Output 'SDK output'
				exit 0
				""");
			var start = UpdateCommand.WindowsUpdateStartInfo(int.MaxValue, WindowsPowerShellPath,
				["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", probe,
					.. WindowsInputValues],
				directory, Path.Combine(directory, "update.log"));
			start.Environment["MAUI_TEST_UPDATE_CAPTURE"] = capture;

			var (exitCode, terminal) = await RunWindowsUpdaterAsync(start);

			Assert.Equal(0, exitCode);
			Assert.Empty(terminal);
			Assert.Equal(WindowsInputValues,
				JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(capture)));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Theory]
	[InlineData(true, 0)]
	[InlineData(true, 7)]
	[InlineData(false, 0)]
	[InlineData(false, 7)]
	public async Task WindowsScript_CompletionMessagesRespectModeAndSdkOutcome(bool interactive, int sdkExit)
	{
		if (!OperatingSystem.IsWindows())
			return;

		var directory = Path.Combine(Path.GetTempPath(), $"maui-update-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			var log = Path.Combine(directory, "update.log");
			await File.WriteAllTextAsync(log, "Update header" + Environment.NewLine);
			var start = UpdateCommand.WindowsUpdateStartInfo(int.MaxValue, WindowsPowerShellPath,
				["-NoProfile", "-NonInteractive", "-Command", $"Write-Output 'SDK output'; exit {sdkExit}"],
				directory, log, interactive);
			Assert.Equal(!interactive, start.CreateNoWindow);
			Assert.Equal(interactive ? "1" : "0", start.Environment["MAUI_CLI_UPDATE_SHOW_COMPLETION"]);

			var (exitCode, terminal) = await RunWindowsUpdaterAsync(start);

			Assert.Equal(sdkExit, exitCode);
			if (interactive)
			{
				Assert.Contains("Updating MAUI CLI...", terminal);
				Assert.Contains(sdkExit == 0 ? "update completed" : "update failed", terminal);
				if (sdkExit != 0)
					Assert.Contains(log, terminal);
			}
			else
			{
				Assert.DoesNotContain("Updating MAUI CLI", terminal);
				Assert.DoesNotContain("update completed", terminal);
				Assert.DoesNotContain("update failed", terminal);
			}
			var contents = await File.ReadAllTextAsync(log);
			Assert.Contains("Update header", contents);
			Assert.Contains("SDK output", contents);
			Assert.Contains($"SDK_EXIT={sdkExit}", contents);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WindowsScript_WaitsForParentBeforeInvokingSdk(bool parentExited)
	{
		if (!OperatingSystem.IsWindows())
			return;

		var directory = Path.Combine(Path.GetTempPath(), $"maui-update-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			var log = Path.Combine(directory, "update.log");
			var start = UpdateCommand.WindowsUpdateStartInfo(123, WindowsPowerShellPath,
				["-NoProfile", "-NonInteractive", "-Command", "Write-Output 'SDK output'; exit 0"],
				directory, log);
			start.Environment["MAUI_TEST_PARENT_EXITED"] = parentExited ? "1" : "0";
			const string probe = """
				function Get-Process {
				    param($Id, $ErrorAction)
				    if ($Id -ne 123) { throw 'Incorrect parent process ID.' }
				    $parent = New-Object PSObject
				    Add-Member -InputObject $parent -MemberType ScriptMethod -Name WaitForExit -Value {
				        param($milliseconds)
				        if ($milliseconds -ne 60000) { throw 'Incorrect parent wait timeout.' }
				        return $env:MAUI_TEST_PARENT_EXITED -eq '1'
				    }
				    return $parent
				}
				""";
			var script = Encoding.Unicode.GetString(Convert.FromBase64String(start.ArgumentList[^1]));
			start.ArgumentList[^1] = Convert.ToBase64String(Encoding.Unicode.GetBytes(probe + Environment.NewLine + script));

			var (exitCode, terminal) = await RunWindowsUpdaterAsync(start);

			Assert.Equal(parentExited ? 0 : 1, exitCode);
			Assert.Empty(terminal);
			var contents = await File.ReadAllTextAsync(log);
			Assert.Contains(parentExited ? "SDK_EXIT=0" : "SDK_EXIT=1", contents);
			if (parentExited)
				Assert.Contains("SDK output", contents);
			else
			{
				Assert.Contains("Timed out waiting for MAUI CLI to exit.", contents);
				Assert.DoesNotContain("SDK output", contents);
			}
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	static string WindowsPowerShellPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
		"WindowsPowerShell", "v1.0", "powershell.exe");

	static async Task<(int ExitCode, string Terminal)> RunWindowsUpdaterAsync(ProcessStartInfo start)
	{
		start.RedirectStandardOutput = true;
		start.RedirectStandardError = true;
		start.CreateNoWindow = true;
		using var process = Process.Start(start)!;
		var stdout = process.StandardOutput.ReadToEndAsync();
		var stderr = process.StandardError.ReadToEndAsync();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			throw;
		}
		return (process.ExitCode, await stdout + await stderr);
	}

	[Fact]
	public void Create_ParsesPrereleaseAndRejectsUnknownFlags()
	{
		var command = UpdateCommand.Create();
		Assert.Empty(command.Parse(["--self", "--prerelease"]).Errors);
		Assert.Empty(command.Parse(["--self"]).Errors);
		Assert.NotEmpty(command.Parse(["--allow-downgrade"]).Errors);
	}

	[Theory]
	[InlineData("")]
	[InlineData("--prerelease")]
	[InlineData("--self false")]
	public void Create_WithoutExplicitSelf_RejectsInvocationWithGuidance(string arguments)
	{
		var result = UpdateCommand.Create().Parse(arguments);
		var error = Assert.Single(result.Errors);
		Assert.Contains("maui update --self", error.Message);
		Assert.Contains("does not update your MAUI project", error.Message);
	}

	[Fact]
	public void Result_StartedIsNotCompletedAndContainsLogPath()
	{
		var result = new CliUpdateResult("started", UpdateCommand.Arguments(false), "working directory", LogPath: "update.log");
		using var json = JsonDocument.Parse(JsonOutputFormatter.Serialize(result));
		Assert.Equal("started", json.RootElement.GetProperty("status").GetString());
		Assert.Equal("update.log", json.RootElement.GetProperty("log_path").GetString());
		Assert.False(json.RootElement.TryGetProperty("exit_code", out _));
	}

	[Fact]
	public async Task Invoke_DryRun_DisplaysSdkArgumentsAndWorkingDirectory()
	{
		var console = new TestConsole();
		console.Profile.Width = 2000;
		var root = CreateRoot(new SpectreOutputFormatter(console), () => true);

		var exitCode = await root.Parse(["update", "--self", "--prerelease", "--dry-run"]).InvokeAsync();

		Assert.Equal(0, exitCode);
		Assert.Contains("dotnet tool update --global Microsoft.Maui.Cli --prerelease", console.Output);
		Assert.Contains(Environment.CurrentDirectory, console.Output);
		Assert.DoesNotContain("System.String[]", console.Output);
	}

	[Fact]
	public async Task Invoke_JsonDryRun_PreservesStructuredResult()
	{
		using var writer = new StringWriter();
		var root = CreateRoot(new JsonOutputFormatter(writer), () => true);

		var exitCode = await root.Parse(["update", "--self", "--prerelease", "--dry-run", "--json"]).InvokeAsync();

		Assert.Equal(0, exitCode);
		using var json = JsonDocument.Parse(writer.ToString());
		Assert.Equal("dry_run", json.RootElement.GetProperty("status").GetString());
		Assert.Equal(UpdateCommand.Arguments(true),
			json.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()));
	}

	[Fact]
	public async Task Invoke_UnsupportedInstall_ReportsErrorBeforeUpdating()
	{
		using var writer = new StringWriter();
		var root = CreateRoot(new JsonOutputFormatter(writer), () => false);

		var exitCode = await root.Parse(["update", "--self", "--json"]).InvokeAsync();

		Assert.Equal(1, exitCode);
		Assert.Contains("default global .NET tool install", writer.ToString());
	}

	static RootCommand CreateRoot(IOutputFormatter formatter, Func<bool> isGlobalInstall) => new()
	{
		GlobalOptions.DryRunOption,
		GlobalOptions.JsonOption,
		UpdateCommand.Create(formatter, isGlobalInstall)
	};
}
