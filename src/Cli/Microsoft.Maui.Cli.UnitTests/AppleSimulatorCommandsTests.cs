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

	// --- Simulator extras subcommand existence ---

	static Command Simulator()
	{
		var root = Program.BuildRootCommand();
		return root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator");
	}

	[Theory]
	[InlineData("privacy")]
	[InlineData("appearance")]
	[InlineData("status-bar")]
	[InlineData("openurl")]
	[InlineData("push")]
	[InlineData("location")]
	[InlineData("add-media")]
	[InlineData("screenshot")]
	[InlineData("record-video")]
	public void SimulatorCommand_HasExtrasSubcommand(string name)
	{
		Assert.Contains(Simulator().Subcommands, c => c.Name == name);
	}

	[Theory]
	[InlineData("grant")]
	[InlineData("revoke")]
	[InlineData("reset")]
	public void PrivacyCommand_HasActionSubcommand(string action)
	{
		var privacy = Simulator().Subcommands.First(c => c.Name == "privacy");
		Assert.Contains(privacy.Subcommands, c => c.Name == action);
	}

	[Fact]
	public void StatusBarCommand_HasOverrideAndClearSubcommands()
	{
		var statusBar = Simulator().Subcommands.First(c => c.Name == "status-bar");
		Assert.Contains(statusBar.Subcommands, c => c.Name == "override");
		Assert.Contains(statusBar.Subcommands, c => c.Name == "clear");
	}

	[Fact]
	public void AppearanceCommand_HasGetLightDarkSubcommands()
	{
		var appearance = Simulator().Subcommands.First(c => c.Name == "appearance");
		Assert.Contains(appearance.Subcommands, c => c.Name == "get");
		Assert.Contains(appearance.Subcommands, c => c.Name == "light");
		Assert.Contains(appearance.Subcommands, c => c.Name == "dark");
	}

	[Fact]
	public void LocationCommand_HasSetClearRunSubcommands()
	{
		var location = Simulator().Subcommands.First(c => c.Name == "location");
		Assert.Contains(location.Subcommands, c => c.Name == "set");
		Assert.Contains(location.Subcommands, c => c.Name == "clear");
		Assert.Contains(location.Subcommands, c => c.Name == "run");
	}

	[Fact]
	public void ScreenshotCommand_HasFormatOption()
	{
		var screenshot = Simulator().Subcommands.First(c => c.Name == "screenshot");
		Assert.Contains(screenshot.Options, o => o.Name == "--format");
	}

	[Fact]
	public void RecordVideoCommand_HasCodecOption()
	{
		var recordVideo = Simulator().Subcommands.First(c => c.Name == "record-video");
		Assert.Contains(recordVideo.Options, o => o.Name == "--codec");
	}

	// --- SimulatorEnumParsing ---

	[Theory]
	[InlineData("location-always", Xamarin.MacDev.PrivacyPermission.LocationAlways)]
	[InlineData("contacts-limited", Xamarin.MacDev.PrivacyPermission.ContactsLimited)]
	[InlineData("photos-add", Xamarin.MacDev.PrivacyPermission.PhotosAdd)]
	[InlineData("media-library", Xamarin.MacDev.PrivacyPermission.MediaLibrary)]
	[InlineData("Photos", Xamarin.MacDev.PrivacyPermission.Photos)]
	[InlineData("SIRI", Xamarin.MacDev.PrivacyPermission.Siri)]
	public void TryParsePrivacyPermission_ParsesKnownTokens(string token, Xamarin.MacDev.PrivacyPermission expected)
	{
		Assert.True(SimulatorEnumParsing.TryParsePrivacyPermission(token, out var permission));
		Assert.Equal(expected, permission);
	}

	[Fact]
	public void TryParsePrivacyPermission_RejectsUnknownToken()
	{
		Assert.False(SimulatorEnumParsing.TryParsePrivacyPermission("camera", out _));
	}

	[Theory]
	[InlineData("png", Xamarin.MacDev.ScreenshotFormat.Png)]
	[InlineData("jpg", Xamarin.MacDev.ScreenshotFormat.Jpeg)]
	[InlineData("JPEG", Xamarin.MacDev.ScreenshotFormat.Jpeg)]
	[InlineData("tiff", Xamarin.MacDev.ScreenshotFormat.Tiff)]
	public void TryParseScreenshotFormat_ParsesKnownTokens(string token, Xamarin.MacDev.ScreenshotFormat expected)
	{
		Assert.True(SimulatorEnumParsing.TryParseScreenshotFormat(token, out var format));
		Assert.Equal(expected, format);
	}

	[Theory]
	[InlineData("3g", Xamarin.MacDev.SimulatorDataNetwork.ThreeG)]
	[InlineData("lte-a", Xamarin.MacDev.SimulatorDataNetwork.LteA)]
	[InlineData("5g-uc", Xamarin.MacDev.SimulatorDataNetwork.FiveGUc)]
	[InlineData("wifi", Xamarin.MacDev.SimulatorDataNetwork.Wifi)]
	public void TryParseDataNetwork_ParsesKnownTokens(string token, Xamarin.MacDev.SimulatorDataNetwork expected)
	{
		Assert.True(SimulatorEnumParsing.TryParseDataNetwork(token, out var network));
		Assert.Equal(expected, network);
	}

	[Theory]
	[InlineData("h264", "h264")]
	[InlineData("H264", "h264")]
	[InlineData("hevc", "hevc")]
	[InlineData("HEVC", "hevc")]
	public void TryParseVideoCodec_ParsesKnownTokens(string token, string expected)
	{
		Assert.True(SimulatorEnumParsing.TryParseVideoCodec(token, out var codec));
		Assert.Equal(expected, codec);
	}

	[Theory]
	[InlineData("mp4")]
	[InlineData("gif")]
	[InlineData("fmp4")]
	[InlineData("invalid")]
	public void TryParseVideoCodec_RejectsUnsupportedValues(string token)
	{
		Assert.False(SimulatorEnumParsing.TryParseVideoCodec(token, out _));
	}

	// --- FakeAppleProvider tracking ---

	[Fact]
	public void FakeAppleProvider_SetPrivacy_TracksCall()
	{
		var fake = new FakeAppleProvider { SetPrivacyResult = true };
		var result = fake.SetPrivacy("grant", "SIM-1", Xamarin.MacDev.PrivacyPermission.Photos, "com.test.app");
		Assert.True(result);
		Assert.Single(fake.PrivacyCalls);
		Assert.Equal(("grant", "SIM-1", Xamarin.MacDev.PrivacyPermission.Photos, (string?)"com.test.app"), fake.PrivacyCalls[0]);
	}

	[Fact]
	public void FakeAppleProvider_Screenshot_TracksCall()
	{
		var fake = new FakeAppleProvider { ScreenshotResult = true };
		var result = fake.Screenshot("SIM-2", "/tmp/shot.png", Xamarin.MacDev.ScreenshotFormat.Jpeg);
		Assert.True(result);
		Assert.Single(fake.ScreenshotCalls);
		Assert.Equal(("SIM-2", "/tmp/shot.png", Xamarin.MacDev.ScreenshotFormat.Jpeg), fake.ScreenshotCalls[0]);
	}

	// --- JSON serialization ---

	[Fact]
	public void SimulatorPrivacyResult_SerializesToSnakeCase()
	{
		var model = new SimulatorPrivacyResult { Udid = "SIM-X", Action = "grant", Service = "photos", BundleIdentifier = "com.test.app", Success = true };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorPrivacyResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("SIM-X", root.GetProperty("udid").GetString());
		Assert.Equal("grant", root.GetProperty("action").GetString());
		Assert.Equal("photos", root.GetProperty("service").GetString());
		Assert.Equal("com.test.app", root.GetProperty("bundle_identifier").GetString());
		Assert.True(root.GetProperty("success").GetBoolean());
	}

	[Fact]
	public void SimulatorPrivacyResult_OmitsNullBundleIdentifier()
	{
		var model = new SimulatorPrivacyResult { Udid = "SIM-X", Action = "reset", Service = "photos", Success = true };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorPrivacyResult);
		using var doc = JsonDocument.Parse(json);
		Assert.False(doc.RootElement.TryGetProperty("bundle_identifier", out _));
	}

	[Fact]
	public void SimulatorAppearanceResult_SerializesToSnakeCase()
	{
		var model = new SimulatorAppearanceResult { Udid = "SIM-A", Appearance = "dark", Action = "get" };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorAppearanceResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("SIM-A", root.GetProperty("udid").GetString());
		Assert.Equal("dark", root.GetProperty("appearance").GetString());
		Assert.Equal("get", root.GetProperty("action").GetString());
	}

	[Fact]
	public void SimulatorLocationResult_SerializesToSnakeCase_WithCoordinates()
	{
		var model = new SimulatorLocationResult { Udid = "SIM-L", Action = "set", Latitude = 37.33, Longitude = -122.03, Success = true };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorLocationResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal(37.33, root.GetProperty("latitude").GetDouble());
		Assert.Equal(-122.03, root.GetProperty("longitude").GetDouble());
		Assert.False(root.TryGetProperty("gpx_path", out _));
	}

	[Fact]
	public void SimulatorScreenshotResult_SerializesToSnakeCase()
	{
		var model = new SimulatorScreenshotResult { Udid = "SIM-S", OutputPath = "/tmp/a.png", Format = "png", Success = true };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorScreenshotResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("/tmp/a.png", root.GetProperty("output_path").GetString());
		Assert.Equal("png", root.GetProperty("format").GetString());
	}

	// --- Error code mapping ---

	[Theory]
	[InlineData(ErrorCodes.AppleSimulatorPrivacyFailed, "E2215")]
	[InlineData(ErrorCodes.AppleSimulatorAppearanceFailed, "E2216")]
	[InlineData(ErrorCodes.AppleSimulatorStatusBarFailed, "E2217")]
	[InlineData(ErrorCodes.AppleSimulatorOpenUrlFailed, "E2218")]
	[InlineData(ErrorCodes.AppleSimulatorPushFailed, "E2219")]
	[InlineData(ErrorCodes.AppleSimulatorLocationFailed, "E2220")]
	[InlineData(ErrorCodes.AppleSimulatorAddMediaFailed, "E2221")]
	[InlineData(ErrorCodes.AppleSimulatorScreenshotFailed, "E2222")]
	[InlineData(ErrorCodes.AppleSimulatorRecordVideoFailed, "E2223")]
	public void SimulatorExtrasFailures_MapToPlatformCategory(string code, string expectedCode)
	{
		var error = ErrorResult.FromException(new MauiToolException(code, "boom"));
		Assert.Equal(expectedCode, error.Code);
		Assert.Equal("platform", error.Category);
	}

	// --- Handler-level tests (require macOS) ---

	[Fact]
	public async Task PrivacyGrantCommand_ValidUdid_CallsProvider()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-PRIV", IsAvailable = true });
				f.SetPrivacyResult = true;
			},
			"apple", "simulator", "privacy", "grant", "SIM-PRIV", "photos", "--bundle-id", "com.test.app", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.PrivacyCalls);
		Assert.Equal(("grant", "SIM-PRIV", Xamarin.MacDev.PrivacyPermission.Photos, (string?)"com.test.app"), fake.PrivacyCalls[0]);
	}

	[Fact]
	public async Task PrivacyGrantCommand_InvalidService_ReturnsInvalidArgument()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f => f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-PRIV", IsAvailable = true }),
			"apple", "simulator", "privacy", "grant", "SIM-PRIV", "camera", "--json");

		Assert.Equal(1, exitCode);
		Assert.Contains("E1004", stdout);
		Assert.Empty(fake.PrivacyCalls);
	}

	[Fact]
	public async Task AppearanceGetCommand_ReturnsCurrentAppearance()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-APP", IsAvailable = true });
				f.GetAppearanceResult = Xamarin.MacDev.SimulatorAppearance.Dark;
			},
			"apple", "simulator", "appearance", "get", "SIM-APP", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.GetAppearanceCalls);
		Assert.Contains("dark", stdout);
	}

	[Fact]
	public async Task AppearanceSetCommand_InvalidMode_ReturnsError()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		// "sepia" is not a valid subcommand (only "get", "light", "dark" are),
		// so System.CommandLine returns an error without reaching the handler.
		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f => f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-APP", IsAvailable = true }),
			"apple", "simulator", "appearance", "sepia", "SIM-APP", "--json");

		Assert.NotEqual(0, exitCode);
		Assert.Empty(fake.SetAppearanceCalls);
	}

	[Fact]
	public async Task OpenUrlCommand_ValidUdid_CallsProvider()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, _, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-URL", IsAvailable = true });
				f.OpenUrlResult = true;
			},
			"apple", "simulator", "openurl", "SIM-URL", "myapp://deeplink", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.OpenUrlCalls);
		Assert.Equal(("SIM-URL", "myapp://deeplink"), fake.OpenUrlCalls[0]);
	}

	[Fact]
	public async Task ScreenshotCommand_ValidUdid_CallsProviderWithFormat()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var output = Path.Combine(Path.GetTempPath(), $"shot_{Guid.NewGuid():N}.jpeg");
		var (exitCode, _, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-SHOT", IsAvailable = true });
				f.ScreenshotResult = true;
			},
			"apple", "simulator", "screenshot", "SIM-SHOT", output, "--format", "jpeg", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.ScreenshotCalls);
		Assert.Equal("SIM-SHOT", fake.ScreenshotCalls[0].Udid);
		Assert.Equal(Xamarin.MacDev.ScreenshotFormat.Jpeg, fake.ScreenshotCalls[0].Format);
	}

	[Fact]
	public async Task StatusBarOverrideCommand_NoOptions_ReturnsInvalidArgument()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, stdout, _, fake) = await InvokeSimulatorCommandAsync(
			f => f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-SB", IsAvailable = true }),
			"apple", "simulator", "status-bar", "override", "SIM-SB", "--json");

		Assert.Equal(1, exitCode);
		Assert.Contains("E1004", stdout);
		Assert.Empty(fake.StatusBarOverrideCalls);
	}

	[Fact]
	public async Task LocationSetCommand_ValidUdid_ForwardsCoordinates()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return; // xUnit v2 lacks Assert.Skip — shows as "passed" on non-macOS

		var (exitCode, _, _, fake) = await InvokeSimulatorCommandAsync(
			f =>
			{
				f.Simulators.Add(new SimulatorInfo { Name = "iPhone 16", Udid = "SIM-LOC", IsAvailable = true });
				f.SetLocationResult = true;
			},
			"apple", "simulator", "location", "set", "SIM-LOC", "37.3349", "-122.009", "--json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.SetLocationCalls);
		Assert.Equal("SIM-LOC", fake.SetLocationCalls[0].Udid);
		Assert.Equal(37.3349, fake.SetLocationCalls[0].Lat, 4);
		Assert.Equal(-122.009, fake.SetLocationCalls[0].Lng, 3);
	}
}
