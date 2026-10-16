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

	[Fact]
	public void WindowsScript_QuotesInputsAndWaitsBeforeInvokingSdk()
	{
		var script = UpdateCommand.WindowsScript(123, @"C:\SDK's folder\dotnet.exe",
			UpdateCommand.Arguments(true), @"C:\project's folder", @"C:\log's folder\update.log");
		Assert.Contains("'C:\\SDK''s folder\\dotnet.exe'", script);
		Assert.Contains("'C:\\project''s folder'", script);
		Assert.Contains("WaitForExit(60000)", script);
		Assert.Contains("SDK_EXIT=$code", script);
		Assert.Contains("*>> $log", script);
		Assert.DoesNotContain(" *> $log", script);
		Assert.True(script.IndexOf("WaitForExit", StringComparison.Ordinal) < script.IndexOf("& '", StringComparison.Ordinal));
		Assert.DoesNotContain("[Console]", script);
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
			var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
				"WindowsPowerShell", "v1.0", "powershell.exe");
			var script = UpdateCommand.WindowsScript(int.MaxValue, powershell,
				["-NoProfile", "-NonInteractive", "-Command", $"Write-Output 'SDK output'; exit {sdkExit}"],
				directory, log, interactive);
			var start = new ProcessStartInfo(powershell)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
				Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
				start.ArgumentList.Add(arg);
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

			Assert.Equal(sdkExit, process.ExitCode);
			var terminal = await stdout + await stderr;
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
