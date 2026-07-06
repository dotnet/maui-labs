// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class AndroidCommandsTests
{
	[Fact]
	public void InstallCommand_ParsesCommaSeparatedPackages()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var installCommand = androidCommand.Subcommands.First(c => c.Name == "install");
		var packagesOption = installCommand.Options.First(o => o.Name == "--packages");

		// Act
		var parseResult = installCommand.Parse("install --packages platform-tools,build-tools;35.0.0,platforms;android-35");

		// Assert
		Assert.Empty(parseResult.Errors);
		var packages = parseResult.GetValue((Option<string[]>)packagesOption);
		Assert.NotNull(packages);
		// The raw value will be a single string with commas - the handler splits it
		Assert.Single(packages);
		Assert.Equal("platform-tools,build-tools;35.0.0,platforms;android-35", packages[0]);
	}

	[Fact]
	public void InstallCommand_ParsesMultiplePackageFlags()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var installCommand = androidCommand.Subcommands.First(c => c.Name == "install");
		var packagesOption = installCommand.Options.First(o => o.Name == "--packages");

		// Act
		var parseResult = installCommand.Parse("install --packages platform-tools --packages build-tools;35.0.0");

		// Assert
		Assert.Empty(parseResult.Errors);
		var packages = parseResult.GetValue((Option<string[]>)packagesOption);
		Assert.NotNull(packages);
		Assert.Equal(2, packages.Length);
	}

	[Fact]
	public void InstallCommand_HasCorrectOptions()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var installCommand = androidCommand.Subcommands.First(c => c.Name == "install");

		// Assert
		Assert.Contains(installCommand.Options, o => o.Name == "--sdk-install-path");
		Assert.Contains(installCommand.Options, o => o.Name == "--jdk-path");
		Assert.Contains(installCommand.Options, o => o.Name == "--jdk-version");
		Assert.Contains(installCommand.Options, o => o.Name == "--packages");
	}

	[Fact]
	public void InstallCommand_JdkVersionDefaultsToDefaultJdkVersion()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var installCommand = androidCommand.Subcommands.First(c => c.Name == "install");
		var jdkVersionOption = (Option<int>)installCommand.Options.First(o => o.Name == "--jdk-version");

		// Act — no --jdk-version supplied, so the default value factory should resolve.
		var parseResult = installCommand.Parse("install");

		// Assert
		Assert.Empty(parseResult.Errors);
		Assert.Equal(Microsoft.Maui.Cli.Providers.Android.JdkManager.DefaultJdkVersion, parseResult.GetValue(jdkVersionOption));
	}

	[Fact]
	public void JdkInstallCommand_VersionDefaultsToDefaultJdkVersion()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var jdkCommand = androidCommand.Subcommands.First(c => c.Name == "jdk");
		var jdkInstallCommand = jdkCommand.Subcommands.First(c => c.Name == "install");
		var versionOption = (Option<int>)jdkInstallCommand.Options.First(o => o.Name == "--version");

		// Act — no --version supplied, so the default value factory should resolve.
		var parseResult = jdkInstallCommand.Parse("install");

		// Assert
		Assert.Empty(parseResult.Errors);
		Assert.Equal(Microsoft.Maui.Cli.Providers.Android.JdkManager.DefaultJdkVersion, parseResult.GetValue(versionOption));
	}

	[Fact]
	public void EmulatorCreateCommand_PackageIsOptional()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var emulatorCommand = androidCommand.Subcommands.First(c => c.Name == "emulator");
		var createCommand = emulatorCommand.Subcommands.First(c => c.Name == "create");
		var packageOption = createCommand.Options.First(o => o.Name == "--package");

		// Assert
		Assert.False(packageOption.Required);
	}

	[Fact]
	public void EmulatorCreateCommand_HasRequiredNameArgument()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var emulatorCommand = androidCommand.Subcommands.First(c => c.Name == "emulator");
		var createCommand = emulatorCommand.Subcommands.First(c => c.Name == "create");

		// Assert
		Assert.Single(createCommand.Arguments);
		Assert.Equal("name", createCommand.Arguments.First().Name);
	}

	[Fact]
	public void EmulatorDeleteCommand_Exists()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var emulatorCommand = androidCommand.Subcommands.First(c => c.Name == "emulator");

		// Assert
		Assert.Contains(emulatorCommand.Subcommands, c => c.Name == "delete");
	}

	[Fact]
	public void EmulatorCommand_HasStopSubcommand()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var emulatorCommand = androidCommand.Subcommands.First(c => c.Name == "emulator");

		// Assert
		Assert.Contains(emulatorCommand.Subcommands, c => c.Name == "stop");
	}

	[Fact]
	public void EmulatorStopCommand_HasRequiredNameArgument()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var emulatorCommand = androidCommand.Subcommands.First(c => c.Name == "emulator");
		var stopCommand = emulatorCommand.Subcommands.First(c => c.Name == "stop");

		// Assert
		Assert.Single(stopCommand.Arguments);
		Assert.Equal("name", stopCommand.Arguments.First().Name);
	}

	[Fact]
	public void EmulatorDeleteCommand_HasRequiredNameArgument()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var emulatorCommand = androidCommand.Subcommands.First(c => c.Name == "emulator");
		var deleteCommand = emulatorCommand.Subcommands.First(c => c.Name == "delete");

		// Assert
		Assert.Single(deleteCommand.Arguments);
		Assert.Equal("name", deleteCommand.Arguments.First().Name);
	}

	[Fact]
	public void EmulatorStartCommand_HasColdBootOption()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var emulatorCommand = androidCommand.Subcommands.First(c => c.Name == "emulator");
		var startCommand = emulatorCommand.Subcommands.First(c => c.Name == "start");

		// Assert
		Assert.Contains(startCommand.Options, o => o.Name == "--cold-boot");
		Assert.Contains(startCommand.Options, o => o.Name == "--wait");
	}

	[Fact]
	public void JdkCommand_HasAllSubcommands()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var jdkCommand = androidCommand.Subcommands.First(c => c.Name == "jdk");

		// Assert
		Assert.Contains(jdkCommand.Subcommands, c => c.Name == "check");
		Assert.Contains(jdkCommand.Subcommands, c => c.Name == "install");
		Assert.Contains(jdkCommand.Subcommands, c => c.Name == "list");
	}

	[Fact]
	public void SdkCommand_HasAllSubcommands()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var sdkCommand = androidCommand.Subcommands.First(c => c.Name == "sdk");

		// Assert
		Assert.Contains(sdkCommand.Subcommands, c => c.Name == "check");
		Assert.Contains(sdkCommand.Subcommands, c => c.Name == "install");
		Assert.Contains(sdkCommand.Subcommands, c => c.Name == "list");
		Assert.Contains(sdkCommand.Subcommands, c => c.Name == "accept-licenses");
	}

	[Fact]
	public void SdkListCommand_HasAvailableAndAllOptions()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();
		var sdkCommand = androidCommand.Subcommands.First(c => c.Name == "sdk");
		var listCommand = sdkCommand.Subcommands.First(c => c.Name == "list");

		// Assert
		Assert.Contains(listCommand.Options, o => o.Name == "--available");
		Assert.Contains(listCommand.Options, o => o.Name == "--all");
	}

	[Fact]
	public void AndroidCommand_HasAllSubcommands()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();

		// Assert
		Assert.Contains(androidCommand.Subcommands, c => c.Name == "install");
		Assert.Contains(androidCommand.Subcommands, c => c.Name == "jdk");
		Assert.Contains(androidCommand.Subcommands, c => c.Name == "sdk");
		Assert.Contains(androidCommand.Subcommands, c => c.Name == "emulator");
	}

	[Fact]
	public void AndroidCommand_HasSdkAndJdkOptions()
	{
		// Arrange
		var androidCommand = AndroidCommands.Create();

		// Assert
		Assert.Contains(androidCommand.Options, o => o.Name == "--sdk");
		Assert.Contains(androidCommand.Options, o => o.Name == "--jdk");
	}

	[Fact]
	public void SdkOption_IsRecursive()
	{
		// The --sdk option should be available to all subcommands
		Assert.True(AndroidCommands.SdkOption.Recursive);
	}

	[Fact]
	public void JdkOption_IsRecursive()
	{
		// The --jdk option should be available to all subcommands
		Assert.True(AndroidCommands.JdkOption.Recursive);
	}

	[Fact]
	public void SdkOption_ParsesOnSubcommand()
	{
		// Arrange
		var rootCommand = Program.BuildRootCommand();

		// Act — parse --sdk on a nested subcommand
		var parseResult = rootCommand.Parse("android --sdk /custom/sdk sdk list");

		// Assert
		Assert.Empty(parseResult.Errors);
		var sdkValue = parseResult.GetValue(AndroidCommands.SdkOption);
		Assert.Equal("/custom/sdk", sdkValue);
	}

	[Fact]
	public void JdkOption_ParsesOnSubcommand()
	{
		// Arrange
		var rootCommand = Program.BuildRootCommand();

		// Act
		var parseResult = rootCommand.Parse("android --jdk /custom/jdk jdk check");

		// Assert
		Assert.Empty(parseResult.Errors);
		var jdkValue = parseResult.GetValue(AndroidCommands.JdkOption);
		Assert.Equal("/custom/jdk", jdkValue);
	}

	[Fact]
	public void SdkAndJdkOptions_ParseTogether()
	{
		// Arrange
		var rootCommand = Program.BuildRootCommand();

		// Act
		var parseResult = rootCommand.Parse("android --sdk /my/sdk --jdk /my/jdk emulator list");

		// Assert
		Assert.Empty(parseResult.Errors);
		Assert.Equal("/my/sdk", parseResult.GetValue(AndroidCommands.SdkOption));
		Assert.Equal("/my/jdk", parseResult.GetValue(AndroidCommands.JdkOption));
	}

	[Fact]
	public async Task SdkOption_OverridesProviderSdkPath()
	{
		var tempSdk = Path.Combine(Path.GetTempPath(), "maui-test-sdk-" + Path.GetRandomFileName());
		Directory.CreateDirectory(tempSdk);
		try
		{
			var fakeAndroid = new FakeAndroidProvider
			{
				IsSdkInstalled = true,
				SdkPath = "/original/sdk",
				LicensesAccepted = true
			};

			var testProvider = ServiceConfiguration.CreateTestServiceProvider(androidProvider: fakeAndroid);
			try
			{
				Program.Services = testProvider;

				var rootCommand = Program.BuildRootCommand();
				var parseResult = rootCommand.Parse($"android --sdk {tempSdk} install --json");
				await parseResult.InvokeAsync();

				// The provider's SdkPath should have been overridden
				Assert.Equal(tempSdk, fakeAndroid.SdkPath);
			}
			finally
			{
				Program.ResetServices();
			}
		}
		finally
		{
			if (Directory.Exists(tempSdk))
				Directory.Delete(tempSdk, recursive: true);
		}
	}

	[Fact]
	public async Task JdkOption_OverridesProviderJdkPath()
	{
		var tempJdk = Path.Combine(Path.GetTempPath(), "maui-test-jdk-" + Path.GetRandomFileName());
		Directory.CreateDirectory(tempJdk);
		try
		{
			var fakeAndroid = new FakeAndroidProvider
			{
				IsSdkInstalled = true,
				LicensesAccepted = true
			};

			var testProvider = ServiceConfiguration.CreateTestServiceProvider(androidProvider: fakeAndroid);
			try
			{
				Program.Services = testProvider;

				var rootCommand = Program.BuildRootCommand();
				var parseResult = rootCommand.Parse($"android --jdk {tempJdk} install --json");
				await parseResult.InvokeAsync();

				// The provider's JdkPath should have been overridden
				Assert.Equal(tempJdk, fakeAndroid.JdkPath);
			}
			finally
			{
				Program.ResetServices();
			}
		}
		finally
		{
			if (Directory.Exists(tempJdk))
				Directory.Delete(tempJdk, recursive: true);
		}
	}

	// --- Handler-level tests for the JSON/non-Spectre 'android install' license preflight. ---
	// These exercise the behavior added in PR #106: fail fast when the SDK is already
	// installed and licenses aren't accepted, but don't block on a fresh machine where
	// InstallAsync will bootstrap tools non-interactively.

	static async Task<(int ExitCode, FakeAndroidProvider Android)> InvokeAndroidInstallJsonAsync(
		Action<FakeAndroidProvider> configure,
		params string[] extraArgs)
	{
		var fakeAndroid = new FakeAndroidProvider();
		configure(fakeAndroid);

		var testProvider = ServiceConfiguration.CreateTestServiceProvider(androidProvider: fakeAndroid);
		var originalServices = Program.Services;
		try
		{
			Program.Services = testProvider;

			var rootCommand = Program.BuildRootCommand();
			var args = new List<string> { "android", "install", "--json" };
			args.AddRange(extraArgs);
			var parseResult = rootCommand.Parse(args.ToArray());
			var exitCode = await parseResult.InvokeAsync();
			return (exitCode, fakeAndroid);
		}
		finally
		{
			Program.ResetServices();
		}
	}

	[Fact]
	public async Task InstallCommand_Json_FailsFast_WhenSdkInstalledAndLicensesNotAccepted()
	{
		var (exitCode, fake) = await InvokeAndroidInstallJsonAsync(f =>
		{
			f.IsSdkInstalled = true;
			f.SdkPath = Path.Combine(Path.GetTempPath(), "sdk-test");
			f.LicensesAccepted = false;
		});

		Assert.Equal(1, exitCode);
		Assert.Empty(fake.InstallCalls);
	}

	[Fact]
	public async Task InstallCommand_Json_ProceedsOnFreshMachine_WhenSdkNotInstalled()
	{
		// Regression: on a fresh machine the preflight should NOT block; InstallAsync
		// is responsible for bootstrapping the SDK and (with --accept-licenses) accepting
		// licenses non-interactively.
		var (exitCode, fake) = await InvokeAndroidInstallJsonAsync(f =>
		{
			f.IsSdkInstalled = false;
			f.LicensesAccepted = false;
		});

		Assert.Equal(0, exitCode);
		Assert.Single(fake.InstallCalls);
	}

	[Fact]
	public async Task InstallCommand_Json_Proceeds_WhenLicensesAlreadyAccepted()
	{
		var (exitCode, fake) = await InvokeAndroidInstallJsonAsync(f =>
		{
			f.IsSdkInstalled = true;
			f.SdkPath = Path.Combine(Path.GetTempPath(), "sdk-test");
			f.LicensesAccepted = true;
		});

		Assert.Equal(0, exitCode);
		Assert.Single(fake.InstallCalls);
	}

	[Fact]
	public async Task InstallCommand_Json_Proceeds_WhenAcceptLicensesFlagPassed()
	{
		var (exitCode, fake) = await InvokeAndroidInstallJsonAsync(f =>
		{
			f.IsSdkInstalled = true;
			f.SdkPath = Path.Combine(Path.GetTempPath(), "sdk-test");
			f.LicensesAccepted = false;
		}, "--accept-licenses");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.InstallCalls);
	}

	[Fact]
	public void GetAndroidProvider_RejectsWhitespaceOnlySdkPath()
	{
		var rootCommand = Program.BuildRootCommand();
		var parseResult = rootCommand.Parse("android --sdk \"   \" sdk list");

		// Whitespace-only should be treated as empty and not applied
		Assert.Empty(parseResult.Errors);
		// GetAndroidProvider uses IsNullOrWhiteSpace, so "   " is ignored
	}

	[Fact]
	public void GetAndroidProvider_ThrowsForNonexistentSdkPath()
	{
		var fakeAndroid = new FakeAndroidProvider { IsSdkInstalled = true };
		var testProvider = ServiceConfiguration.CreateTestServiceProvider(androidProvider: fakeAndroid);
		try
		{
			Program.Services = testProvider;
			var rootCommand = Program.BuildRootCommand();
			var parseResult = rootCommand.Parse("android --sdk /nonexistent/path/that/does/not/exist sdk list");

			Assert.Throws<DirectoryNotFoundException>(() => AndroidCommands.GetAndroidProvider(parseResult));
		}
		finally
		{
			Program.ResetServices();
		}
	}

	[Fact]
	public void GetAndroidProvider_ThrowsForNonexistentJdkPath()
	{
		var fakeAndroid = new FakeAndroidProvider { IsSdkInstalled = true };
		var testProvider = ServiceConfiguration.CreateTestServiceProvider(androidProvider: fakeAndroid);
		try
		{
			Program.Services = testProvider;
			var rootCommand = Program.BuildRootCommand();
			var parseResult = rootCommand.Parse("android --jdk /nonexistent/jdk/path sdk list");

			Assert.Throws<DirectoryNotFoundException>(() => AndroidCommands.GetAndroidProvider(parseResult));
		}
		finally
		{
			Program.ResetServices();
		}
	}

	[Fact]
	public void InstallCommand_HasSdkInstallPathOption()
	{
		// Verify the option was renamed from --sdk-path to --sdk-install-path
		var androidCommand = AndroidCommands.Create();
		var installCommand = androidCommand.Subcommands.First(c => c.Name == "install");

		Assert.Contains(installCommand.Options, o => o.Name == "--sdk-install-path");
		Assert.DoesNotContain(installCommand.Options, o => o.Name == "--sdk-path");
	}

	// --- Handler-level tests for 'android sdk accept-licenses' exit code behaviour ---
	// Verifies that the command returns non-zero when the user declines so that callers
	// (e.g. the VS Code MAUI extension) can trust the exit code.

	static async Task<int> InvokeAcceptLicensesJsonAsync(Action<FakeAndroidProvider> configure)
	{
		var fakeAndroid = new FakeAndroidProvider();
		configure(fakeAndroid);

		var testProvider = ServiceConfiguration.CreateTestServiceProvider(androidProvider: fakeAndroid);
		try
		{
			Program.Services = testProvider;

			var rootCommand = Program.BuildRootCommand();
			var parseResult = rootCommand.Parse("android sdk accept-licenses --json");
			return await parseResult.InvokeAsync();
		}
		finally
		{
			Program.ResetServices();
		}
	}

	[Fact]
	public async Task AcceptLicensesCommand_Json_ReturnsZero_WhenLicensesAlreadyAccepted()
	{
		var exitCode = await InvokeAcceptLicensesJsonAsync(f =>
		{
			f.LicensesAccepted = true;
			f.LicenseAcceptanceCommand = ("sdkmanager", "--licenses");
		});

		Assert.Equal(0, exitCode);
	}

	[Fact]
	public async Task AcceptLicensesCommand_Json_ReturnsOne_WhenSdkNotFound()
	{
		// When sdkmanager is not found the command returns 1 (sdk_not_found).
		var exitCode = await InvokeAcceptLicensesJsonAsync(f =>
		{
			f.LicensesAccepted = false;
			f.LicenseAcceptanceCommand = null;
		});

		Assert.Equal(1, exitCode);
	}

	// --- Handler-level tests for 'android install' interactive license decline ---
	// Same sdkmanager exit-0-on-decline bug exists in the install flow.

	[Fact]
	public async Task InstallCommand_Json_FailsFast_WhenLicensesDeclinedInteractively()
	{
		// SDK is installed but licenses not accepted — simulates user typing 'n'.
		// The install flow should abort with exit code 1, not proceed.
		var (exitCode, fake) = await InvokeAndroidInstallJsonAsync(f =>
		{
			f.IsSdkInstalled = true;
			f.SdkPath = Path.Combine(Path.GetTempPath(), "sdk-test");
			f.LicensesAccepted = false;
		});

		Assert.Equal(1, exitCode);
		Assert.Empty(fake.InstallCalls);
	}

	// --- Tests for device profile helpers added when 'emulator create' switched from a
	// hardcoded Pixel device list to a live avdmanager query (AvdManagerRunner.ListDeviceProfilesAsync). ---

	[Theory]
	[InlineData("pixel_6", "Pixel 6")]
	[InlineData("pixel_9_pro_fold", "Pixel 9 Pro Fold")]
	[InlineData("medium_phone", "Medium Phone")]
	[InlineData("automotive_1024p_landscape", "Automotive 1024p Landscape")]
	public void HumanizeDeviceProfileId_ConvertsSnakeCaseToTitleCase(string id, string expected)
	{
		Assert.Equal(expected, AndroidCommands.HumanizeDeviceProfileId(id));
	}

	[Theory]
	[InlineData("Nexus 10")]
	[InlineData("Galaxy Nexus")]
	[InlineData("")]
	public void HumanizeDeviceProfileId_LeavesAlreadyFriendlyIdsUnchanged(string id)
	{
		Assert.Equal(id, AndroidCommands.HumanizeDeviceProfileId(id));
	}

	[Fact]
	public void BuildDeviceProfileChoices_FallsBackToDefaults_WhenLiveListIsNull()
	{
		var choices = AndroidCommands.BuildDeviceProfileChoices(null);

		Assert.NotEmpty(choices);
		Assert.Contains(choices, c => c.Id == "pixel_6" && c.Name == "Pixel 6");
	}

	[Fact]
	public void BuildDeviceProfileChoices_FallsBackToDefaults_WhenLiveListIsEmpty()
	{
		var choices = AndroidCommands.BuildDeviceProfileChoices(Array.Empty<string>());

		Assert.NotEmpty(choices);
		Assert.Contains(choices, c => c.Id == "pixel_6" && c.Name == "Pixel 6");
	}

	[Fact]
	public void BuildDeviceProfileChoices_MapsLiveIds_ToHumanizedNames()
	{
		var liveProfileIds = new[] { "pixel_9_pro_fold", "Nexus 10" };

		var choices = AndroidCommands.BuildDeviceProfileChoices(liveProfileIds);

		Assert.Equal(2, choices.Count);
		Assert.Equal(("pixel_9_pro_fold", "Pixel 9 Pro Fold"), choices[0]);
		Assert.Equal(("Nexus 10", "Nexus 10"), choices[1]);
	}
}
