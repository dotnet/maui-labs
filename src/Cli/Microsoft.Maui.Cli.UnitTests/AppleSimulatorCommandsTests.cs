// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class AppleSimulatorCommandsTests
{
	[Fact]
	public void SimulatorCommand_HasCreateSubcommand()
	{
		var root = Program.BuildRootCommand();
		var apple = root.Subcommands.FirstOrDefault(c => c.Name == "apple");
		var simulator = apple?.Subcommands.FirstOrDefault(c => c.Name == "simulator");
		Assert.NotNull(simulator);
		Assert.Contains(simulator!.Subcommands, c => c.Name == "create");
	}

	[Fact]
	public void SimulatorCommand_HasEraseSubcommand()
	{
		var root = Program.BuildRootCommand();
		var apple = root.Subcommands.FirstOrDefault(c => c.Name == "apple");
		var simulator = apple?.Subcommands.FirstOrDefault(c => c.Name == "simulator");
		Assert.NotNull(simulator);
		Assert.Contains(simulator!.Subcommands, c => c.Name == "erase");
	}

	[Fact]
	public void CreateCommand_HasDeviceTypeArgument()
	{
		var root = Program.BuildRootCommand();
		var createCmd = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator").Subcommands
			.First(c => c.Name == "create");
		Assert.Contains(createCmd.Arguments, a => a.Name == "device-type");
	}

	[Fact]
	public void CreateCommand_HasNameAndRuntimeOptions()
	{
		var root = Program.BuildRootCommand();
		var createCmd = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator").Subcommands
			.First(c => c.Name == "create");
		Assert.Contains(createCmd.Options, o => o.Name == "--name");
		Assert.Contains(createCmd.Options, o => o.Name == "--runtime");
	}

	[Fact]
	public void EraseCommand_HasNameOrUdidArgument()
	{
		var root = Program.BuildRootCommand();
		var eraseCmd = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator").Subcommands
			.First(c => c.Name == "erase");
		Assert.Contains(eraseCmd.Arguments, a => a.Name == "name-or-udid");
	}

	[Fact]
	public void FakeAppleProvider_CreateSimulator_TracksCall()
	{
		var fake = new FakeAppleProvider { CreateSimulatorResult = "test-udid-1234" };
		var udid = fake.CreateSimulator("My iPhone 15", "com.apple.CoreSimulator.SimDeviceType.iPhone-15", "com.apple.CoreSimulator.SimRuntime.iOS-17-2");
		Assert.Equal("test-udid-1234", udid);
		Assert.Single(fake.CreatedSimulators);
		Assert.Equal(("My iPhone 15", "com.apple.CoreSimulator.SimDeviceType.iPhone-15", "com.apple.CoreSimulator.SimRuntime.iOS-17-2"), fake.CreatedSimulators[0]);
	}

	[Fact]
	public void FakeAppleProvider_CreateSimulator_ReturnsNull_WhenResultIsNull()
	{
		var fake = new FakeAppleProvider { CreateSimulatorResult = null };
		var udid = fake.CreateSimulator("Ghost", "com.apple.CoreSimulator.SimDeviceType.iPhone-15");
		Assert.Null(udid);
	}

	[Fact]
	public void FakeAppleProvider_EraseSimulator_TracksCall()
	{
		var fake = new FakeAppleProvider { EraseSimulatorResult = true };
		var result = fake.EraseSimulator("ABC-DEF-123");
		Assert.True(result);
		Assert.Single(fake.ErasedSimulators);
		Assert.Equal("ABC-DEF-123", fake.ErasedSimulators[0]);
	}

	[Fact]
	public void FakeAppleProvider_EraseSimulator_ReturnsFalse_WhenConfigured()
	{
		var fake = new FakeAppleProvider { EraseSimulatorResult = false };
		var result = fake.EraseSimulator("nonexistent");
		Assert.False(result);
	}

	[Fact]
	public void SimulatorCreateResult_SerializesToSnakeCase()
	{
		var model = new SimulatorCreateResult
		{
			Udid = "AABBCCDD-1234-5678-ABCD-000000000001",
			Name = "iPhone 15",
			DeviceType = "com.apple.CoreSimulator.SimDeviceType.iPhone-15",
			Runtime = "com.apple.CoreSimulator.SimRuntime.iOS-17-2"
		};
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorCreateResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("AABBCCDD-1234-5678-ABCD-000000000001", root.GetProperty("udid").GetString());
		Assert.Equal("iPhone 15", root.GetProperty("name").GetString());
		Assert.Equal("com.apple.CoreSimulator.SimDeviceType.iPhone-15", root.GetProperty("device_type").GetString());
		Assert.Equal("com.apple.CoreSimulator.SimRuntime.iOS-17-2", root.GetProperty("runtime").GetString());
	}

	[Fact]
	public void SimulatorCreateResult_OmitsNullRuntime()
	{
		var model = new SimulatorCreateResult { Udid = "AABBCCDD-1234", Name = "iPhone 15", DeviceType = "com.apple.CoreSimulator.SimDeviceType.iPhone-15" };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorCreateResult);
		using var doc = JsonDocument.Parse(json);
		Assert.False(doc.RootElement.TryGetProperty("runtime", out _));
	}

	[Fact]
	public void SimulatorEraseResult_SerializesToSnakeCase()
	{
		var model = new SimulatorEraseResult { Target = "My iPhone 15", Erased = true };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorEraseResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("My iPhone 15", root.GetProperty("target").GetString());
		Assert.True(root.GetProperty("erased").GetBoolean());
	}

	[Fact]
	public void SimulatorCreateFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorCreateFailed, "Create failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2207", error.Code);
		Assert.Equal("platform", error.Category);
	}

	[Fact]
	public void SimulatorEraseFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorEraseFailed, "Erase failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2208", error.Code);
		Assert.Equal("platform", error.Category);
	}

	// --- Install/Uninstall/Launch/Terminate/GetAppContainer command tests ---

	[Fact]
	public void SimulatorCommand_HasInstallSubcommand()
	{
		var root = Program.BuildRootCommand();
		var simulator = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator");
		Assert.Contains(simulator.Subcommands, c => c.Name == "install");
	}

	[Fact]
	public void SimulatorCommand_HasUninstallSubcommand()
	{
		var root = Program.BuildRootCommand();
		var simulator = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator");
		Assert.Contains(simulator.Subcommands, c => c.Name == "uninstall");
	}

	[Fact]
	public void SimulatorCommand_HasLaunchSubcommand()
	{
		var root = Program.BuildRootCommand();
		var simulator = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator");
		Assert.Contains(simulator.Subcommands, c => c.Name == "launch");
	}

	[Fact]
	public void SimulatorCommand_HasTerminateSubcommand()
	{
		var root = Program.BuildRootCommand();
		var simulator = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator");
		Assert.Contains(simulator.Subcommands, c => c.Name == "terminate");
	}

	[Fact]
	public void SimulatorCommand_HasGetAppContainerSubcommand()
	{
		var root = Program.BuildRootCommand();
		var simulator = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator");
		Assert.Contains(simulator.Subcommands, c => c.Name == "get-app-container");
	}

	[Fact]
	public void FakeAppleProvider_InstallApp_TracksCall()
	{
		var fake = new FakeAppleProvider { InstallAppResult = true };
		var result = fake.InstallApp("UDID-123", "/path/to/MyApp.app");
		Assert.True(result);
		Assert.Single(fake.InstalledApps);
		Assert.Equal(("UDID-123", "/path/to/MyApp.app"), fake.InstalledApps[0]);
	}

	[Fact]
	public void FakeAppleProvider_UninstallApp_TracksCall()
	{
		var fake = new FakeAppleProvider { UninstallAppResult = true };
		var result = fake.UninstallApp("UDID-123", "com.example.myapp");
		Assert.True(result);
		Assert.Single(fake.UninstalledApps);
		Assert.Equal(("UDID-123", "com.example.myapp"), fake.UninstalledApps[0]);
	}

	[Fact]
	public void FakeAppleProvider_LaunchApp_TracksCallWithArgs()
	{
		var fake = new FakeAppleProvider { LaunchAppResult = true };
		var result = fake.LaunchApp("UDID-123", "com.example.myapp", "--debug", "--wait");
		Assert.True(result);
		Assert.Single(fake.LaunchedApps);
		Assert.Equal("UDID-123", fake.LaunchedApps[0].Udid);
		Assert.Equal("com.example.myapp", fake.LaunchedApps[0].BundleId);
		Assert.Equal(new[] { "--debug", "--wait" }, fake.LaunchedApps[0].Args);
	}

	[Fact]
	public void FakeAppleProvider_TerminateApp_TracksCall()
	{
		var fake = new FakeAppleProvider { TerminateAppResult = true };
		var result = fake.TerminateApp("UDID-123", "com.example.myapp");
		Assert.True(result);
		Assert.Single(fake.TerminatedApps);
		Assert.Equal(("UDID-123", "com.example.myapp"), fake.TerminatedApps[0]);
	}

	[Fact]
	public void FakeAppleProvider_GetAppContainer_TracksCallAndReturnsPath()
	{
		var fake = new FakeAppleProvider { GetAppContainerResult = "/data/Containers/Data/Application/UUID" };
		var path = fake.GetAppContainer("UDID-123", "com.example.myapp", "data");
		Assert.Equal("/data/Containers/Data/Application/UUID", path);
		Assert.Single(fake.GetAppContainerCalls);
		Assert.Equal(("UDID-123", "com.example.myapp", "data"), fake.GetAppContainerCalls[0]);
	}

	[Fact]
	public void SimulatorAppResult_SerializesToSnakeCase_WithBundleIdentifier()
	{
		var model = new SimulatorAppResult { Udid = "UDID-AAA", BundleIdentifier = "com.example.app", Action = "launched", Success = true };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorAppResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("UDID-AAA", root.GetProperty("udid").GetString());
		Assert.Equal("com.example.app", root.GetProperty("bundle_identifier").GetString());
		Assert.Equal("launched", root.GetProperty("action").GetString());
		Assert.True(root.GetProperty("success").GetBoolean());
		Assert.False(root.TryGetProperty("app_path", out _));
	}

	[Fact]
	public void SimulatorAppResult_SerializesToSnakeCase_WithAppPath()
	{
		var model = new SimulatorAppResult { Udid = "UDID-BBB", AppPath = "/path/to/MyApp.app", Action = "installed", Success = true };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorAppResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("UDID-BBB", root.GetProperty("udid").GetString());
		Assert.Equal("/path/to/MyApp.app", root.GetProperty("app_path").GetString());
		Assert.Equal("installed", root.GetProperty("action").GetString());
		Assert.True(root.GetProperty("success").GetBoolean());
		Assert.False(root.TryGetProperty("bundle_identifier", out _));
	}

	[Fact]
	public void SimulatorAppContainerResult_SerializesToSnakeCase()
	{
		var model = new SimulatorAppContainerResult { Udid = "UDID-CCC", BundleIdentifier = "com.test.app", Path = "/data/path" };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorAppContainerResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("UDID-CCC", root.GetProperty("udid").GetString());
		Assert.Equal("com.test.app", root.GetProperty("bundle_identifier").GetString());
		Assert.Equal("/data/path", root.GetProperty("path").GetString());
	}

	[Fact]
	public void SimulatorInstallFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorInstallFailed, "Install failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2209", error.Code);
		Assert.Equal("platform", error.Category);
	}

	[Fact]
	public void SimulatorUninstallFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorUninstallFailed, "Uninstall failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2210", error.Code);
		Assert.Equal("platform", error.Category);
	}

	[Fact]
	public void SimulatorLaunchFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorLaunchFailed, "Launch failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2211", error.Code);
		Assert.Equal("platform", error.Category);
	}

	[Fact]
	public void SimulatorTerminateFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorTerminateFailed, "Terminate failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2212", error.Code);
		Assert.Equal("platform", error.Category);
	}

	[Fact]
	public void SimulatorGetAppContainerFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorGetContainerFailed, "GetAppContainer failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2213", error.Code);
		Assert.Equal("platform", error.Category);
	}

	// --- Handler-level tests (require macOS, exercise CLI argument parsing) ---

	static async Task<(int ExitCode, string StdOut, string StdErr, FakeAppleProvider Fake)> InvokeSimulatorCommandAsync(
		Action<FakeAppleProvider>? configure = null,
		params string[] args)
	{
		var fake = new FakeAppleProvider();
		configure?.Invoke(fake);

		var testProvider = ServiceConfiguration.CreateTestServiceProvider(appleProvider: fake);
		var originalServices = Program.Services;
		var stdOut = new StringWriter();
		var stdErr = new StringWriter();
		var originalOut = Console.Out;
		var originalErr = Console.Error;
		try
		{
			Program.Services = testProvider;
			Console.SetOut(stdOut);
			Console.SetError(stdErr);

			var rootCommand = Program.BuildRootCommand();
			var parseResult = rootCommand.Parse(args);
			var exitCode = await parseResult.InvokeAsync();
			return (exitCode, stdOut.ToString(), stdErr.ToString(), fake);
		}
		finally
		{
			Console.SetOut(originalOut);
			Console.SetError(originalErr);
			Program.ResetServices();
		}
	}

	[Fact]
	public async Task LaunchCommand_ForwardsArgsToProvider()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 15", Udid = "AAAA-BBBB", IsAvailable = true });
				f.LaunchAppResult = true;
			},
			"apple", "simulator", "launch", "AAAA-BBBB", "com.test.app", "--args", "--debug", "--wait-for-debugger", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.LaunchedApps);
		Assert.Equal("AAAA-BBBB", fake.LaunchedApps[0].Udid);
		Assert.Equal("com.test.app", fake.LaunchedApps[0].BundleId);
		Assert.Equal(new[] { "--debug", "--wait-for-debugger" }, fake.LaunchedApps[0].Args);
	}

	[Fact]
	public async Task InstallCommand_InvalidUdid_ReturnsSimulatorNotFound()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		// Create a temporary .app directory so we pass path validation and hit the UDID check
		var tempApp = Path.Combine(Path.GetTempPath(), $"FakeTest_{Guid.NewGuid():N}.app");
		Directory.CreateDirectory(tempApp);
		try
		{
			var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
				f =>
				{
					// No simulators — any UDID will be "not found"
					f.InstallAppResult = true;
				},
				"apple", "simulator", "install", "BAD-UDID", tempApp, "--json");

			Assert.Equal(1, exitCode);
			Assert.Contains("E2204", stdout);
			Assert.Empty(fake.InstalledApps); // never reached the provider
		}
		finally
		{
			Directory.Delete(tempApp, recursive: true);
		}
	}

	[Fact]
	public async Task UninstallCommand_ValidUdid_CallsProvider()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-1234", IsAvailable = true });
				f.UninstallAppResult = true;
			},
			"apple", "simulator", "uninstall", "SIM-1234", "com.example.myapp", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.UninstalledApps);
		Assert.Equal(("SIM-1234", "com.example.myapp"), fake.UninstalledApps[0]);
		Assert.Contains("uninstalled", stdout);
	}

	[Fact]
	public async Task TerminateCommand_ValidUdid_CallsProvider()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-TERM", IsAvailable = true });
				f.TerminateAppResult = true;
			},
			"apple", "simulator", "terminate", "SIM-TERM", "com.example.running", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.TerminatedApps);
		Assert.Equal(("SIM-TERM", "com.example.running"), fake.TerminatedApps[0]);
		Assert.Contains("terminated", stdout);
	}

	[Fact]
	public async Task GetAppContainerCommand_ValidUdid_ReturnsPath()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-CONT", IsAvailable = true });
				f.GetAppContainerResult = "/Users/test/Library/Developer/CoreSimulator/Devices/SIM-CONT/data/Containers/Bundle/Application/ABC/MyApp.app";
			},
			"apple", "simulator", "get-app-container", "SIM-CONT", "com.example.myapp", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.GetAppContainerCalls);
		Assert.Equal(("SIM-CONT", "com.example.myapp", (string?)null), fake.GetAppContainerCalls[0]);
		Assert.Contains("MyApp.app", stdout);
	}

	[Fact]
	public async Task GetAppContainerCommand_UnavailableSimulator_ReturnsError()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				// Simulator exists but IsAvailable = false (runtime deleted)
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 12", Udid = "OLD-SIM", IsAvailable = false });
				f.GetAppContainerResult = "/some/path";
			},
			"apple", "simulator", "get-app-container", "OLD-SIM", "com.example.app", "--json");

		Assert.Equal(1, exitCode);
		Assert.Contains("E2214", stdout);
		Assert.Contains("unavailable", stdout);
		Assert.Empty(fake.GetAppContainerCalls); // never reached the provider
	}
}
