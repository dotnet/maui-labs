// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AppleCommandsTests
{
	[Fact]
	public void InstallCommand_Exists()
	{
		var appleCommand = AppleCommands.Create();
		Assert.Contains(appleCommand.Subcommands, c => c.Name == "install");
	}

	[Fact]
	public void InstallCommand_HasPlatformOption()
	{
		var appleCommand = AppleCommands.Create();
		var installCommand = appleCommand.Subcommands.First(c => c.Name == "install");

		Assert.Contains(installCommand.Options, o => o.Name == "--platform");
	}

	[Fact]
	public void InstallCommand_DoesNotDeclareOwnDryRunOption()
	{
		// Regression: install should use GlobalOptions.DryRunOption, not a local --dry-run
		var appleCommand = AppleCommands.Create();
		var installCommand = appleCommand.Subcommands.First(c => c.Name == "install");

		Assert.DoesNotContain(installCommand.Options, o => o.Name == "--dry-run");
	}

	[Fact]
	public void InstallCommand_ParsesPlatformOption()
	{
		var appleCommand = AppleCommands.Create();
		var installCommand = appleCommand.Subcommands.First(c => c.Name == "install");
		var platformOption = (Option<string[]>)installCommand.Options.First(o => o.Name == "--platform");

		var parseResult = installCommand.Parse("install --platform iOS --platform tvOS");

		Assert.Empty(parseResult.Errors);
		var platforms = parseResult.GetValue(platformOption);
		Assert.NotNull(platforms);
		Assert.Equal(2, platforms.Length);
		Assert.Contains("iOS", platforms);
		Assert.Contains("tvOS", platforms);
	}

	// --- Handler-level tests ---

	static async Task<(int ExitCode, FakeAppleProvider Apple)> InvokeAppleInstallAsync(
		Action<FakeAppleProvider>? configure = null,
		params string[] extraArgs)
	{
		var fakeApple = new FakeAppleProvider();
		configure?.Invoke(fakeApple);

		var testProvider = ServiceConfiguration.CreateTestServiceProvider(appleProvider: fakeApple);
		var originalServices = Program.Services;
		try
		{
			Program.Services = testProvider;

			var rootCommand = Program.BuildRootCommand();
			var args = new List<string> { "apple", "install", "--json" };
			args.AddRange(extraArgs);
			var parseResult = rootCommand.Parse(args.ToArray());
			var exitCode = await parseResult.InvokeAsync();
			return (exitCode, fakeApple);
		}
		finally
		{
			Program.ResetServices();
		}
	}

	[Fact]
	public async Task InstallCommand_Json_CallsInstallEnvironmentAsync()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // Install handler requires macOS

		var (exitCode, fake) = await InvokeAppleInstallAsync(f =>
		{
			f.InstallResult = new AppleInstallResult
			{
				Status = "ok",
				XcodeVersion = "16.0 (16A242d)",
				CommandLineToolsInstalled = true,
				Platforms = new List<string> { "iOS", "tvOS" },
				Runtimes = new List<string> { "iOS 18.0" },
				DryRun = false
			};
		});

		Assert.Equal(0, exitCode);
		Assert.Single(fake.InstallCalls);
		var (platforms, dryRun) = fake.InstallCalls[0];
		Assert.NotNull(platforms);
		Assert.Contains("iOS", platforms);
		Assert.False(dryRun);
	}

	[Fact]
	public async Task InstallCommand_Json_PassesPlatformFilter()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // Install handler requires macOS

		var (exitCode, fake) = await InvokeAppleInstallAsync(
			f => f.InstallResult = new AppleInstallResult { Status = "ok" },
			"--platform", "iOS");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.InstallCalls);
		var (platforms, _) = fake.InstallCalls[0];
		Assert.NotNull(platforms);
		Assert.Contains("iOS", platforms);
	}

	[Fact]
	public async Task InstallCommand_Json_PlatformAllPassesNullFilter()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // Install handler requires macOS

		var (exitCode, fake) = await InvokeAppleInstallAsync(
			f => f.InstallResult = new AppleInstallResult { Status = "ok" },
			"--platform", "all");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.InstallCalls);
		var (platforms, _) = fake.InstallCalls[0];
		Assert.Null(platforms); // "all" means no filter
	}

	[Fact]
	public async Task InstallCommand_Json_PassesDryRunFromGlobalOption()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // Install handler requires macOS

		var fakeApple = new FakeAppleProvider
		{
			InstallResult = new AppleInstallResult { Status = "ok", DryRun = true }
		};

		var testProvider = ServiceConfiguration.CreateTestServiceProvider(appleProvider: fakeApple);
		var originalServices = Program.Services;
		try
		{
			Program.Services = testProvider;

			var rootCommand = Program.BuildRootCommand();
			// --dry-run is a global option, placed before the subcommand path
			var parseResult = rootCommand.Parse(new[] { "--dry-run", "apple", "install", "--json" });
			await parseResult.InvokeAsync();

			Assert.Single(fakeApple.InstallCalls);
			var (_, dryRun) = fakeApple.InstallCalls[0];
			Assert.True(dryRun);
		}
		finally
		{
			Program.ResetServices();
		}
	}

	[Fact]
	public async Task InstallCommand_Json_ReturnsZeroForSkippedStatus()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // Install handler requires macOS

		// On non-macOS (or when installer is null), status is "skipped" — should not be a failure
		var (exitCode, _) = await InvokeAppleInstallAsync(f =>
		{
			f.InstallResult = new AppleInstallResult { Status = "skipped" };
		});

		Assert.Equal(0, exitCode);
	}

	[Fact]
	public async Task InstallCommand_Json_ReturnsOneForFailedStatus()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // Install handler requires macOS

		var (exitCode, _) = await InvokeAppleInstallAsync(f =>
		{
			f.InstallResult = new AppleInstallResult { Status = "failed" };
		});

		Assert.Equal(1, exitCode);
	}
}
